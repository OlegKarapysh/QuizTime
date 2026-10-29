using System.Text.RegularExpressions;

namespace QuizBank.Validation;

/// <summary>A name from the denylist found in a file: its offset in the text and the denylist line that matched.</summary>
public readonly record struct DenylistMatch(int Offset, int EntryLine);

/// <summary>
/// Names that must never appear in the public bank (private/denylist.txt): one per line, matched in any case as
/// whole words or as parts of camelCase identifiers. Blank lines and lines starting with # are ignored.
/// </summary>
public sealed class Denylist
{
    // A side of the name is a hump when a lowercase letter meets an uppercase one there: AcmeOrders, myAcme.
    // Letters and digits otherwise mean the name is part of a longer word: Acmes, Novak.
    private const string Hump = @"(?<=(?-i:\p{Ll}))(?=(?-i:\p{Lu}))";
    private const string Start = @"(?:(?<![\p{L}\p{N}])|" + Hump + ")";
    private const string End = @"(?:(?![\p{L}\p{N}])|" + Hump + ")";

    private readonly IReadOnlyList<(int Line, Regex Pattern)> _entries;

    private Denylist(IReadOnlyList<(int Line, Regex Pattern)> entries) => _entries = entries;

    public static Denylist? Load(string path) => File.Exists(path) ? Parse(File.ReadAllLines(path)) : null;

    public static Denylist Parse(IReadOnlyList<string> lines)
    {
        var entries = new List<(int, Regex)>();
        for (var i = 0; i < lines.Count; i++)
        {
            var name = lines[i].Trim();
            if (name.Length == 0 || name.StartsWith('#')) continue;

            entries.Add((i + 1, new Regex(Start + Regex.Escape(name) + End, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)));
        }

        return new Denylist(entries);
    }

    public IEnumerable<DenylistMatch> Matches(string text) =>
        _entries
            .SelectMany(entry => entry.Pattern.Matches(text).Select(m => new DenylistMatch(m.Index, entry.Line)))
            .OrderBy(m => m.Offset);
}
