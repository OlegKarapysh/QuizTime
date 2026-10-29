namespace QuizBank.Model;

public sealed record Concept(string Id, string Idea, string Area, IReadOnlyList<string> Nodes);
