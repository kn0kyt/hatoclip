using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Hatoclip.Tests;

/// <summary>
/// End-to-end tests against a real Kestrel instance on 127.0.0.1 (loopback only, so no firewall
/// prompt) with an in-memory clipboard.
/// </summary>
public sealed class HttpApiTests : IAsyncLifetime
{
    private readonly TempDirectory _dir = new();
    private readonly FakeClipboard _clipboard = new();
    private readonly AuthRateLimiter _limiter = new();
    private ConfigStore _config = null!;
    private HatoclipServer _server = null!;
    private HttpClient _http = null!;
    private int _reads;
    private int _received;
    private int _pings;

    public async ValueTask InitializeAsync()
    {
        _config = new ConfigStore(_dir.File("config.json"));
        _config.Load();
        _config.Update(c => c.Port = FreeLoopbackPort());
        _server = new HatoclipServer(_config, _clipboard, _limiter, IPAddress.Loopback);
        _server.ClipboardRead += (_, _) => Interlocked.Increment(ref _reads);
        _server.TextReceived += (_, _) => Interlocked.Increment(ref _received);
        _server.Pinged += (_, _) => Interlocked.Increment(ref _pings);
        await _server.StartAsync();
        _http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{_server.Port}") };
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await _server.DisposeAsync();
        _dir.Dispose();
    }

