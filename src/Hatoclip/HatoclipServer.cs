using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets;
using Microsoft.Extensions.Logging;

namespace Hatoclip;

/// <summary>
/// In-process Kestrel server: serves the embedded web page and the clipboard API on 0.0.0.0:&lt;port&gt;.
/// </summary>
public sealed class HatoclipServer : IAsyncDisposable
{
    public const int MaxBodyBytes = 1024 * 1024;
    public const int PortSearchRange = 20;
    public const string KeyHeader = "X-Hatoclip-Key";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    private const string Html = "text/html; charset=utf-8";

    /// <summary>
    /// CSP for the page. Scripts come only from /app.js. 'unsafe-inline' for styles is needed for the
    /// style="fill: var(...)" attributes of the inline SVG pigeon sprite (presentation attributes
    /// cannot use CSS variables, and styling &lt;use&gt; clones from style.css is unreliable).
    /// </summary>
    private const string ContentSecurityPolicy =
        "default-src 'self'; style-src 'self' 'unsafe-inline'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
    private const string Png = "image/png";

    /// <summary>
    /// Public (no key) static files: request path → (embedded resource logical name, content type).
    /// Web/** is embedded as "Hatoclip.Web.&lt;file&gt;", assets/*.ico as "Hatoclip.assets.&lt;file&gt;".
    /// </summary>
    private static readonly Dictionary<string, (string Resource, string ContentType)> StaticFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["/"] = ("Hatoclip.Web.index.html", Html),
        ["/index.html"] = ("Hatoclip.Web.index.html", Html),
        ["/app.js"] = ("Hatoclip.Web.app.js", "text/javascript; charset=utf-8"),
        ["/style.css"] = ("Hatoclip.Web.style.css", "text/css; charset=utf-8"),
        ["/manifest.webmanifest"] = ("Hatoclip.Web.manifest.webmanifest", "application/manifest+json; charset=utf-8"),
        ["/icon.png"] = ("Hatoclip.Web.icon.png", Png),
        ["/icon-192.png"] = ("Hatoclip.Web.icon-192.png", Png),
        ["/apple-touch-icon.png"] = ("Hatoclip.Web.apple-touch-icon.png", Png),
        // Same pigeon as the tray icon.
        ["/favicon.ico"] = ("Hatoclip.assets.hatoclip.ico", "image/x-icon"),
    };

    private readonly ConfigStore _config;
    private readonly IClipboardService _clipboard;
    private readonly AuthRateLimiter _rateLimiter;
    private readonly IPAddress _listenAddress;
    private WebApplication? _app;

    /// <param name="listenAddress">
    /// Address to listen on; null = 0.0.0.0 (IPv4 any). Tests pass loopback so that no firewall prompt appears.
    /// </param>
    public HatoclipServer(ConfigStore config, IClipboardService clipboard, AuthRateLimiter rateLimiter, IPAddress? listenAddress = null)
    {
        _config = config;
        _clipboard = clipboard;
        _rateLimiter = rateLimiter;
        _listenAddress = listenAddress ?? IPAddress.Any;
    }

    /// <summary>Raised (on a server thread) after text from the phone has been placed on the PC clipboard.</summary>
    public event EventHandler<string>? TextReceived;

    /// <summary>
    /// Raised (on a server thread) after a phone has read the PC clipboard (200 response), so the PC
    /// can show that it happened. Carries no content.
    /// </summary>
    public event EventHandler? ClipboardRead;

    public int Port { get; private set; }

    public bool IsRunning => _app is not null;

    /// <summary>
    /// Starts listening on the configured port or, if that is unavailable, on the next free port above
    /// it for this run only. The fallback is never saved, so the next start tries the usual port again
    /// and bookmarks / Shortcuts work again once the collision is gone.
    /// </summary>
    public async Task StartAsync()
    {
        if (_app is not null)
        {
            return;
        }

        var firstPort = _config.Current.Port;
        var lastPort = Math.Min(firstPort + PortSearchRange, 65535);
        Exception? lastError = null;

        for (var port = firstPort; port <= lastPort; port++)
        {
            var app = Build(port);
            try
            {
                await app.StartAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (IsBindFailure(ex))
            {
                lastError = ex;
                Log.Warn($"Port {port} is unavailable ({ex.GetBaseException().Message}); trying next port.");
                await app.DisposeAsync().ConfigureAwait(false);
                continue;
            }

            _app = app;
            Port = port;
            if (port != firstPort)
            {
                Log.Warn($"Configured port {firstPort} was unavailable; using port {port} for this run only (not saved).");
            }

            Log.Info($"Server started on {_listenAddress}:{port}.");
            return;
        }

        throw new IOException($"No free port in range {firstPort}-{lastPort}.", lastError);
    }

    /// <summary>
    /// A port that cannot be bound: in use (Kestrel wraps that in an IOException) or refused by the
    /// OS, e.g. access denied (WSAEACCES) for a port in a Hyper-V/WSL/Docker excluded port range.
    /// Anything else (configuration errors etc.) is not a bind failure and must fail startup.
    /// </summary>
    private static bool IsBindFailure(Exception ex) =>
        ex is IOException or SocketException || ex.GetBaseException() is SocketException;

    public async Task StopAsync()
    {
        var app = _app;
        if (app is null)
        {
            return;
        }

        _app = null;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await app.StopAsync(cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error("Error while stopping server.", ex);
        }
        finally
        {
            await app.DisposeAsync().ConfigureAwait(false);
            Log.Info("Server stopped.");
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private WebApplication Build(int port)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            Args = [],
            ContentRootPath = AppContext.BaseDirectory,
        });

        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Logging.AddProvider(new FileLoggerProvider());
        // Bind failures during the port search are logged by StartAsync; skip the host's duplicate stack traces.
        builder.Logging.AddFilter("Microsoft.Extensions.Hosting.Internal.Host", LogLevel.None);

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.AddServerHeader = false;
            // IPv4 only (0.0.0.0). ListenAnyIP would bind [::] dual-mode and also accept IPv6.
            options.Listen(_listenAddress, port);
        });

        // Bind with SO_EXCLUSIVEADDRUSE so that (a) a port already held by another process reliably
        // fails to bind (Windows otherwise lets some sockets share a wildcard port) and we move on to
        // the next port, and (b) no other process can bind on top of our port while we run.
        builder.WebHost.UseSockets(o => o.CreateBoundListenSocket = CreateExclusiveListenSocket);

        var app = builder.Build();

        // WebApplication runs routing before this pipeline, so the middleware below can see the
        // matched endpoint and its metadata.
        app.Use(LogRequestsAsync);
        app.Use(AddSecurityHeadersAsync);
        app.Use(HandleErrorsAsync);
        app.Use(AuthenticateAsync);

        app.MapMethods("/{**path}", ["GET", "HEAD"], (Delegate)ServeStatic);

        // Every endpoint in this group requires the key (see AuthenticateAsync), including ones
        // added later. The catch-all answers unknown /api paths with 404 only after authentication,
        // so a client without the key gets 401 for every /api path and cannot probe which exist.
        var api = app.MapGroup("/api").WithMetadata(RequiresKey.Instance);
        api.MapGet("/ping", Ping);
        api.MapPost("/clipboard", (Delegate)PostClipboardAsync);
        api.MapGet("/clipboard", GetClipboardAsync);
        api.Map("/{**rest}", () => Json(404, new { ok = false, error = "not-found" }));

        return app;
    }

    /// <summary>
    /// Connection check. Also hands the page the iOS Shortcut links from config.json (read from the
    /// live config on every request); anything that is not an absolute http/https URL is sent as "".
    /// </summary>
    private IResult Ping()
    {
        var config = _config.Current;
        return Json(200, new
        {
            ok = true,
            pcName = Environment.MachineName,
            shortcutSendUrl = SanitizeLinkUrl(config.ShortcutSendUrl),
            shortcutReceiveUrl = SanitizeLinkUrl(config.ShortcutReceiveUrl),
        });
    }

    internal static string SanitizeLinkUrl(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed)
            || !Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return "";
        }

        return uri.AbsoluteUri;
    }

    private static Socket CreateExclusiveListenSocket(EndPoint endpoint)
    {
        if (endpoint is not IPEndPoint ip)
        {
            return SocketTransportOptions.CreateDefaultBoundListenSocket(endpoint);
        }

        var socket = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            socket.ExclusiveAddressUse = true;
            socket.Bind(ip);
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    // ---- middleware -------------------------------------------------------------------------

    private static async Task LogRequestsAsync(HttpContext ctx, RequestDelegate next)
    {
        try
        {
            await next(ctx);
        }
        finally
        {
            // Path only: never the query string, headers or body.
            Log.Info($"{ctx.Request.Method} {ctx.Request.Path} -> {ctx.Response.StatusCode} ({RemoteIp(ctx)})");
        }
    }

    private static async Task HandleErrorsAsync(HttpContext ctx, RequestDelegate next)
    {
        try
        {
            await next(ctx);
        }
        catch (Exception ex) when (!ctx.RequestAborted.IsCancellationRequested)
        {
            Log.Error($"Unhandled error for {ctx.Request.Method} {ctx.Request.Path}.", ex);
            if (!ctx.Response.HasStarted)
            {
                ctx.Response.Clear();
                await Json(500, new { ok = false, error = "internal-error" }).ExecuteAsync(ctx);
            }
        }
    }

    /// <summary>
    /// Security headers on every response, added just before the headers are sent so that they
    /// survive HandleErrorsAsync clearing the response.
    /// </summary>
    private static Task AddSecurityHeadersAsync(HttpContext ctx, RequestDelegate next)
    {
        ctx.Response.OnStarting(() =>
        {
            var headers = ctx.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers["Referrer-Policy"] = "no-referrer";
            headers.ContentSecurityPolicy = ContentSecurityPolicy;
            if (ctx.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
            {
                headers.CacheControl = "no-store";
            }

            return Task.CompletedTask;
        });
        return next(ctx);
    }

    /// <summary>
    /// Key check for endpoints carrying <see cref="RequiresKey"/> (the /api group). The key is
    /// checked first, so a phone with the right key always gets through, even while its IP is
    /// blocked because of wrong guesses from the same address (the block itself stays for wrong keys).
    /// </summary>
    private async Task AuthenticateAsync(HttpContext ctx, RequestDelegate next)
    {
        if (ctx.GetEndpoint()?.Metadata.GetMetadata<RequiresKey>() is null)
        {
            await next(ctx);
            return;
        }

        // There is no CORS support: OPTIONS (e.g. a browser preflight) gets a bare 204 without CORS
        // headers, so a cross-origin request still fails. It is never counted as an auth failure.
        if (HttpMethods.IsOptions(ctx.Request.Method))
        {
            ctx.Response.StatusCode = StatusCodes.Status204NoContent;
            return;
        }

        var ip = ctx.Connection.RemoteIpAddress;
        if (IsValidKey(ctx.Request.Headers[KeyHeader].ToString()))
        {
            _rateLimiter.RecordSuccess(ip);
            await next(ctx);
            return;
        }

        if (_rateLimiter.IsBlocked(ip))
        {
            await Json(429, new { ok = false, error = "too-many-requests" }).ExecuteAsync(ctx);
            return;
        }

        var nowBlocked = _rateLimiter.RecordFailure(ip);
        Log.Warn($"Auth failure from {RemoteIp(ctx)} on {ctx.Request.Path}.");
        if (nowBlocked)
        {
            Log.Warn($"Blocking {RemoteIp(ctx)} for {AuthRateLimiter.BlockDuration.TotalMinutes:0} minutes after repeated auth failures.");
        }

        await Json(401, new { ok = false, error = "unauthorized" }).ExecuteAsync(ctx);
    }

    private bool IsValidKey(string supplied)
    {
        if (supplied.Length == 0)
        {
            return false;
        }

        // Read per request (under the config lock) so a regenerated key applies without a restart.
        var expected = Encoding.UTF8.GetBytes(_config.Key);
        var actual = Encoding.UTF8.GetBytes(supplied);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    // ---- endpoints --------------------------------------------------------------------------

    private static IResult ServeStatic(HttpContext ctx)
    {
        if (!StaticFiles.TryGetValue(ctx.Request.Path.Value ?? "/", out var entry))
        {
            return Results.NotFound();
        }

        var bytes = EmbeddedAssets.Get(entry.Resource);
        if (bytes is null)
        {
            return Results.NotFound();
        }

        ctx.Response.Headers.CacheControl = "no-cache";
        return Results.Bytes(bytes, entry.ContentType);
    }

    private async Task<IResult> PostClipboardAsync(HttpContext ctx)
    {
        if (ctx.Request.ContentLength > MaxBodyBytes)
        {
            return TooLarge();
        }

        var body = await ReadBodyAsync(ctx.Request.Body, MaxBodyBytes, ctx.RequestAborted);
        if (body is null)
        {
            return TooLarge();
        }

        string text;
        if (IsJson(ctx.Request.ContentType))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                    !doc.RootElement.TryGetProperty("text", out var textElement) ||
                    textElement.ValueKind != JsonValueKind.String)
                {
                    return Json(400, new { ok = false, error = "bad-request" });
                }

                text = textElement.GetString() ?? "";
            }
            catch (JsonException)
            {
                return Json(400, new { ok = false, error = "bad-request" });
            }
        }
        else
        {
            // text/plain (and anything else that is not JSON) is treated as raw UTF-8 text.
            text = DecodeUtf8(body);
        }

        // An empty body must not wipe the PC clipboard (e.g. a Shortcut run with an empty clipboard).
        if (text.Length == 0)
        {
            return Json(400, new { ok = false, error = "empty" });
        }

        try
        {
            await _clipboard.SetTextAsync(text, _config.Current.ExcludeFromHistory);
        }
        catch (ExternalException ex)
        {
            Log.Warn($"Clipboard is busy; could not set text ({ex.GetType().Name}).");
            return Json(503, new { ok = false, error = "clipboard-busy" });
        }

        try
        {
            TextReceived?.Invoke(this, text);
        }
        catch (Exception ex)
        {
            Log.Error("TextReceived handler failed.", ex);
        }

        return Json(200, new { ok = true, length = text.Length });
    }

    private async Task<IResult> GetClipboardAsync()
    {
        if (!_config.Current.AllowGet)
        {
            return Json(403, new { ok = false, error = "get-disabled" });
        }

        try
        {
            var (text, type) = await _clipboard.GetTextAsync();
            try
            {
                ClipboardRead?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Log.Error("ClipboardRead handler failed.", ex);
            }

            return Json(200, new { ok = true, text, type });
        }
        catch (ExternalException ex)
        {
            Log.Warn($"Clipboard is busy; could not read text ({ex.GetType().Name}).");
            return Json(503, new { ok = false, error = "clipboard-busy" });
        }
    }

    // ---- helpers ----------------------------------------------------------------------------

    private static IResult Json(int status, object value) =>
        Results.Json(value, JsonOptions, "application/json; charset=utf-8", status);

    private static IResult TooLarge() => Json(413, new { ok = false, error = "too-large" });

    private static bool IsJson(string? contentType) =>
        contentType is not null &&
        (contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) ||
         contentType.Contains("+json", StringComparison.OrdinalIgnoreCase));

    /// <summary>Reads at most <paramref name="limit"/> bytes; returns null if the body is larger.</summary>
    private static async Task<byte[]?> ReadBodyAsync(Stream body, int limit, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await body.ReadAsync(buffer, ct);
            if (read == 0)
            {
                return ms.ToArray();
            }

            if (ms.Length + read > limit)
            {
                return null;
            }

            ms.Write(buffer, 0, read);
        }
    }

    private static string DecodeUtf8(byte[] bytes)
    {
        ReadOnlySpan<byte> span = bytes;
        if (span.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            span = span[3..];
        }

        return Encoding.UTF8.GetString(span);
    }

    private static string RemoteIp(HttpContext ctx)
    {
        var ip = ctx.Connection.RemoteIpAddress;
        if (ip is null)
        {
            return "unknown";
        }

        return (ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip).ToString();
    }

    /// <summary>Endpoint metadata: the request must carry the connection key.</summary>
    private sealed class RequiresKey
    {
        public static readonly RequiresKey Instance = new();
    }
}
