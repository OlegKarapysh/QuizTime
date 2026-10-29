using QuizBank.Model;
using QuizBank.Validation;

namespace QuizBank;

/// <summary>Which questions run-checks and check-links cover: all of them, the ids asked for, or the changed ones.</summary>
public static class QuestionSelection
{
    private static readonly TimeSpan GitTimeout = TimeSpan.FromMinutes(1);

    /// <summary>The questions with these ids. Throws <see cref="ArgumentException"/> naming the first unknown id.</summary>
    public static IReadOnlyList<BankFile<Question>> ByIds(IReadOnlyList<BankFile<Question>> questions, IReadOnlyList<string> ids)
    {
        var byId = questions.ToDictionary(q => q.Value.Id);
        if (ids.FirstOrDefault(id => !byId.ContainsKey(id)) is { } unknown)
            throw new ArgumentException($"No question has id '{unknown}'.");
        return ids.Distinct().Select(id => byId[id]).ToList();
    }

    /// <summary>
    /// The questions whose question file or checks/&lt;id&gt;/ folder changed. A change to the tools (except their
    /// tests) or to global.json can change any result, so it selects every question.
    /// </summary>
    public static IReadOnlyList<BankFile<Question>> ByChangedFiles(IReadOnlyList<BankFile<Question>> questions, IEnumerable<string> changedFiles)
    {
        var files = changedFiles.ToList();
        if (files.Any(AffectsEveryQuestion)) return questions;

        var ids = files.Select(IdOf).OfType<string>().ToHashSet();
        return questions.Where(q => ids.Contains(q.Value.Id)).ToList();
    }

    /// <summary>
    /// Files that differ from the merge base with <paramref name="gitRef"/>: committed, staged, unstaged and
    /// untracked, as paths relative to the root.
    /// </summary>
    public static async Task<IReadOnlyList<string>> ChangedFilesAsync(string root, string gitRef, CancellationToken cancellationToken)
    {
        var changed = await GitAsync(root, cancellationToken, "diff", "--name-only", "--relative", "--merge-base", gitRef);
        var untracked = await GitAsync(root, cancellationToken, "ls-files", "--others", "--exclude-standard");
        return changed.Concat(untracked).Distinct().Order(StringComparer.Ordinal).ToList();
    }

    private static bool AffectsEveryQuestion(string file) =>
        file == "global.json" || (file.StartsWith("tools/", StringComparison.Ordinal) && !file.StartsWith("tools/QuizBank.Tests/", StringComparison.Ordinal));

    private static string? IdOf(string file) => file.Split('/') switch
    {
        ["questions", .., var name] when name.EndsWith(".yaml", StringComparison.Ordinal) => name[..^".yaml".Length],
        ["checks", var id, _, ..] => id,
        _ => null,
    };

    private static async Task<IEnumerable<string>> GitAsync(string root, CancellationToken cancellationToken, params string[] arguments)
    {
        var result = await ProcessRunner.RunAsync("git", arguments, root, GitTimeout, cancellationToken);
        if (result.ExitCode != 0 || result.TimedOut)
        {
            var reason = result.TimedOut ? "it timed out" : result.Stderr.Trim().Split('\n')[0];
            throw new EnvironmentException($"'git {arguments[0]}' failed: {reason}");
        }

        return result.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
