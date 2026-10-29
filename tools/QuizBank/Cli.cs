using QuizBank.Checks;
using QuizBank.Links;
using QuizBank.Model;
using QuizBank.Validation;

namespace QuizBank;

public static class Cli
{
    private const string Usage = """
        Usage: quizbank <command> [--root <repo-root>] [options]

        Commands:
          validate      Gate 1: check roadmaps, concepts and questions against the schemas, against each
                        other and against the lint rules.
                          --denylist <file>   Names that must not appear (default: private/denylist.txt).
          run-checks    Gate 2: run each question's check. Database checks get PostgreSQL 16 from
                        QUIZTIME_PG, or from a Docker container started for the run.
          check-links   Open every source link and check that the page and its anchor exist.

          run-checks and check-links cover every question, or only:
                          <id>...                   these questions
                          --changed-since <git-ref> questions changed since the merge base with the ref
                                                    (all of them when the tools, build files or CI changed)

        Without --root, the repo root is the nearest directory at or above the current one
        that holds schema/question.schema.json.

        Exit codes: 0 passed, 1 found errors, 2 bad usage or environment, 130 cancelled (Ctrl+C).
        """;

    private sealed record Options(string Root, string? Denylist, string? ChangedSince, IReadOnlyList<string> Ids);

    /// <param name="httpHandler">The link checker's HTTP handler; tests pass a fake one.</param>
    /// <param name="cancellationToken">Cancelled on Ctrl+C. The run stops, cleans up and exits with 130.</param>
    public static async Task<int> RunAsync(
        string[] args, TextWriter stdout, TextWriter stderr, HttpMessageHandler? httpHandler = null, CancellationToken cancellationToken = default)
    {
        if (args is ["-h" or "--help" or "help"])
        {
            await stdout.WriteLineAsync(Usage);
            return 0;
        }

        if (args is not [("validate" or "run-checks" or "check-links") and var command, .. var rest])
            return UsageError(stderr, args is [] ? null : $"Unknown command '{args[0]}'.");

        var (options, error) = Parse(command, rest);
        if (options is null) return UsageError(stderr, error);

        try
        {
            return command switch
            {
                "validate" => Validate(options, stdout),
                "run-checks" => await RunChecksAsync(options, stdout, stderr, cancellationToken),
                _ => await CheckLinksAsync(options, stdout, httpHandler, cancellationToken),
            };
        }
        catch (EnvironmentException e)
        {
            await stderr.WriteLineAsync(e.Message);
            return 2;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await stderr.WriteLineAsync($"{command}: cancelled.");
            return 130;
        }
    }

    private static (Options? Options, string? Error) Parse(string command, string[] rest)
    {
        string? root = null, denylist = null, changedSince = null;
        var ids = new List<string>();
        for (var i = 0; i < rest.Length; i++)
        {
            var hasValue = i + 1 < rest.Length;
            switch (rest[i])
            {
                case "--root" when hasValue:
                    root = rest[++i];
                    break;
                case "--denylist" when hasValue && command == "validate":
                    denylist = Path.GetFullPath(rest[++i]);
                    break;
                case "--changed-since" when hasValue && command != "validate":
                    changedSince = rest[++i];
                    break;
                case var id when command != "validate" && !id.StartsWith('-'):
                    ids.Add(id);
                    break;
                default:
                    return (null, $"Unknown argument '{rest[i]}'.");
            }
        }

        if (changedSince is not null && ids.Count > 0) return (null, "Pass question ids or --changed-since, not both.");

        root = root is null ? RepoRoot.Find(Directory.GetCurrentDirectory()) : Path.GetFullPath(root);
        if (root is null || !RepoRoot.IsRoot(root))
            return (null, "Could not find the repo root (a directory with schema/question.schema.json). Pass --root.");

        return (new Options(root, denylist, changedSince, ids), null);
    }

