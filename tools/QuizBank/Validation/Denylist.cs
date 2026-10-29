using System.Text.RegularExpressions;

namespace QuizBank.Validation;

/// <summary>A name from the denylist found in a file: its offset in the text and the denylist line that matched.</summary>
public readonly record struct DenylistMatch(int Offset, int EntryLine);

/// <summary>
/// Names that must never appear in the public bank (private/denylist.txt): one per line, matched as whole
/// words in any case. Blank lines and lines starting with # are ignored.
/// </summary>
public sealed class Denylist
{
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

            // Letters and digits on either side mean the name is part of a longer word.
            var pattern = $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(name)}(?![\p{{L}}\p{{N}}])";
            entries.Add((i + 1, new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)));
        }

        return new Denylist(entries);
    }

    public IEnumerable<DenylistMatch> Matches(string text) =>
        _entries
            .SelectMany(entry => entry.Pattern.Matches(text).Select(m => new DenylistMatch(m.Index, entry.Line)))
            .OrderBy(m => m.Offset);
}
