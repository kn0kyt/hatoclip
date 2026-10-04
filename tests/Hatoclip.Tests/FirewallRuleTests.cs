namespace Hatoclip.Tests;

public sealed class FirewallRuleTests
{
    private const string Exe = @"C:\Users\me\Hatoclip\Hatoclip.exe";
    private const int Port = 52817;

    private static string AppRule(string action = "Allow", string active = "TRUE", string dir = "In",
        string? protocol = "6", string? profile = "Private", string app = Exe) =>
        "v2.30|Action=" + action + "|Active=" + active + "|Dir=" + dir +
        (protocol is null ? "" : "|Protocol=" + protocol) +
        (profile is null ? "" : "|Profile=" + profile) +
        "|App=" + app + "|Name=hatoclip.exe|Desc=hatoclip.exe|";

    private static FirewallStatus Evaluate(NetworkCategory category, params string[] rules) =>
        NetworkDiagnostics.EvaluateRules(rules, Exe, Port, category);

    [Fact]
    public void Active_inbound_allow_rule_for_the_exe_allows()
    {
        Assert.Equal(FirewallStatus.Allowed, Evaluate(NetworkCategory.Private, AppRule()));
    }

    [Fact]
    public void No_rules_means_not_allowed()
    {
        Assert.Equal(FirewallStatus.NotAllowed, Evaluate(NetworkCategory.Private));
    }

    [Fact]
    public void Inactive_rule_is_ignored()
    {
        Assert.Equal(FirewallStatus.NotAllowed, Evaluate(NetworkCategory.Private, AppRule(active: "FALSE")));
    }

    [Fact]
    public void Outbound_rule_is_ignored()
    {
        Assert.Equal(FirewallStatus.NotAllowed, Evaluate(NetworkCategory.Private, AppRule(dir: "Out")));
    }

    [Theory]
    [InlineData("Public", NetworkCategory.Private, FirewallStatus.NotAllowed)]
    [InlineData("Public", NetworkCategory.Public, FirewallStatus.Allowed)]
    [InlineData("Domain", NetworkCategory.Private, FirewallStatus.NotAllowed)]
    [InlineData(null, NetworkCategory.Public, FirewallStatus.Allowed)] // no Profile field = all profiles
    [InlineData("Public", NetworkCategory.Unknown, FirewallStatus.Allowed)] // unknown profile matches any rule
    public void Profile_must_match_the_current_network(string? ruleProfile, NetworkCategory category, FirewallStatus expected)
    {
        Assert.Equal(expected, Evaluate(category, AppRule(profile: ruleProfile)));
    }

    [Fact]
    public void Rule_with_several_profiles_matches_each_of_them()
    {
        var rule = AppRule(profile: "Private") + "Profile=Public|";

        Assert.Equal(FirewallStatus.Allowed, Evaluate(NetworkCategory.Public, rule));
        Assert.Equal(FirewallStatus.Allowed, Evaluate(NetworkCategory.Private, rule));
        Assert.Equal(FirewallStatus.NotAllowed, Evaluate(NetworkCategory.Domain, rule));
    }

    [Theory]
    [InlineData("6", FirewallStatus.Allowed)]
    [InlineData(null, FirewallStatus.Allowed)] // any protocol
    [InlineData("17", FirewallStatus.NotAllowed)] // UDP only
    public void Rule_must_cover_tcp(string? protocol, FirewallStatus expected)
    {
        Assert.Equal(expected, Evaluate(NetworkCategory.Private, AppRule(protocol: protocol)));
    }

    [Fact]
    public void Exe_path_comparison_is_case_insensitive()
    {
        Assert.Equal(FirewallStatus.Allowed, Evaluate(NetworkCategory.Private, AppRule(app: Exe.ToUpperInvariant())));
    }

    [Fact]
    public void Rule_for_another_exe_does_not_allow()
    {
        Assert.Equal(FirewallStatus.NotAllowed,
            Evaluate(NetworkCategory.Private, AppRule(app: @"C:\Users\me\Downloads\Hatoclip.exe")));
    }

    [Fact]
    public void Block_rule_wins_over_allow_rule()
    {
        Assert.Equal(FirewallStatus.Blocked,
            Evaluate(NetworkCategory.Private, AppRule(), AppRule(action: "Block")));
    }

    [Fact]
    public void Block_rule_for_another_exe_is_ignored()
    {
        Assert.Equal(FirewallStatus.Allowed,
            Evaluate(NetworkCategory.Private, AppRule(), AppRule(action: "Block", app: @"C:\Other\Other.exe")));
    }

    [Theory]
    [InlineData("52817", FirewallStatus.Allowed)]
    [InlineData("80,52817", FirewallStatus.Allowed)]
    [InlineData("52800-52900", FirewallStatus.Allowed)]
    [InlineData("80,443", FirewallStatus.NotAllowed)]
    [InlineData("52818-52900", FirewallStatus.NotAllowed)]
    public void Port_rule_without_app_allows_our_port(string lport, FirewallStatus expected)
    {
        var rule = $"v2.30|Action=Allow|Active=TRUE|Dir=In|Protocol=6|Profile=Private|LPort={lport}|Name=port rule|";

        Assert.Equal(expected, Evaluate(NetworkCategory.Private, rule));
    }

    [Fact]
    public void Port_rule_bound_to_another_app_does_not_allow()
    {
        var rule = $"v2.30|Action=Allow|Active=TRUE|Dir=In|Protocol=6|LPort={Port}|App=C:\\Other\\Other.exe|";

        Assert.Equal(FirewallStatus.NotAllowed, Evaluate(NetworkCategory.Private, rule));
    }

    [Fact]
    public void Malformed_rule_strings_are_ignored()
    {
        Assert.Equal(FirewallStatus.Allowed,
            Evaluate(NetworkCategory.Private, "", "garbage", "|||=|", AppRule()));
    }
}
