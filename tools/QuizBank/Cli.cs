using QuizBank.Validation;

namespace QuizBank;

public static class Cli
{
    private const string Usage = """
        Usage: quizbank <command> [--root <repo-root>]

        Commands:
          validate   Check roadmaps, concepts and questions against the schemas and against each other.

        Without --root, the repo root is the nearest directory at or above the current one
        that holds schema/question.schema.json.
        """;

    /// <summary>Exit codes: 0 passed, 1 found errors, 2 bad usage.</summary>
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args is ["-h" or "--help" or "help"])
        {
            stdout.WriteLine(Usage);
            return 0;
        }

        if (args is not ["validate", .. var options]) return UsageError(stderr, args is [] ? null : $"Unknown command '{args[0]}'.");

        string? root = null;
        for (var i = 0; i < options.Length; i++)
        {
            if (options[i] == "--root" && i + 1 < options.Length) root = options[++i];
            else return UsageError(stderr, $"Unknown argument '{options[i]}'.");
        }

        root = root is null ? RepoRoot.Find(Directory.GetCurrentDirectory()) : Path.GetFullPath(root);
        if (root is null || !RepoRoot.IsRoot(root))
            return UsageError(stderr, "Could not find the repo root (a directory with schema/question.schema.json). Pass --root.");

        return Validate(root, stdout);
    }

    private static int Validate(string root, TextWriter stdout)
    {
        var report = BankValidator.Validate(root);
        foreach (var error in report.Errors) stdout.WriteLine(error);

        var counts = $"{Count(report.Roadmaps, "roadmap")} ({Count(report.Nodes, "node")}), " +
                     $"{Count(report.Concepts, "concept")}, {Count(report.Questions, "question")}";
        stdout.WriteLine(report.IsValid ? $"validate: OK, {counts}" : $"validate: {Count(report.Errors.Count, "error")} in {counts}");
        return report.IsValid ? 0 : 1;
    }

    private static int UsageError(TextWriter stderr, string? message)
    {
        if (message is not null) stderr.WriteLine(message);
        stderr.WriteLine(Usage);
        return 2;
    }

    private static string Count(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";
}
