namespace QuizBank.Tests;

public sealed class CliTests
{
    [Fact]
    public void Validate_passes_on_the_real_repo()
    {
        var (exitCode, stdout, _) = Run("validate", "--root", RealRepo.Root);

        Assert.Equal(0, exitCode);
        Assert.StartsWith("validate: OK", stdout);
    }

    [Fact]
    public void Validate_fails_with_located_errors()
    {
        using var repo = new TestRepo();
        repo.Replace(TestRepo.QuestionPath, "answer: a", "answer: e");

        var (exitCode, stdout, _) = Run("validate", "--root", repo.Root);

        Assert.Equal(1, exitCode);
        Assert.Contains($"{TestRepo.QuestionPath}:{repo.LineOf(TestRepo.QuestionPath, "answer: e")}:9: Answer 'e' is not an option id.", stdout);
        Assert.Contains("validate: 1 error in", stdout);
    }

    [Theory]
    [InlineData]
    [InlineData("lint")]
    [InlineData("validate", "--verbose")]
    [InlineData("validate", "--root")]
    public void Bad_usage_exits_with_2(params string[] args)
    {
        var (exitCode, _, stderr) = Run(args);

        Assert.Equal(2, exitCode);
        Assert.Contains("Usage: quizbank", stderr);
    }

    [Fact]
    public void Root_must_hold_the_schemas()
    {
        var (exitCode, _, stderr) = Run("validate", "--root", Path.GetTempPath());

        Assert.Equal(2, exitCode);
        Assert.Contains("Could not find the repo root", stderr);
    }

    private static (int ExitCode, string Stdout, string Stderr) Run(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exitCode = Cli.Run(args, stdout, stderr);
        return (exitCode, stdout.ToString(), stderr.ToString());
    }
}
