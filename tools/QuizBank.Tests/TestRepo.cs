using QuizBank.Model;
using QuizBank.Validation;

namespace QuizBank.Tests;

/// <summary>The repository these tests run in.</summary>
internal static class RealRepo
{
    public static string Root { get; } = RepoRoot.Find(AppContext.BaseDirectory)
        ?? throw new InvalidOperationException("Tests must run inside the repo.");
}

/// <summary>
/// A throwaway bank in a temp folder: the real schemas plus a small valid roadmap, concept file,
/// question and check. Tests break one thing and assert the error it produces.
/// </summary>
internal sealed class TestRepo : IDisposable
{
    public const string RoadmapPath = "roadmaps/sample.yaml";
    public const string ConceptsPath = "concepts/sample.yaml";
    public const string QuestionPath = "questions/sample/csharp/csharp-001.yaml";
    public const string CheckPath = "checks/csharp-001/check.cs";
    public const string SqlCheckPath = "checks/csharp-001/check.sql";

    public TestRepo()
    {
        Root = Directory.CreateTempSubdirectory("quizbank-tests-").FullName;
        foreach (var schema in Directory.GetFiles(Path.Combine(RealRepo.Root, "schema"), "*.json"))
            Write($"schema/{Path.GetFileName(schema)}", File.ReadAllText(schema));

        Write(RoadmapPath, Roadmap);
        Write(ConceptsPath, Concepts);
        Write(QuestionPath, Question);
        Write(CheckPath, "return 0;\n");
    }

    public string Root { get; }

    public void Write(string path, string content)
    {
        var full = Full(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    public void Replace(string path, string oldText, string newText)
    {
        var text = File.ReadAllText(Full(path));
        Assert.Contains(oldText, text);
        File.WriteAllText(Full(path), text.Replace(oldText, newText));
    }

    public void Move(string from, string to)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Full(to))!);
        File.Move(Full(from), Full(to));
    }

    public void Delete(string path) => File.Delete(Full(path));

    /// <summary>1-based line of the first line in <paramref name="path"/> that contains <paramref name="text"/>.</summary>
    public int LineOf(string path, string text) =>
        File.ReadAllLines(Full(path)).Index().First(line => line.Item.Contains(text)).Index + 1;

    /// <summary>Switches the sample question to method: sql with this script as its check.</summary>
    public void UseSqlCheck(string script)
    {
        Replace(QuestionPath, "method: run", "method: sql");
        Replace(QuestionPath, $"check: {CheckPath}", $"check: {SqlCheckPath}");
        Delete(CheckPath);
        Write(SqlCheckPath, script);
    }

    public ValidationReport Validate() => BankValidator.Validate(Root);

    /// <summary>The bank's questions, which must be valid.</summary>
    public IReadOnlyList<BankFile<Question>> Questions()
    {
        var report = Validate();
        Assert.Empty(report.Errors);
        return report.QuestionFiles;
    }

    public void Dispose() => Directory.Delete(Root, recursive: true);

    private string Full(string path) => Path.Combine(Root, path);

    private const string Roadmap = """
        id: sample
        title: Sample
        source: { name: Sample roadmap, total_nodes: 5 }
        nodes:
          - id: language
            title: Language
          - id: csharp
            title: C#
            parent: language
            areas: [C# language features]
          - id: linq
            title: LINQ
            parent: language
        """;

    private const string Concepts = """
        - id: operator-binding-static
          idea: Operators are chosen at compile time from the static types
          area: C# language features
          nodes: [csharp]
        """;

    private const string Question = """
        id: csharp-001
        roadmap: sample
        node: csharp
        concept: operator-binding-static
        area: C# language features
        level: intermediate
        type: single-choice
        targets: { dotnet: "10.0" }
        stem: What does this print?
        code:
          lang: csharp
          text: |
            object a = "x";
            Console.WriteLine(a == "x");
        options:
          - { id: a, text: "Prints True" }
          - { id: b, text: "Prints False" }
          - { id: c, text: "Fails to build" }
          - { id: d, text: "Throws at run" }
        answer: a
        explanation:
          takeaway: Literals are interned.
          details: Both sides refer to the same interned string.
        sources:
          - kind: proof
            title: String interning
            url: https://learn.microsoft.com/dotnet/api/system.string.intern?view=net-10.0
            look_for: The runtime keeps one instance per literal
            checked: 2026-10-29
        verification:
          method: run
          check: checks/csharp-001/check.cs
        status: draft
        """;
}
