namespace Hatoclip.Tests;

public sealed class ThemeTests
{
    [Theory]
    [InlineData("dark", true)]
    [InlineData(" Dark ", true)]
    [InlineData("LIGHT", false)]
    [InlineData("light", false)]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("auto", null)]
    public void Theme_override_is_parsed(string? value, bool? expected)
    {
        Assert.Equal(expected, Theme.ParseOverride(value));
    }

    [Fact]
    public void Apps_use_light_theme_zero_means_dark()
    {
        Assert.True(NativeTheme.IsDarkFromRegistryValue(0));
        Assert.False(NativeTheme.IsDarkFromRegistryValue(1));
        Assert.False(NativeTheme.IsDarkFromRegistryValue(null)); // value missing: light (the Windows default)
        Assert.False(NativeTheme.IsDarkFromRegistryValue("0")); // wrong type: ignored
    }

    [Theory]
    [InlineData(12, 1f, 12)]
    [InlineData(12, 1.25f, 15)]
    [InlineData(12, 1.5f, 18)]
    [InlineData(3, 1.25f, 4)] // 3.75 rounds up
    [InlineData(1, 0.4f, 1)] // never 0 for a positive length
    [InlineData(0, 2f, 0)]
    public void Logical_pixels_scale_and_round(int logical, float factor, int expected)
    {
        Assert.Equal(expected, Theme.Scale(logical, factor));
    }

    [Fact]
    public void Light_palette_matches_the_phone_page_tokens()
    {
        var p = Theme.Light;
        Assert.Equal(Color.FromArgb(0xf4, 0xf6, 0xfa).ToArgb(), p.Background.ToArgb());
        Assert.Equal(Color.FromArgb(0x4a, 0x62, 0x8c).ToArgb(), p.Accent.ToArgb());
        Assert.Equal(Color.FromArgb(0x2f, 0x6f, 0xd6).ToArgb(), p.Focus.ToArgb());
        Assert.Equal(Color.FromArgb(0x12, 0x16, 0x1d).ToArgb(), Theme.Dark.Background.ToArgb());
    }
}

public sealed class UrlFieldEllipsizeTests
{
    // One "pixel" per character keeps the expectations readable.
    private static int Measure(string s) => s.Length;

    [Fact]
    public void Text_that_fits_is_unchanged()
    {
        Assert.Equal("http://a:1/#k=x", UrlField.Ellipsize("http://a:1/#k=x", 15, Measure));
        Assert.Equal("", UrlField.Ellipsize("", 0, Measure));
    }

    [Fact]
    public void Long_text_keeps_the_longest_prefix_that_fits_with_an_ellipsis()
    {
        Assert.Equal("http://…", UrlField.Ellipsize("http://example.local:52817/", 8, Measure));
        Assert.Equal("…", UrlField.Ellipsize("abc", 1, Measure));
        Assert.Equal("…", UrlField.Ellipsize("abc", 0, Measure)); // nothing fits: just the ellipsis
    }

    [Fact]
    public void A_surrogate_pair_is_never_split()
    {
        var text = "ab\U0001F54Acd"; // a, b, dove (2 UTF-16 units), c, d
        Assert.Equal("ab…", UrlField.Ellipsize(text, 4, Measure));
    }
}

public sealed class RoundedButtonBlendTests
{
    [Fact]
    public void Blend_interpolates_and_clamps()
    {
        Assert.Equal(Color.FromArgb(0, 0, 0).ToArgb(), RoundedButton.Blend(Color.Black, Color.White, 0f).ToArgb());
        Assert.Equal(Color.FromArgb(255, 255, 255).ToArgb(), RoundedButton.Blend(Color.Black, Color.White, 2f).ToArgb());
        Assert.Equal(Color.FromArgb(128, 128, 128).ToArgb(), RoundedButton.Blend(Color.Black, Color.White, 0.5f).ToArgb());
    }
}
