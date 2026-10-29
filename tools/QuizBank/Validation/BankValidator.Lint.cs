using System.Text.RegularExpressions;
using QuizBank.Model;
using QuizBank.Yaml;

namespace QuizBank.Validation;

// Gate 1 lint: the writing rules from the PLAN.md section "Question writing rules" that a tool can decide,
// the name denylist, and checks that copy the question's code unchanged.
public sealed partial class BankValidator
{
    /// <summary>Where a check keeps its copy of the question's code: between Start and End, or from Start to the end of the file.</summary>
    private sealed record SnippetBlock(string Start, string? End);

    // C# statements, then the types that top-level programs must declare after them (docs/CODE_STYLE.md).
    private static readonly SnippetBlock[] CSharpSnippet =
        [new("// --- snippet statements ---", "// --- end snippet statements ---"), new("// --- snippet types ---", null)];

    private static readonly SnippetBlock[] SqlSnippet = [new("-- --- snippet ---", "-- --- end snippet ---")];

    // Within a question the longest option is at most 1.3 times the shortest, compared in tenths so it stays integer.
    private const int MaxOptionLengthRatioInTenths = 13;

    // The correct option is the longest in at most 35% of questions. Below 20 questions the share is mostly noise.
    private const int MaxLongestCorrectPercent = 35;
    private const int MinQuestionsForLongestCorrect = 20;

    private static readonly string[] ApiReferenceViews = ["net-10.0", "net-10.0-pp", "aspnetcore-10.0", "efcore-10.0"];

    private void LintQuestion(BankFile<Question> item)
    {
        CheckOptionLengths(item);
        CheckPinnedDocs(item);
        CheckSnippet(item);
    }

    private void CheckOptionLengths(BankFile<Question> item)
    {
        var question = item.Value;
        if (question.Type == QuestionType.Ordering) return;

        var lengths = question.Options.Select(o => o.Text.Trim().Length).ToList();
        var longest = lengths.IndexOf(lengths.Max());
        var shortest = lengths.IndexOf(lengths.Min());
        if (lengths[longest] * 10 > lengths[shortest] * MaxOptionLengthRatioInTenths)
            Error(item, $"/options/{longest}/text",
                $"Option '{question.Options[longest].Id}' has {lengths[longest]} characters, more than 1.3 times the " +
                $"{lengths[shortest]} of option '{question.Options[shortest].Id}'; even out the option lengths so length gives nothing away.");
    }

    private void CheckPinnedDocs(BankFile<Question> item)
    {
        for (var i = 0; i < item.Value.Sources.Count; i++)
        {
            if (UnpinnedDocs(item.Value.Sources[i].Url) is { } problem)
                Error(item, $"/sources/{i}/url", problem);
        }
    }

    /// <summary>Why a docs link isn't pinned to the target versions (.NET 10, ASP.NET Core 10, PostgreSQL 16), or null.</summary>
    private static string? UnpinnedDocs(string url)
    {
        // The schema already reports URLs that don't parse.
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;

        var view = ViewOf(uri);
        return uri.Host.ToLowerInvariant() switch
        {
            "learn.microsoft.com" when LearnAspNetCore().IsMatch(uri.AbsolutePath) && view != "aspnetcore-10.0" =>
                "Pin ASP.NET Core docs to version 10: add ?view=aspnetcore-10.0 to the URL.",
            "learn.microsoft.com" when LearnApiReference().IsMatch(uri.AbsolutePath) && !ApiReferenceViews.Contains(view) =>
                $"Pin API reference links to a .NET 10 view: add ?view= with one of {string.Join(", ", ApiReferenceViews)}.",
            "www.postgresql.org" or "postgresql.org" when PostgresDocs().Match(uri.AbsolutePath) is { Success: true } docs
                                                          && docs.Groups[1].Value != "16" =>
                $"Link the PostgreSQL 16 docs: use /docs/16/ instead of /docs/{docs.Groups[1].Value}/.",
            _ => null,
        };
    }

    /// <summary>The ?view= of a Microsoft Learn URL, such as net-10.0, or null.</summary>
    internal static string? ViewOf(Uri uri) =>
        ViewParameter().Match(uri.Query) is { Success: true } m ? Uri.UnescapeDataString(m.Groups[1].Value) : null;

