using System.Text.RegularExpressions;
using QuizBank.Model;

namespace QuizBank.Validation;

// Gate 1 lint: the writing rules from the PLAN.md section "Question writing rules" that a tool can decide,
// plus the name denylist.
public sealed partial class BankValidator
{
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

        var view = ViewParameter().Match(uri.Query) is { Success: true } m ? Uri.UnescapeDataString(m.Groups[1].Value) : null;
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
                var line = 1 + text.AsSpan(0, match.Offset).Count('\n');
                var column = match.Offset - (text.LastIndexOf('\n', Math.Max(match.Offset - 1, 0)) + 1) + 1;
                // The message names the denylist line, never the name: output may be pasted into public places.
                _errors.Add(new Diagnostic(Relative(file), line, column,
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

    [GeneratedRegex(@"^/docs/([^/]+)/")]
    private static partial Regex PostgresDocs();
}
