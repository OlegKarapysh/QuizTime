using QuizBank.Checks;

namespace QuizBank.Tests;

/// <summary>C# checks run for real with dotnet run. They need the .NET SDK but no database.</summary>
public sealed class CheckRunnerTests : IDisposable
{
    private readonly TestRepo _repo = new();

    public void Dispose() => _repo.Dispose();

    // Running C# checks

    [Fact]
    public async Task Passing_check_reports_its_pass_line()
    {
        _repo.Write(TestRepo.CheckPath, """
            Console.WriteLine("PASS csharp-001: printed True");
            return 0;
            """);

        var result = await RunAsync();

        Assert.Equal("PASS csharp-001: printed True", result.PassLine);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task Failing_check_reports_its_fail_line()
    {
        _repo.Write(TestRepo.CheckPath, """
            Console.Error.WriteLine("FAIL csharp-001: printed False; expected True");
            return 1;
            """);

        var result = await RunAsync();

        Assert.False(result.Passed);
        Assert.Equal($"{TestRepo.CheckPath}: FAIL csharp-001: printed False; expected True", Assert.Single(result.Errors).ToString());
    }

    [Fact]
    public async Task Check_that_does_not_compile_reports_errors_at_their_lines()
    {
        _repo.Write(TestRepo.CheckPath, "Console.WriteLine(\"ok\");\nvar total = missing;\nreturn 0;\n");

        var result = await RunAsync();

        Assert.Equal(
            $"{TestRepo.CheckPath}:2:13: error CS0103: The name 'missing' does not exist in the current context",
            Assert.Single(result.Errors).ToString());
    }

    [Fact]
    public async Task Check_that_exits_0_without_a_pass_line_fails()
    {
        _repo.Write(TestRepo.CheckPath, "return 0;\n");

        var result = await RunAsync();

        Assert.Equal($"{TestRepo.CheckPath}: Exited with code 0 but printed no 'PASS csharp-001:' line.", Assert.Single(result.Errors).ToString());
    }

    [Fact]
    public async Task Pass_line_must_name_its_own_question()
    {
        _repo.Write(TestRepo.CheckPath, """
            Console.WriteLine("PASS csharp-002: copied from another check");
            return 0;
            """);

        var result = await RunAsync();

        Assert.False(result.Passed);
    }

    [Fact]
    public async Task Crashing_check_reports_the_exception()
    {
        _repo.Write(TestRepo.CheckPath, "throw new InvalidOperationException(\"boom\");\n");

        var result = await RunAsync();

        var message = Assert.Single(result.Errors).Message;
        Assert.StartsWith("Exited with code ", message);
        Assert.EndsWith("and printed no FAIL line: Unhandled exception. System.InvalidOperationException: boom", message);
    }

    // Interpreting the output

    [Fact]
    public void Restore_errors_without_a_position_are_reported_on_the_check()
    {
        var job = new CheckJob("csharp-001", CheckKind.Run, TestRepo.CheckPath, NeedsDatabase: false);
        var stdout = "/repo/checks/csharp-001/check.cs.csproj : error NU1101: Unable to find package Nope. No packages exist with this id in source(s): nuget.org\n";

        var result = DotnetCheck.Interpret(job, new ProcessResult(1, stdout, "The build failed.", TimedOut: false));

        Assert.Equal(
            $"{TestRepo.CheckPath}: error NU1101: Unable to find package Nope. No packages exist with this id in source(s): nuget.org",
            Assert.Single(result.Errors).ToString());
    }

    [Fact]
    public void Timed_out_check_fails()
    {
        var job = new CheckJob("csharp-001", CheckKind.Run, TestRepo.CheckPath, NeedsDatabase: false);

        var result = DotnetCheck.Interpret(job, new ProcessResult(-1, "", "", TimedOut: true));

        Assert.Equal($"{TestRepo.CheckPath}: Timed out after 5 minutes.", Assert.Single(result.Errors).ToString());
    }

    // Choosing the checks

    [Fact]
    public void Docs_questions_have_no_check_to_run()
    {
        _repo.Replace(TestRepo.QuestionPath, "  method: run\n  check: checks/csharp-001/check.cs", "  method: docs");
        _repo.Delete(TestRepo.CheckPath);

        Assert.Empty(CheckRunner.Jobs(_repo.Root, _repo.Questions()));
    }

    [Fact]
    public void Retired_questions_are_not_run()
    {
        _repo.Replace(TestRepo.QuestionPath, "status: draft", "status: retired");

        Assert.Empty(CheckRunner.Jobs(_repo.Root, _repo.Questions()));
    }

    [Fact]
    public void Checks_that_read_QUIZTIME_PG_need_a_database()
    {
        _repo.Write(TestRepo.CheckPath, "var cs = Environment.GetEnvironmentVariable(\"QUIZTIME_PG\");\n");

        var job = Assert.Single(CheckRunner.Jobs(_repo.Root, _repo.Questions()));

        Assert.Equal(new CheckJob("csharp-001", CheckKind.Run, TestRepo.CheckPath, NeedsDatabase: true), job);
        Assert.Equal("check_csharp_001", job.DatabaseName);
    }

    [Fact]
    public void Sql_checks_always_need_a_database()
    {
        _repo.UseSqlCheck("SELECT 1;\n-- --- expected ---\n-- --- end expected ---\n");

        var job = Assert.Single(CheckRunner.Jobs(_repo.Root, _repo.Questions()));

        Assert.Equal(new CheckJob("csharp-001", CheckKind.Sql, TestRepo.SqlCheckPath, NeedsDatabase: true), job);
    }

    private async Task<CheckResult> RunAsync()
    {
        var jobs = CheckRunner.Jobs(_repo.Root, _repo.Questions());
        return Assert.Single(await CheckRunner.RunAsync(_repo.Root, jobs, server: null, TestContext.Current.CancellationToken));
    }
}
