using QuizBank.Checks;

namespace QuizBank.Tests;

/// <summary>
/// One PostgreSQL 16 server for the tests that need a database, started the way run-checks starts it:
/// QUIZTIME_PG if set, otherwise a Docker container. Without Docker those tests skip with the reason,
/// except when QUIZTIME_REQUIRE_DOCKER=1 (CI), where they fail so a broken harness can't pass as skipped.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private PostgresServer? _server;
    private string? _unavailable;

    public PostgresServer Server
    {
        get
        {
            if (_server is not null) return _server;
            if (Environment.GetEnvironmentVariable("QUIZTIME_REQUIRE_DOCKER") == "1")
                Assert.Fail($"QUIZTIME_REQUIRE_DOCKER=1, but PostgreSQL could not start: {_unavailable}");
            Assert.Skip($"Needs PostgreSQL 16 from Docker: {_unavailable}");
            return null!;
        }
    }

    public async ValueTask InitializeAsync()
    {
        try
        {
            _server = await PostgresServer.StartAsync(TextWriter.Null, TestContext.Current.CancellationToken);
        }
        catch (EnvironmentException e)
        {
            _unavailable = e.Message;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_server is not null) await _server.DisposeAsync();
    }
}