    private static int FreeLoopbackPort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }

    private HttpRequestMessage Request(HttpMethod method, string path, string? key = null, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Add(HatoclipServer.KeyHeader, key ?? _config.Key);
        return request;
    }

    private async Task<(HttpStatusCode Status, JsonElement Json)> SendAsync(HttpRequestMessage request)
    {
        using var response = await _http.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var json = body.Length > 0 ? JsonDocument.Parse(body).RootElement.Clone() : default;
        return (response.StatusCode, json);
    }

    [Fact]
    public async Task Ping_with_the_key_succeeds()
    {
        var (status, json) = await SendAsync(Request(HttpMethod.Get, "/api/ping"));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(json.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task Successful_ping_raises_Pinged()
    {
        var (status, _) = await SendAsync(Request(HttpMethod.Get, "/api/ping"));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(1, _pings);
    }

    [Fact]
    public async Task Rejected_ping_and_other_endpoints_do_not_raise_Pinged()
    {
        var (wrongKey, _) = await SendAsync(Request(HttpMethod.Get, "/api/ping", key: "wrong"));
        using var noKey = await _http.GetAsync("/api/ping", TestContext.Current.CancellationToken);
        var (clipboard, _) = await SendAsync(Request(HttpMethod.Get, "/api/clipboard"));

        Assert.Equal(HttpStatusCode.Unauthorized, wrongKey);
        Assert.Equal(HttpStatusCode.Unauthorized, noKey.StatusCode);
        Assert.Equal(HttpStatusCode.OK, clipboard);
        Assert.Equal(0, _pings);
    }

    [Theory]
    [InlineData("/api/ping")]
    [InlineData("/api/clipboard")]
    [InlineData("/api/does-not-exist")]
    public async Task Wrong_key_gets_401_on_every_api_path(string path)
    {
        var (status, _) = await SendAsync(Request(HttpMethod.Get, path, key: "wrong"));

        Assert.Equal(HttpStatusCode.Unauthorized, status);
    }

    [Fact]
    public async Task Missing_key_gets_401()
    {
        using var response = await _http.GetAsync("/api/ping", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Repeated_wrong_keys_get_429_but_the_right_key_still_works()
    {
        for (var i = 0; i < AuthRateLimiter.MaxFailures; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(Request(HttpMethod.Get, "/api/ping", key: "wrong"))).Status);
        }

        Assert.Equal((HttpStatusCode)429, (await SendAsync(Request(HttpMethod.Get, "/api/ping", key: "wrong"))).Status);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(Request(HttpMethod.Get, "/api/ping"))).Status);
    }

    [Fact]
    public async Task Options_preflight_gets_204_without_cors_and_is_not_an_auth_failure()
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/clipboard");
        using var response = await _http.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.Equal(0, _limiter.EntryCount);
    }

    [Fact]
    public async Task Post_json_sets_the_clipboard_and_raises_TextReceived()
    {
        var content = new StringContent("""{"text":"こんにちは🕊️\n2行目"}""", Encoding.UTF8, "application/json");

        var (status, json) = await SendAsync(Request(HttpMethod.Post, "/api/clipboard", content: content));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("こんにちは🕊️\n2行目", _clipboard.Text);
        Assert.Equal("こんにちは🕊️\n2行目".Length, json.GetProperty("length").GetInt32());
        Assert.Equal(1, _received);
        Assert.Equal(0, _reads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Post_passes_the_excludeFromHistory_setting(bool exclude)
    {
        _config.Update(c => c.ExcludeFromHistory = exclude);

        var (status, _) = await SendAsync(Request(HttpMethod.Post, "/api/clipboard",
            content: new StringContent("text", Encoding.UTF8, "text/plain")));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(exclude, _clipboard.LastExcludeFromHistory);
    }

    [Theory]
    [InlineData("", "text/plain", "empty")]
    [InlineData("""{"text":""}""", "application/json", "empty")]
    [InlineData("""{"txt":"x"}""", "application/json", "bad-request")]
    [InlineData("{not json", "application/json", "bad-request")]
    public async Task Bad_or_empty_post_gets_400_and_leaves_the_clipboard_alone(string body, string contentType, string error)
    {
        _clipboard.Text = "keep me";
        var content = new StringContent(body, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        var (status, json) = await SendAsync(Request(HttpMethod.Post, "/api/clipboard", content: content));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(error, json.GetProperty("error").GetString());
        Assert.Equal("keep me", _clipboard.Text);
        Assert.Equal(0, _received);
    }

    [Fact]
    public async Task Body_over_1MB_gets_413()
    {
        var content = new ByteArrayContent(new byte[HatoclipServer.MaxBodyBytes + 1]);
        content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");

        var (status, _) = await SendAsync(Request(HttpMethod.Post, "/api/clipboard", content: content));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, status);
        Assert.Equal(0, _clipboard.SetCount);
    }

    [Fact]
    public async Task Get_returns_the_clipboard_and_raises_ClipboardRead()
    {
        _clipboard.Text = "PC のテキスト";

        var (status, json) = await SendAsync(Request(HttpMethod.Get, "/api/clipboard"));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("PC のテキスト", json.GetProperty("text").GetString());
        Assert.Equal(1, _reads);
    }

    [Fact]
    public async Task Get_when_disabled_gets_403_and_does_not_raise_ClipboardRead()
    {
        _config.Update(c => c.AllowGet = false);

        var (status, json) = await SendAsync(Request(HttpMethod.Get, "/api/clipboard"));

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Equal("get-disabled", json.GetProperty("error").GetString());
        Assert.Equal(0, _reads);
    }

    [Fact]
    public async Task Busy_clipboard_gets_503_without_events()
    {
        _clipboard.Busy = true;

        var (getStatus, _) = await SendAsync(Request(HttpMethod.Get, "/api/clipboard"));
        var (postStatus, _) = await SendAsync(Request(HttpMethod.Post, "/api/clipboard",
            content: new StringContent("x", Encoding.UTF8, "text/plain")));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, getStatus);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, postStatus);
        Assert.Equal(0, _reads);
        Assert.Equal(0, _received);
    }

    [Theory]
    [InlineData("/", false)]
    [InlineData("/api/ping", true)]
    public async Task Responses_carry_the_security_headers(string path, bool isApi)
    {
        using var response = await _http.SendAsync(Request(HttpMethod.Get, path), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.False(response.Headers.Contains("Server"));
        Assert.Equal(isApi ? "no-store" : "no-cache", response.Headers.CacheControl?.ToString());
    }
}

/// <summary>Port fallback: used for this run only, never written to config.</summary>
public sealed class PortFallbackTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public async Task Busy_configured_port_falls_back_without_saving()
    {
        using var blocker = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
        {
            ExclusiveAddressUse = true,
        };
        blocker.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        blocker.Listen();
        var busyPort = ((IPEndPoint)blocker.LocalEndPoint!).Port;

        var configPath = _dir.File("config.json");
        var config = new ConfigStore(configPath);
        config.Load();
        config.Update(c => c.Port = busyPort);
        var fileBefore = File.ReadAllText(configPath);

        await using (var server = new HatoclipServer(config, new FakeClipboard(), new AuthRateLimiter(), IPAddress.Loopback))
        {
            await server.StartAsync();

            Assert.True(server.Port > busyPort && server.Port <= busyPort + HatoclipServer.PortSearchRange);
            Assert.Equal(busyPort, config.Current.Port);
            Assert.Equal(fileBefore, File.ReadAllText(configPath));
        }

        // Once the port is free again, the next start uses it.
        blocker.Close();
        await using (var server = new HatoclipServer(config, new FakeClipboard(), new AuthRateLimiter(), IPAddress.Loopback))
        {
            await server.StartAsync();
            Assert.Equal(busyPort, server.Port);
        }
    }
}