    /// <summary>
    /// A check copies its question's code unchanged between snippet markers (docs/CODE_STYLE.md), so it runs what the
    /// question shows. Only C# and SQL code is compared, with trailing spaces and blank lines at either end ignored.
    /// </summary>
    private void CheckSnippet(BankFile<Question> item)
    {
        if (item.Value is not { Code: { } code, Verification.Check: { } check }) return;

        var markers = (code.Lang, Path.GetExtension(check)) switch
        {
            ("csharp", ".cs") => CSharpSnippet,
            ("sql", ".sql") => SqlSnippet,
            _ => null,
        };
        var path = Path.Combine(_root, check);
        if (markers is null || !File.Exists(path)) return;

        var lines = File.ReadAllLines(path);
        var snippet = new List<(string Text, int Line)>();
        var found = false;
        var endLine = lines.Length;
        foreach (var block in markers)
        {
            var start = Array.FindIndex(lines, line => line.Trim() == block.Start);
            if (start < 0) continue;

            var end = block.End is null ? lines.Length : Array.FindIndex(lines, start + 1, line => line.Trim() == block.End);
            if (end < 0)
            {
                _errors.Add(new Diagnostic(check, start + 1, 1, $"'{block.Start}' has no '{block.End}' line after it."));
                return;
            }

            var copied = TrimBlankEnds(lines[(start + 1)..end].Select((text, i) => (Text: text.TrimEnd(), Line: start + 2 + i)), line => line.Text)
                .ToList();
            // One blank line separates the statements from the types, as in the question.
            if (snippet.Count > 0 && copied.Count > 0) snippet.Add(("", start + 1));
            snippet.AddRange(copied);
            found = true;
            endLine = block.End is null ? lines.Length : end + 1;
        }

        if (!found)
        {
            var where = markers == CSharpSnippet
                ? $"between '{CSharpSnippet[0].Start}' and '{CSharpSnippet[0].End}', with its types after '{CSharpSnippet[1].Start}'"
                : $"between '{SqlSnippet[0].Start}' and '{SqlSnippet[0].End}'";
            Error(check, $"No snippet markers; copy the question's code {where}.");
            return;
        }

        var expected = TrimBlankEnds(code.Text.Split('\n').Select(line => line.TrimEnd()), line => line).ToList();
        for (var i = 0; i < Math.Max(expected.Count, snippet.Count); i++)
        {
            if (i < expected.Count && i < snippet.Count && expected[i] == snippet[i].Text) continue;

            // Both lists end with a non-blank line, so the rest of the longer one has one to quote.
            var missing = expected.Skip(i).FirstOrDefault(text => text.Length > 0);
            var extra = snippet.Skip(i).FirstOrDefault(copied => copied.Text.Length > 0);
            var (line, message) = (i < expected.Count, i < snippet.Count) switch
            {
                (true, true) => (snippet[i].Line, $"the question has '{expected[i]}' here, but the check has '{snippet[i].Text}'"),
                (true, false) => (endLine, $"the snippet ends before the question's '{missing}'"),
                _ => (extra.Line, $"the check has '{extra.Text}' after the question's code ends"),
            };
            _errors.Add(new Diagnostic(check, line, 1, $"The snippet differs from the question's code: {message}; copy the code unchanged."));
            return;
        }
    }

    private static IEnumerable<T> TrimBlankEnds<T>(IEnumerable<T> lines, Func<T, string> text) =>
        lines.SkipWhile(line => text(line).Length == 0).Reverse().SkipWhile(line => text(line).Length == 0).Reverse();

    /// <summary>Across the bank, the correct option may be the longest one in at most 35% of single-choice questions.</summary>
    private void CheckLongestCorrectShare(IEnumerable<BankFile<Question>> questions)
    {
        var eligible = questions
            .Select(q => q.Value)
            .Where(q => q is { Type: QuestionType.SingleChoice } && q.Status != QuestionStatus.Retired)
            .ToList();
        if (eligible.Count < MinQuestionsForLongestCorrect) return;

        var longestCorrect = eligible.Where(IsCorrectTheLongest).Select(q => q.Id).Order(StringComparer.Ordinal).ToList();
        if (longestCorrect.Count * 100 <= eligible.Count * MaxLongestCorrectPercent) return;

        Error("questions",
            $"The correct option is the longest in {longestCorrect.Count} of {eligible.Count} single-choice questions " +
            $"({longestCorrect.Count * 100 / eligible.Count}%), more than the {MaxLongestCorrectPercent}% allowed; shorten the " +
            $"correct option or lengthen a wrong one in some of: {string.Join(", ", longestCorrect)}.");
    }

    // Strictly the longest: a tie with a wrong option doesn't point at the answer.
    private static bool IsCorrectTheLongest(Question question)
    {
        var correct = question.Options.FirstOrDefault(o => o.Id == question.Answer[0]);
        if (correct is null) return false;

        var length = correct.Text.Trim().Length;
        return question.Options.All(o => o == correct || o.Text.Trim().Length < length);
    }

    /// <summary>Denylisted names anywhere in the bank: roadmaps, concepts, questions and checks.</summary>
    private void CheckDenylist(Denylist denylist)
    {
        string[] folders = ["roadmaps", "concepts", "questions", "checks"];
        var files = folders
            .Select(folder => Path.Combine(_root, folder))
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            .Order(StringComparer.Ordinal);

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var match in denylist.Matches(text))
            {
                var position = SourcePosition.FromOffset(text, match.Offset);
                // The message names the denylist line, never the name: output may be pasted into public places.
                _errors.Add(new Diagnostic(Relative(file), position.Line, position.Column,
                    $"Matches the name on line {match.EntryLine} of the denylist; use a neutral name such as Shop, Orders or Billing."));
            }
        }
    }

    [GeneratedRegex(@"(?:^|&|\?)view=([^&]*)")]
    private static partial Regex ViewParameter();

    // With or without a locale segment: /aspnet/core/... or /en-us/aspnet/core/...
    [GeneratedRegex(@"^/(?:[a-z]{2}-[a-z]{2}/)?aspnet/core(?:/|$)", RegexOptions.IgnoreCase)]
    private static partial Regex LearnAspNetCore();

    [GeneratedRegex(@"^/(?:[a-z]{2}-[a-z]{2}/)?dotnet/api(?:/|$)", RegexOptions.IgnoreCase)]
    private static partial Regex LearnApiReference();

    // Only versions: /docs/release/16.4/ (release notes) and /docs/manuals/ aren't versioned docs.
    [GeneratedRegex(@"^/docs/(\d+(?:\.\d+)?|current|devel)/")]
    private static partial Regex PostgresDocs();
}
