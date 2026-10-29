namespace QuizBank.Validation;

/// <summary>One validation error. <see cref="File"/> is relative to the repo root, with forward slashes.</summary>
public sealed record Diagnostic(string File, int? Line, int? Column, string Message)
{
    public override string ToString() =>
        Line is { } line ? $"{File}:{line}:{Column ?? 1}: {Message}" : $"{File}: {Message}";
}
