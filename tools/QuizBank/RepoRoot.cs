namespace QuizBank;

public static class RepoRoot
{
    /// <summary>The nearest directory at or above <paramref name="start"/> that holds schema/question.schema.json.</summary>
    public static string? Find(string start)
    {
        for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
        {
            if (IsRoot(directory.FullName)) return directory.FullName;
        }

        return null;
    }

    public static bool IsRoot(string directory) => File.Exists(Path.Combine(directory, "schema", "question.schema.json"));
}
