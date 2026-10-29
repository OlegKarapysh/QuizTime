using System.Text.RegularExpressions;
using QuizBank.Validation;

namespace QuizBank.Checks;

/// <summary>
/// Runs a check.cs with <c>dotnet run</c> from the repo root, so global.json picks the SDK. It passes only when it
/// exits 0 and prints "PASS &lt;id&gt;:"; otherwise the FAIL line, the compiler errors or the crash explain why.
/// </summary>
public static partial class DotnetCheck
{
    // The first run of a check restores its packages, which can take a while on a cold cache.
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    public static async Task<CheckResult> RunAsync(CheckJob job, string root, string? connectionString, CancellationToken cancellationToken)
    {
        var environment = new Dictionary<string, string> { ["DOTNET_NOLOGO"] = "1", ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1" };
        if (connectionString is not null) environment[PostgresServer.ConnectionVariable] = connectionString;

        var result = await ProcessRunner.RunAsync("dotnet", ["run", job.Path], root, Timeout, cancellationToken, environment);
        return Interpret(job, result);
    }

    public static CheckResult Interpret(CheckJob job, ProcessResult result)
    {
        if (result.TimedOut) return CheckResult.Fail(job, Error(job, $"Timed out after {Timeout.TotalMinutes:0} minutes."));

        if (result.ExitCode == 0)
        {
            return Lines(result.Stdout).FirstOrDefault(line => line.StartsWith($"PASS {job.Id}:", StringComparison.Ordinal)) is { } pass
                ? CheckResult.Pass(job, pass)
                : CheckResult.Fail(job, Error(job, $"Exited with code 0 but printed no 'PASS {job.Id}:' line."));
        }

        // The build prints its errors to stdout.
        var buildErrors = Lines(result.Stdout).Select(line => BuildError(job, line)).OfType<Diagnostic>().Distinct().ToList();
        if (buildErrors.Count > 0) return CheckResult.Fail(job, buildErrors);

        if (Lines(result.Stderr).Concat(Lines(result.Stdout)).FirstOrDefault(line => line.StartsWith("FAIL ", StringComparison.Ordinal)) is { } fail)
            return CheckResult.Fail(job, Error(job, fail));

        var reason = Lines(result.Stderr).FirstOrDefault() is { } first ? $": {first}" : ".";
        return CheckResult.Fail(job, Error(job, $"Exited with code {result.ExitCode} and printed no FAIL line{reason}"));
    }

    private static Diagnostic? BuildError(CheckJob job, string line)
    {
        var match = BuildErrorLine().Match(line);
        if (!match.Success) return null;

        var message = $"error {match.Groups["code"].Value}: {match.Groups["message"].Value}";
        var inCheck = match.Groups["path"].Value.Replace('\\', '/').EndsWith("/" + job.Path, StringComparison.Ordinal);
        return inCheck && match.Groups["line"].Success
            ? new Diagnostic(job.Path, int.Parse(match.Groups["line"].Value), int.Parse(match.Groups["column"].Value), message)
            : Error(job, message);
    }

    private static Diagnostic Error(CheckJob job, string message) => new(job.Path, null, null, message);

    private static IEnumerable<string> Lines(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // "/abs/checks/x/check.cs(3,10): error CS1002: ; expected", or a restore error without a position,
    // optionally followed by the project in brackets.
    [GeneratedRegex(@"^(?<path>.+?)(?:\((?<line>\d+),(?<column>\d+)\))?\s*:\s*error (?<code>[A-Z]+\d+): (?<message>.+?)(?: \[[^\]]+\])?$")]
    private static partial Regex BuildErrorLine();
}