    private static int Validate(Options options, TextWriter stdout)
    {
        var report = BankValidator.Validate(options.Root, options.Denylist);
        foreach (var error in report.Errors) stdout.WriteLine(error);

        var counts = $"{Count(report.Roadmaps, "roadmap")} ({Count(report.Nodes, "node")}), " +
                     $"{Count(report.Concepts, "concept")}, {Count(report.Questions, "question")}";
        var denylist = report.DenylistChecked ? "" : "; no denylist found, so names were not checked";
        stdout.WriteLine(report.IsValid
            ? $"validate: OK, {counts}{denylist}"
            : $"validate: {Count(report.Errors.Count, "error")} in {counts}{denylist}");
        return report.IsValid ? 0 : 1;
    }

    private static async Task<int> RunChecksAsync(Options options, TextWriter stdout, TextWriter stderr, CancellationToken cancellationToken)
    {
        var jobs = CheckRunner.Jobs(options.Root, await SelectAsync(options, cancellationToken));
        if (jobs.Count == 0)
        {
            await stdout.WriteLineAsync("run-checks: OK, no checks selected");
            return 0;
        }

        await using var server = jobs.Any(job => job.NeedsDatabase) ? await PostgresServer.StartAsync(stderr, cancellationToken) : null;
        var results = await CheckRunner.RunAsync(options.Root, jobs, server, cancellationToken);

        foreach (var result in results)
        {
            if (result.Passed) await stdout.WriteLineAsync(result.PassLine);
            foreach (var error in result.Errors) await stdout.WriteLineAsync(error.ToString());
            foreach (var line in result.Output) await stdout.WriteLineAsync($"    {line}");
        }

        var failed = results.Count(r => !r.Passed);
        var kinds = $"{jobs.Count(j => j.Kind == CheckKind.Run)} run, {jobs.Count(j => j.Kind == CheckKind.Sql)} sql";
        await stdout.WriteLineAsync(failed == 0
            ? $"run-checks: OK, {Count(jobs.Count, "check")} ({kinds})"
            : $"run-checks: {failed} of {Count(jobs.Count, "check")} failed ({kinds})");
        return failed == 0 ? 0 : 1;
    }

    private static async Task<int> CheckLinksAsync(
        Options options, TextWriter stdout, HttpMessageHandler? httpHandler, CancellationToken cancellationToken)
    {
        var questions = (await SelectAsync(options, cancellationToken)).Where(q => q.Value.Status != QuestionStatus.Retired).ToList();

        using var defaultHandler = httpHandler is null ? LinkChecker.CreateDefaultHandler() : null;
        var report = await new LinkChecker(httpHandler ?? defaultHandler!).CheckAsync(questions, cancellationToken);
        foreach (var error in report.Errors) await stdout.WriteLineAsync(error.ToString());

        var counts = $"{Count(report.Links, "link")} in {Count(report.Questions, "question")}";
        await stdout.WriteLineAsync(report.IsValid
            ? $"check-links: OK, {counts}"
            : $"check-links: {report.Errors.Count} broken of {counts}");
        return report.IsValid ? 0 : 1;
    }

    /// <summary>The questions a run-checks or check-links run covers. The bank must pass validation first.</summary>
    private static async Task<IReadOnlyList<BankFile<Question>>> SelectAsync(Options options, CancellationToken cancellationToken)
    {
        var report = BankValidator.Validate(options.Root);
        if (!report.IsValid)
            throw new EnvironmentException($"tools/validate reports {Count(report.Errors.Count, "error")}; fix them first.");

        if (options.Ids.Count > 0)
        {
            try
            {
                return QuestionSelection.ByIds(report.QuestionFiles, options.Ids);
            }
            catch (ArgumentException e)
            {
                throw new EnvironmentException(e.Message);
            }
        }

        if (options.ChangedSince is null) return report.QuestionFiles;
        var changed = await QuestionSelection.ChangedFilesAsync(options.Root, options.ChangedSince, cancellationToken);
        return QuestionSelection.ByChangedFiles(report.QuestionFiles, changed);
    }

    private static int UsageError(TextWriter stderr, string? message)
    {
        if (message is not null) stderr.WriteLine(message);
        stderr.WriteLine(Usage);
        return 2;
    }

    private static string Count(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";
}
