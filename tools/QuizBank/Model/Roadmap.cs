namespace QuizBank.Model;

public sealed record Roadmap(string Id, string Title, RoadmapSource? Source, IReadOnlyList<RoadmapNode> Nodes);

public sealed record RoadmapSource(string Name, int TotalNodes, string? Note);

public sealed record RoadmapNode(string Id, string Title, string? Parent, IReadOnlyList<string>? Areas);
