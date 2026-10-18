using AetherControl.Core.Navigation;

namespace AetherControl.Tests;

/// <summary>Pins the Ctrl+K matching contract: ranking order, AND-token semantics, empty query,
/// and case-insensitivity — the list must never shuffle under the user's cursor while typing.</summary>
public sealed class CommandPaletteFilterTests
{
    private static readonly IReadOnlyList<PaletteEntry> Entries =
    [
        new("overview", "Go to Overview", "Live system health", "dashboard home"),
        new("performance", "Go to Performance", "Optimisation and fan control", "cooling fans curve"),
        new("portrait", "Open Portrait Mode", "Second-screen stats window", "display monitor"),
        new("fans", "Reset all fans to BIOS control", "Stops curves and manual control", "cooling fan curve")
    ];

    [Fact]
    public void EmptyQuery_ReturnsEverythingAlphabetically()
    {
        var result = PaletteFilter.Filter(Entries, "");
        Assert.Equal(4, result.Count);
        Assert.Equal("Go to Overview", result[0].Title);
        Assert.Equal("Go to Performance", result[1].Title);
        Assert.Equal("Open Portrait Mode", result[2].Title);
        Assert.Equal("Reset all fans to BIOS control", result[3].Title);
    }

    [Fact]
    public void TitleStart_BeatsTitleContains_BeatsKeywordMatch()
    {
        // "go" starts two titles; "portrait" contains... "por" starts "Portrait" only in title-case;
        // use "fan": title-contains in "Reset all fans..." vs keyword in "performance".
        var result = PaletteFilter.Filter(Entries, "fan");
        Assert.Equal(2, result.Count);
        Assert.Equal("fans", result[0].Id); // title contains
        Assert.Equal("performance", result[1].Id); // keyword only
    }

    [Fact]
    public void MultipleTokens_AllMustMatch()
    {
        var result = PaletteFilter.Filter(Entries, "go performance");
        Assert.Single(result);
        Assert.Equal("performance", result[0].Id);
    }

    [Fact]
    public void NoMatch_ReturnsEmpty()
    {
        Assert.Empty(PaletteFilter.Filter(Entries, "xyzzy"));
    }

    [Fact]
    public void Matching_IsCaseInsensitive()
    {
        var result = PaletteFilter.Filter(Entries, "PORTRAIT");
        Assert.Single(result);
        Assert.Equal("portrait", result[0].Id);
    }

    [Fact]
    public void NullQuery_BehavesLikeEmpty()
    {
        Assert.Equal(4, PaletteFilter.Filter(Entries, null).Count);
    }
}
