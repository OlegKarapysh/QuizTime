namespace QuizBank.Tests;

public sealed class QuestionSelectionTests : IDisposable
{
    private readonly TestRepo _repo = new();

    public void Dispose() => _repo.Dispose();

    [Theory]
    [InlineData("questions/sample/csharp/csharp-001.yaml")]
    [InlineData("checks/csharp-001/check.cs")]
    [InlineData("checks/csharp-001/data/seed.sql")]
    public void A_changed_question_or_check_selects_its_question(string file)
    {
        var selected = QuestionSelection.ByChangedFiles(_repo.Questions(), [file]);

        Assert.Equal("csharp-001", Assert.Single(selected).Value.Id);
    }

    [Theory]
    [InlineData("docs/PLAN.md")]
    [InlineData("concepts/sample.yaml")]
    [InlineData("tools/QuizBank.Tests/CliTests.cs")]
    [InlineData("checks/csharp-002/check.cs")]
    public void Unrelated_changes_select_nothing(string file)
    {
        Assert.Empty(QuestionSelection.ByChangedFiles(_repo.Questions(), [file]));
    }

    [Theory]
    [InlineData("tools/QuizBank/Checks/SqlCheck.cs")]
    [InlineData("tools/Directory.Packages.props")]
    [InlineData("tools/run-checks")]
    [InlineData("global.json")]
    public void Tool_and_sdk_changes_select_every_question(string file)
    {
        Assert.Single(QuestionSelection.ByChangedFiles(_repo.Questions(), ["docs/PLAN.md", file]));
    }

    [Fact]
    public void Unknown_ids_are_reported()
    {
        var error = Assert.Throws<ArgumentException>(() => QuestionSelection.ByIds(_repo.Questions(), ["csharp-001", "csharp-404"]));

        Assert.Equal("No question has id 'csharp-404'.", error.Message);
    }

    [Fact]
    public async Task Changed_files_include_commits_edits_and_untracked_files_since_the_merge_base()
    {
        await GitAsync("init", "--quiet", "--initial-branch=main");
        await GitAsync("add", "--all");
        await GitAsync("commit", "--quiet", "--message=Base");
        await GitAsync("switch", "--quiet", "--create", "feature");
        _repo.Write("docs/notes.md", "Committed on the branch.\n");
        await GitAsync("add", "--all");
        await GitAsync("commit", "--quiet", "--message=Notes");
        _repo.Replace(TestRepo.QuestionPath, "status: draft", "status: draft\n");
        _repo.Write("checks/csharp-002/check.cs", "return 0;\n");

        var changed = await QuestionSelection.ChangedFilesAsync(_repo.Root, "main", TestContext.Current.CancellationToken);

        Assert.Equal(["checks/csharp-002/check.cs", "docs/notes.md", TestRepo.QuestionPath], changed);
    }

    [Fact]
    public async Task Unknown_git_ref_is_an_environment_error()
    {
        await GitAsync("init", "--quiet", "--initial-branch=main");

        var error = await Assert.ThrowsAsync<EnvironmentException>(() =>
            QuestionSelection.ChangedFilesAsync(_repo.Root, "no-such-ref", TestContext.Current.CancellationToken));

        Assert.StartsWith("'git diff' failed: ", error.Message);
    }

    // The user's global config may sign commits or lack an identity; tests must not depend on it.
    private async Task GitAsync(params string[] arguments)
    {
        string[] config = ["-c", "user.name=Tests", "-c", "user.email=tests@example.com", "-c", "commit.gpgsign=false"];
        var result = await ProcessRunner.RunAsync("git", [.. config, .. arguments], _repo.Root, TimeSpan.FromMinutes(1),
            TestContext.Current.CancellationToken);
        Assert.True(result.ExitCode == 0, $"git {string.Join(' ', arguments)} failed: {result.Stderr}");
    }
}
