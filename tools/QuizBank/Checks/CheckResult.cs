using QuizBank.Validation;

namespace QuizBank.Checks;

/// <summary>
/// The outcome of one check: its PASS line, or the errors that explain the failure. <see cref="Output"/> is the
/// full output of a failing SQL check, so its expected block can be updated from it.
/// </summary>
public sealed record CheckResult(string Id, string? PassLine, IReadOnlyList<Diagnostic> Errors, IReadOnlyList<string> Output)
{
    public bool Passed => PassLine is not null;

    public static CheckResult Pass(CheckJob job, string passLine) => new(job.Id, passLine, [], []);

    public static CheckResult Fail(CheckJob job, params IReadOnlyList<Diagnostic> errors) => new(job.Id, null, errors, []);

    public static CheckResult Fail(CheckJob job, IReadOnlyList<string> output, Diagnostic error) => new(job.Id, null, [error], output);
}
