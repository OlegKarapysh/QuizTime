using System.Text.Json.Serialization;

namespace QuizBank.Model;

public sealed record Question(
    string Id,
    string Roadmap,
    string Node,
    string Concept,
    string Area,
    Level Level,
    QuestionType Type,
    IReadOnlyDictionary<string, string> Targets,
    string Stem,
    QuestionCode? Code,
    IReadOnlyList<QuestionOption> Options,
    [property: JsonConverter(typeof(OneOrManyConverter))] IReadOnlyList<string> Answer,
    Explanation Explanation,
    IReadOnlyList<Source> Sources,
    Verification? Verification,
    Quarantine? Quarantine,
    QuestionStatus Status);

public enum Level { Beginner, Intermediate, Advanced }

public enum QuestionType { SingleChoice, MultiChoice, Ordering }

public enum QuestionStatus { Draft, Verified, Reviewed, Quarantined, Retired }

public sealed record QuestionCode(string Lang, string Text);

public sealed record QuestionOption(string Id, string Text);

public sealed record Explanation(string Takeaway, string Details);

public enum SourceKind { Proof, Further }

public sealed record Source(SourceKind Kind, string Title, string Url, string LookFor, DateOnly Checked);

public enum VerificationMethod { Run, Sql, Docs }

public sealed record Verification(VerificationMethod Method, string? Check, BlindSolve? BlindSolve, Audit? Audit);

public sealed record BlindSolve(
    string Model,
    [property: JsonConverter(typeof(OneOrManyConverter))] IReadOnlyList<string> Picked,
    bool Agreed,
    DateOnly Date);

public enum AuditVerdict { Pass, Fail }

public sealed record Audit(AuditVerdict Verdict, DateOnly Date, string? Notes);

public sealed record Quarantine(string Reason, DateOnly Date);
