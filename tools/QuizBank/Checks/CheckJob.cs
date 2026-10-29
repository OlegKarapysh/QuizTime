namespace QuizBank.Checks;

public enum CheckKind { Run, Sql }

/// <summary>
/// One question's check: a C# file-based app (method: run) or a script (method: sql). <see cref="Path"/> is
/// relative to the repo root. Database checks get their own database, named after the question.
/// </summary>
public sealed record CheckJob(string Id, CheckKind Kind, string Path, bool NeedsDatabase)
{
    public string DatabaseName => "check_" + Id.Replace('-', '_');
}
