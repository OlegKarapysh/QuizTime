using System.Net;

namespace QuizBank.Tests;

public sealed class CliTests
{
    // validate

    [Fact]
    public async Task Validate_passes_on_the_real_repo()
    {
        var (exitCode, stdout, _) = await RunAsync("validate", "--root", RealRepo.Root);

        Assert.Equal(0, exitCode);
        Assert.StartsWith("validate: OK", stdout);
    }

    [Fact]
    public async Task Validate_fails_with_located_errors()
    {
        using var repo = new TestRepo();
        repo.Replace(TestRepo.QuestionPath, "answer: a", "answer: e");

        var (exitCode, stdout, _) = await RunAsync("validate", "--root", repo.Root);

        Assert.Equal(1, exitCode);
        Assert.Contains($"{TestRepo.QuestionPath}:{repo.LineOf(TestRepo.QuestionPath, "answer: e")}:9: Answer 'e' is not an option id.", stdout);
        Assert.Contains("validate: 1 error in", stdout);
    }

    [Fact]
    public async Task Validate_says_when_there_was_no_denylist()
    {
        using var repo = new TestRepo();

        var (_, stdout, _) = await RunAsync("validate", "--root", repo.Root);

        Assert.Equal("validate: OK, 1 roadmap (3 nodes), 1 concept, 1 question; no denylist found, so names were not checked\n", stdout);
    }

    [Fact]
    public async Task Validate_uses_the_denylist_it_is_given()
    {
        using var repo = new TestRepo();
        using var other = new TestRepo();
        other.Write("private/names.txt", "print\n");

        var (exitCode, stdout, _) = await RunAsync("validate", "--root", repo.Root, "--denylist", Path.Combine(other.Root, "private", "names.txt"));

        Assert.Equal(1, exitCode);
        Assert.EndsWith("validate: 1 error in 1 roadmap (3 nodes), 1 concept, 1 question\n", stdout);
    }

    // run-checks

    [Fact]
    public async Task Run_checks_runs_and_summarizes_the_checks()
    {
        using var repo = new TestRepo();
        repo.Write(TestRepo.CheckPath, "Console.WriteLine(\"PASS csharp-001: printed True\");\nreturn 0;\n");

        var (exitCode, stdout, _) = await RunAsync("run-checks", "--root", repo.Root);

        Assert.Equal(0, exitCode);
        Assert.Equal("PASS csharp-001: printed True\nrun-checks: OK, 1 check (1 run, 0 sql)\n", stdout);
    }

    [Fact]
    public async Task Run_checks_exits_1_when_a_check_fails()
    {
        using var repo = new TestRepo();

        var (exitCode, stdout, _) = await RunAsync("run-checks", "--root", repo.Root, "csharp-001");

        Assert.Equal(1, exitCode);
        Assert.EndsWith("run-checks: 1 of 1 check failed (1 run, 0 sql)\n", stdout);
    }

    [Fact]
    public async Task Run_checks_with_nothing_to_run_passes()
    {
        using var repo = new TestRepo();
        repo.Replace(TestRepo.QuestionPath, "status: draft", "status: retired");

        var (exitCode, stdout, _) = await RunAsync("run-checks", "--root", repo.Root);

        Assert.Equal(0, exitCode);
        Assert.Equal("run-checks: OK, no checks selected\n", stdout);
    }

    [Fact]
    public async Task Unknown_question_id_exits_with_2()
    {
        using var repo = new TestRepo();

        var (exitCode, _, stderr) = await RunAsync("run-checks", "--root", repo.Root, "csharp-404");

        Assert.Equal(2, exitCode);
        Assert.Equal("No question has id 'csharp-404'.\n", stderr);
    }

    [Fact]
    public async Task Run_checks_refuses_an_invalid_bank()
    {
        using var repo = new TestRepo();
        repo.Replace(TestRepo.QuestionPath, "answer: a", "answer: e");

        var (exitCode, _, stderr) = await RunAsync("run-checks", "--root", repo.Root);

        Assert.Equal(2, exitCode);
        Assert.Equal("tools/validate reports 1 error; fix them first.\n", stderr);
    }

    // check-links

    [Fact]
    public async Task Check_links_reports_broken_links_and_exits_1()
    {
        using var repo = new TestRepo();
        using var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var (exitCode, stdout, _) = await RunAsync(["check-links", "--root", repo.Root], handler);

        Assert.Equal(1, exitCode);
        Assert.EndsWith("returned HTTP 404 Not Found.\ncheck-links: 1 broken of 1 link in 1 question\n", stdout);
    }

    [Fact]
    public async Task Check_links_passes_when_every_page_loads()
    {
        using var repo = new TestRepo();
        using var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html></html>") });

        var (exitCode, stdout, _) = await RunAsync(["check-links", "--root", repo.Root], handler);

        Assert.Equal(0, exitCode);
        Assert.Equal("check-links: OK, 1 link in 1 question\n", stdout);
    }

    // Usage

    [Theory]
    [InlineData]
    [InlineData("lint")]
    [InlineData("validate", "--verbose")]
    [InlineData("validate", "--root")]
    [InlineData("validate", "csharp-001")]
    [InlineData("validate", "--changed-since", "main")]
    [InlineData("check-links", "--denylist", "names.txt")]
    [InlineData("run-checks", "csharp-001", "--changed-since", "main")]
    [InlineData("run-checks", "--changed-since")]
    public async Task Bad_usage_exits_with_2(params string[] args)
    {
        var (exitCode, _, stderr) = await RunAsync(args);

        Assert.Equal(2, exitCode);
        Assert.Contains("Usage: quizbank", stderr);
    }

    [Fact]
    public async Task Root_must_hold_the_schemas()
    {
        var (exitCode, _, stderr) = await RunAsync("validate", "--root", Path.GetTempPath());

        Assert.Equal(2, exitCode);
        Assert.Contains("Could not find the repo root", stderr);
    }

    private static Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(params string[] args) => RunAsync(args, null);

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(string[] args, HttpMessageHandler? httpHandler)
    {
        var stdout = new StringWriter { NewLine = "\n" };
        var stderr = new StringWriter { NewLine = "\n" };
        var exitCode = await Cli.RunAsync(args, stdout, stderr, httpHandler);
        return (exitCode, stdout.ToString(), stderr.ToString());
    }
}
