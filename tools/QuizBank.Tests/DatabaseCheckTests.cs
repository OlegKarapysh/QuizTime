using QuizBank.Checks;

namespace QuizBank.Tests;

/// <summary>
/// Database checks end to end against PostgreSQL 16 in Docker. They skip without Docker, except in CI
/// (QUIZTIME_REQUIRE_DOCKER=1), where they fail; see <see cref="PostgresFixture"/>.
/// </summary>
public sealed class DatabaseCheckTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IDisposable
{
    private const string Orders = """
        CREATE TABLE orders (id int PRIMARY KEY, total numeric(10,2));
        INSERT INTO orders VALUES (1, 10), (2, NULL);

        """;

    private readonly TestRepo _repo = new();

    public void Dispose() => _repo.Dispose();

    [Fact]
    public async Task Sql_check_passes_when_the_output_matches_the_expected_block()
    {
        _repo.UseSqlCheck(Orders + """
            SELECT count(total) AS priced, count(*) AS all_orders FROM orders;

            -- --- expected ---
            -- priced|all_orders
            -- 1|2
            -- --- end expected ---
            """);

        var result = await RunAsync();

        Assert.Equal("PASS csharp-001: printed 'priced|all_orders / 1|2', as the expected block says", result.PassLine);
    }

    [Fact]
    public async Task Sql_check_fails_at_the_first_line_that_differs()
    {
        _repo.UseSqlCheck(Orders + """
            SELECT count(total) AS priced, count(*) AS all_orders FROM orders;

            -- --- expected ---
            -- priced|all_orders
            -- 2|2
            -- --- end expected ---
            """);

        var result = await RunAsync();

        Assert.Equal(
            $"{TestRepo.SqlCheckPath}:{_repo.LineOf(TestRepo.SqlCheckPath, "-- 2|2")}:1: FAIL csharp-001: the expected block says " +
            "'2|2' here, but the output has '1|2'. The full output follows.",
            Assert.Single(result.Errors).ToString());
        Assert.Equal(["priced|all_orders", "1|2"], result.Output);
    }

    [Fact]
    public async Task Output_beyond_the_expected_block_fails()
    {
        _repo.UseSqlCheck(Orders + """
            SELECT id FROM orders ORDER BY id;

            -- --- expected ---
            -- id
            -- 1
            -- --- end expected ---
            """);

        var result = await RunAsync();

        Assert.Equal(
            $"{TestRepo.SqlCheckPath}:{_repo.LineOf(TestRepo.SqlCheckPath, "-- --- end expected ---")}:1: FAIL csharp-001: " +
            "the output continues with '2' after the expected block ends. The full output follows.",
            Assert.Single(result.Errors).ToString());
    }

    [Fact]
    public async Task Values_print_in_postgres_text_format()
    {
        _repo.UseSqlCheck("""
            SELECT NULL AS nothing, true AS yes, 1.50::numeric(4,2) AS price, 7 / 2 AS half, 'a' || NULL AS joined;

            -- --- expected ---
            -- nothing|yes|price|half|joined
            -- NULL|t|1.50|3|NULL
            -- --- end expected ---
            """);

        Assert.True((await RunAsync()).Passed);
    }

    [Fact]
    public async Task Errors_are_part_of_the_output_and_stop_the_script()
    {
        _repo.UseSqlCheck(Orders + """
            INSERT INTO orders VALUES (1, 5);
            SELECT 'never runs' AS skipped;

            -- --- expected ---
            -- ERROR 23505: duplicate key value violates unique constraint "orders_pkey"
            -- --- end expected ---
            """);

        Assert.True((await RunAsync()).Passed);
    }

    [Fact]
    public async Task Rows_read_before_an_error_are_not_printed()
    {
        // The server sends the row for x = 1 before x = 2 divides by zero; psql shows only the error.
        _repo.UseSqlCheck(Orders + """
            SELECT count(*) AS n FROM orders;
            SELECT 1 / (x - 2) AS r FROM generate_series(1, 3) x;

            -- --- expected ---
            -- n
            -- 2
            --
            -- ERROR 22012: division by zero
            -- --- end expected ---
            """);

        var result = await RunAsync();

        Assert.True(result.Passed, string.Join("\n", result.Errors.Concat<object>(result.Output)));
    }

    [Fact]
    public async Task Unexpected_error_names_the_failing_statement()
    {
        _repo.UseSqlCheck(Orders + """
            SELECT count(*) AS n FROM order_lines;

            -- --- expected ---
            -- n
            -- 0
            -- --- end expected ---
            """);

        var result = await RunAsync();

        Assert.Equal(
            "FAIL csharp-001: the expected block says 'n' here, but the output has 'ERROR 42P01: relation \"order_lines\" does " +
            "not exist' (the statement at line 3 failed). The full output follows.",
            Assert.Single(result.Errors).Message);
    }

    [Fact]
    public async Task Statements_run_one_at_a_time_like_psql()
    {
        // VACUUM can't run inside a transaction block, so it fails if the statements go as one batch.
        _repo.UseSqlCheck(Orders + """
            VACUUM orders;
            BEGIN;
            INSERT INTO orders VALUES (3, 1);
            ROLLBACK;
            SELECT count(*) AS n FROM orders;

            -- --- expected ---
            -- n
            -- 2
            -- --- end expected ---
            """);

        var result = await RunAsync();

        Assert.True(result.Passed, string.Join("\n", result.Errors.Concat<object>(result.Output)));
    }

    [Fact]
    public async Task Each_run_gets_a_fresh_database()
    {
        // CREATE TABLE would fail on the second run if the database were reused.
        _repo.UseSqlCheck(Orders + "-- --- expected ---\n-- --- end expected ---\n");

        Assert.True((await RunAsync()).Passed);
        Assert.True((await RunAsync()).Passed);
    }

    [Fact]
    public async Task Csharp_check_gets_its_own_database_in_QUIZTIME_PG()
    {
        _repo.Write(TestRepo.CheckPath, """
            var connection = Environment.GetEnvironmentVariable("QUIZTIME_PG") ?? "";
            if (!connection.Contains("check_csharp_001"))
            {
                Console.Error.WriteLine($"FAIL csharp-001: QUIZTIME_PG is '{connection}'");
                return 1;
            }

            Console.WriteLine("PASS csharp-001: got database check_csharp_001");
            return 0;
            """);

        var result = await RunAsync();

        Assert.Equal("PASS csharp-001: got database check_csharp_001", result.PassLine);
    }

    private async Task<CheckResult> RunAsync()
    {
        var jobs = CheckRunner.Jobs(_repo.Root, _repo.Questions());
        return Assert.Single(await CheckRunner.RunAsync(_repo.Root, jobs, postgres.Server, TestContext.Current.CancellationToken));
    }
}
