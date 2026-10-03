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
    private readonly ClipboardService _clipboard;
    private readonly AuthRateLimiter _rateLimiter;
    private WebApplication? _app;

    public HatoclipServer(ConfigStore config, ClipboardService clipboard, AuthRateLimiter rateLimiter)
    {
        _config = config;
        _clipboard = clipboard;
        _rateLimiter = rateLimiter;
    }

    /// <summary>Raised (on a server thread) after text from the phone has been placed on the PC clipboard.</summary>
    public event EventHandler<string>? TextReceived;

    public int Port { get; private set; }

    public bool IsRunning => _app is not null;

    /// <summary>Starts listening on the configured port, or the next free port above it (persisted to config).</summary>
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
            catch (IOException ex)
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
                _config.Update(c => c.Port = port);
                Log.Info($"Configured port {firstPort} was in use; saved port {port} to config.");
            }

            Log.Info($"Server started on 0.0.0.0:{port}.");
            return;
        }

        throw new IOException($"No free port in range {firstPort}-{lastPort}.", lastError);
    }

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
            options.ListenAnyIP(port);
        });

        // Bind with SO_EXCLUSIVEADDRUSE so that (a) a port already held by another process reliably
        // fails to bind (Windows otherwise lets some sockets share a wildcard port) and we move on to
        // the next port, and (b) no other process can bind on top of our port while we run.
        builder.WebHost.UseSockets(o => o.CreateBoundListenSocket = CreateExclusiveListenSocket);

        var app = builder.Build();

        app.Use(LogRequestsAsync);
        app.Use(HandleErrorsAsync);
        app.Use(AuthenticateAsync);

        app.MapMethods("/{**path}", ["GET", "HEAD"], (Delegate)ServeStatic);
        app.MapGet("/api/ping", Ping);
        app.MapPost("/api/clipboard", (Delegate)PostClipboardAsync);
        app.MapGet("/api/clipboard", GetClipboardAsync);

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

    private static string SanitizeLinkUrl(string? value)
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
            if (ip.Address.Equals(IPAddress.IPv6Any))
            {
                socket.DualMode = true; // same as Kestrel's default: [::] also accepts IPv4
            }

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

    private async Task AuthenticateAsync(HttpContext ctx, RequestDelegate next)
    {
        if (!ctx.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            await next(ctx);
            return;
        }

        var ip = ctx.Connection.RemoteIpAddress;
        if (_rateLimiter.IsBlocked(ip))
        {
            await Json(429, new { ok = false, error = "too-many-requests" }).ExecuteAsync(ctx);
            return;
        }

        if (!IsValidKey(ctx.Request.Headers[KeyHeader].ToString()))
        {
            var nowBlocked = _rateLimiter.RecordFailure(ip);
            Log.Warn($"Auth failure from {RemoteIp(ctx)} on {ctx.Request.Path}.");
            if (nowBlocked)
            {
                Log.Warn($"Blocking {RemoteIp(ctx)} for {AuthRateLimiter.BlockDuration.TotalMinutes:0} minutes after repeated auth failures.");
            }

            await Json(401, new { ok = false, error = "unauthorized" }).ExecuteAsync(ctx);
            return;
        }

        _rateLimiter.RecordSuccess(ip);
        await next(ctx);
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

        try
        {
            await _clipboard.SetTextAsync(text);
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
}
