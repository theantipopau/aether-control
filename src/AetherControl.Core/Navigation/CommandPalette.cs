namespace AetherControl.Core.Navigation;

/// <summary>One searchable row in the Ctrl+K palette: a navigation target or an action.
/// Pure data — what running it *does* lives App-side (page navigation, window opening),
/// so matching and ranking stay unit-testable without a window.</summary>
public sealed record PaletteEntry(string Id, string Title, string Subtitle, string Keywords);

/// <summary>
/// Case-insensitive AND-token matcher with stable ranking: title-starts-with beats
/// title-contains beats subtitle/keyword match, ties broken alphabetically so the list
/// never shuffles under the user's cursor while they type.
/// </summary>
public static class PaletteFilter
{
    public static IReadOnlyList<PaletteEntry> Filter(IEnumerable<PaletteEntry> entries, string? query)
    {
        var tokens = (query ?? string.Empty)
            .Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var ranked = new List<(PaletteEntry Entry, int Rank)>();
        foreach (var entry in entries)
        {
            if (Rank(entry, tokens) is { } rank)
            {
                ranked.Add((entry, rank));
            }
        }

        return ranked
            .OrderBy(r => r.Rank)
            .ThenBy(r => r.Entry.Title, StringComparer.OrdinalIgnoreCase)
            .Select(r => r.Entry)
            .ToList();
    }

    private static int? Rank(PaletteEntry entry, string[] tokens)
    {
        if (tokens.Length == 0)
        {
            return 3; // empty query lists everything, alphabetically (rank is uniform)
        }

        var worst = 0;
        foreach (var token in tokens)
        {
            var rank = RankToken(entry, token);
            if (rank is null)
            {
                return null; // AND semantics: every token must match somewhere
            }

            worst = Math.Max(worst, rank.Value);
        }

        return worst;
    }

    private static int? RankToken(PaletteEntry entry, string token)
    {
        if (entry.Title.StartsWith(token, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (entry.Title.Contains(token, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return entry.Subtitle.Contains(token, StringComparison.OrdinalIgnoreCase)
            || entry.Keywords.Contains(token, StringComparison.OrdinalIgnoreCase)
            ? 2
            : null;
    }
}
