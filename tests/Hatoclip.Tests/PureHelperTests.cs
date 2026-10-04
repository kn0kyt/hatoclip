using System.Net;
using System.Net.NetworkInformation;

namespace Hatoclip.Tests;

public sealed class SanitizeLinkUrlTests
{
    [Theory]
    [InlineData("https://www.icloud.com/shortcuts/abc123", "https://www.icloud.com/shortcuts/abc123")]
    [InlineData("  https://www.icloud.com/shortcuts/abc123  ", "https://www.icloud.com/shortcuts/abc123")]
    [InlineData("http://example.com", "http://example.com/")]
    [InlineData("HTTPS://EXAMPLE.COM/a b", "https://example.com/a%20b")]
    public void Absolute_http_and_https_urls_pass(string input, string expected)
    {
        Assert.Equal(expected, HatoclipServer.SanitizeLinkUrl(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<b>x</b>")]
    [InlineData("ftp://example.com/file")]
    [InlineData("shortcuts://run-shortcut?name=x")]
    [InlineData("/relative/path")]
    [InlineData("www.icloud.com/shortcuts/abc")]
    public void Anything_else_becomes_empty(string? input)
    {
        Assert.Equal("", HatoclipServer.SanitizeLinkUrl(input));
    }
}

public sealed class NetworkInfoTests
{
    [Theory]
    [InlineData("vEthernet (WSL)", "Hyper-V Virtual Ethernet Adapter")]
    [InlineData("Tailscale", "Tailscale Tunnel")]
    [InlineData("イーサネット 3", "VirtualBox Host-Only Ethernet Adapter")]
    [InlineData("ローカル エリア接続* 2", "Microsoft Wi-Fi Direct Virtual Adapter #2")]
    [InlineData("wg0", "wireguard tunnel")] // case-insensitive
    [InlineData("Bluetooth ネットワーク接続", "Bluetooth Device (Personal Area Network)")]
    public void Virtual_and_vpn_adapters_are_excluded(string name, string description)
    {
        Assert.True(NetworkInfo.IsVirtualAdapter(name, description));
    }

    [Theory]
    [InlineData("Wi-Fi", "Intel(R) Wi-Fi 6E AX211 160MHz")]
    [InlineData("イーサネット", "Realtek PCIe GbE Family Controller")]
    [InlineData("Ethernet", "Intel(R) Ethernet Connection (17) I219-V")]
    public void Physical_adapters_are_kept(string name, string description)
    {
        Assert.False(NetworkInfo.IsVirtualAdapter(name, description));
    }

    private static LanCandidate Candidate(string ip, bool gateway, NetworkInterfaceType type, bool bestRoute) =>
        new(IPAddress.Parse(ip), "{" + Guid.NewGuid() + "}", ip, ip, type, gateway, NetworkInfo.IsPhysicalType(type), bestRoute);

    [Fact]
    public void Candidates_are_ordered_by_gateway_then_physical_type_then_best_route()
    {
        var noGatewayWifi = Candidate("10.0.0.2", gateway: false, NetworkInterfaceType.Wireless80211, bestRoute: true);
        var gatewayOther = Candidate("10.0.0.3", gateway: true, NetworkInterfaceType.Unknown, bestRoute: true);
        var gatewayEthernet = Candidate("10.0.0.4", gateway: true, NetworkInterfaceType.Ethernet, bestRoute: false);
        var gatewayWifiBest = Candidate("10.0.0.5", gateway: true, NetworkInterfaceType.Wireless80211, bestRoute: true);

        var ordered = NetworkInfo.OrderCandidates([noGatewayWifi, gatewayOther, gatewayEthernet, gatewayWifiBest]);

        Assert.Equal([gatewayWifiBest, gatewayEthernet, gatewayOther, noGatewayWifi], ordered);
    }

    [Fact]
    public void Connect_url_carries_the_key_in_the_fragment()
    {
        Assert.Equal("http://192.168.1.10:52818/#k=abc", NetworkInfo.BuildConnectUrl("192.168.1.10", 52818, "abc"));
    }
}

public sealed class ReceivedNotificationTests
{
    [Fact]
    public void Preview_off_shows_only_the_fixed_message()
    {
        Assert.Equal("スマホから鳩が届きました", TrayApplicationContext.BuildReceivedMessage("secret text", showPreview: false));
    }

    [Fact]
    public void Preview_on_appends_the_collapsed_text()
    {
        Assert.Equal("スマホから鳩が届きました\nline1 line2",
            TrayApplicationContext.BuildReceivedMessage("line1\r\nline2\n", showPreview: true));
    }

    [Fact]
    public void Preview_is_cut_at_40_text_elements_without_splitting_emoji()
    {
        var text = string.Concat(Enumerable.Repeat("🕊️", 50));

        var preview = TrayApplicationContext.MakePreview(text);

        Assert.Equal(string.Concat(Enumerable.Repeat("🕊️", 40)) + "…", preview);
    }

    [Fact]
    public void Whitespace_only_text_has_a_placeholder_preview()
    {
        Assert.Equal("（空のテキスト）", TrayApplicationContext.MakePreview("\r\n\t"));
    }
}

public sealed class ClipboardDataObjectTests
{
    [Fact]
    public void Plain_text_has_no_exclusion_formats()
    {
        var data = ClipboardService.CreateTextDataObject("こんにちは 🕊️", excludeFromHistory: false);

        Assert.True(data.TryGetData<string>(DataFormats.UnicodeText, out var text));
        Assert.Equal("こんにちは 🕊️", text);
        Assert.DoesNotContain(ClipboardService.CanIncludeInHistoryFormat, data.GetFormats(false));
        Assert.DoesNotContain(ClipboardService.CanUploadToCloudFormat, data.GetFormats(false));
        Assert.DoesNotContain(ClipboardService.ExcludeFromMonitorFormat, data.GetFormats(false));
    }

    [Fact]
    public void Excluded_text_carries_the_three_formats_as_dword_zero()
    {
        var data = ClipboardService.CreateTextDataObject("secret", excludeFromHistory: true);

        Assert.True(data.TryGetData<string>(DataFormats.UnicodeText, out var text));
        Assert.Equal("secret", text);
        foreach (var format in new[]
                 {
                     ClipboardService.ExcludeFromMonitorFormat,
                     ClipboardService.CanIncludeInHistoryFormat,
                     ClipboardService.CanUploadToCloudFormat,
                 })
        {
            Assert.True(data.TryGetData<MemoryStream>(format, autoConvert: false, out var stream));
            Assert.Equal(new byte[] { 0, 0, 0, 0 }, stream.ToArray());
        }
    }
}

public sealed class TrayIconAssetTests
{
    [Theory]
    [InlineData("hatoclip.ico")]
    [InlineData("hatoclip-received.ico")]
    [InlineData("hatoclip-sent.ico")]
    public void Tray_icons_are_embedded_with_all_four_sizes(string fileName)
    {
        var bytes = EmbeddedAssets.GetAsset(fileName);

        Assert.NotNull(bytes);
        Assert.Equal(4, BitConverter.ToUInt16(bytes, 4)); // ICONDIR.idCount: 16, 24, 32, 48
        foreach (var size in new[] { 16, 24, 32, 48 })
        {
            using var icon = new Icon(new MemoryStream(bytes), size, size);
            Assert.Equal(size, icon.Width);
        }
    }

    [Fact]
    public void Sent_icon_differs_from_the_other_two()
    {
        var sent = EmbeddedAssets.GetAsset("hatoclip-sent.ico");

        Assert.NotEqual(EmbeddedAssets.GetAsset("hatoclip.ico"), sent);
        Assert.NotEqual(EmbeddedAssets.GetAsset("hatoclip-received.ico"), sent);
    }
}
