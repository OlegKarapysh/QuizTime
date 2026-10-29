using Npgsql;

namespace QuizBank.Checks;

/// <summary>
/// The PostgreSQL 16 server that database checks run against: the one in QUIZTIME_PG when it is set,
/// otherwise a throwaway Docker container that is removed on dispose. Each check gets a fresh database.
/// </summary>
public sealed class PostgresServer : IAsyncDisposable
{
    /// <summary>Pinned so every run, local or CI, sees the same server defaults (time zone, collation, messages).</summary>
    public const string Image = "postgres:16.15";

    public const string ConnectionVariable = "QUIZTIME_PG";

    private static readonly TimeSpan DockerTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(60);

    private readonly NpgsqlDataSource _admin;
    private readonly string? _containerId;

    // CREATE DATABASE copies template1, so creations run one at a time.
    private readonly SemaphoreSlim _create = new(1, 1);

    private PostgresServer(string connectionString, NpgsqlDataSource admin, string? containerId)
    {
        ConnectionString = connectionString;
        _admin = admin;
        _containerId = containerId;
    }

    /// <summary>
    /// The server-level connection string, password included. NpgsqlDataSource.ConnectionString drops the password,
    /// so it can't be the base for the per-check connection strings.
    /// </summary>
    public string ConnectionString { get; }

    /// <summary>Connects to QUIZTIME_PG when it is set, otherwise starts a container.</summary>
    public static Task<PostgresServer> StartAsync(TextWriter log, CancellationToken cancellationToken) =>
        Environment.GetEnvironmentVariable(ConnectionVariable) is { Length: > 0 } connectionString
            ? ConnectAsync(connectionString, containerId: null, cancellationToken)
            : StartContainerAsync(log, cancellationToken);

    /// <summary>Creates an empty database (dropping a leftover one first) and returns its connection string.</summary>
    public async Task<string> CreateDatabaseAsync(string name, CancellationToken cancellationToken)
    {
        await _create.WaitAsync(cancellationToken);
        try
        {
            await ExecuteAsync($"""DROP DATABASE IF EXISTS "{name}" WITH (FORCE)""", cancellationToken);
            await ExecuteAsync($"""CREATE DATABASE "{name}" """, cancellationToken);
        }
        finally
        {
            _create.Release();
        }

        // No pooling: the database is dropped right after the check, and pooled connections would outlive it.
        return new NpgsqlConnectionStringBuilder(ConnectionString) { Database = name, Pooling = false }.ConnectionString;
    }

    public Task DropDatabaseAsync(string name, CancellationToken cancellationToken) =>
        ExecuteAsync($"""DROP DATABASE IF EXISTS "{name}" WITH (FORCE)""", cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await _admin.DisposeAsync();
        if (_containerId is not null)
            await ProcessRunner.RunAsync("docker", ["rm", "--force", _containerId], ".", DockerTimeout, CancellationToken.None);
    }

    private async Task ExecuteAsync(string sql, CancellationToken cancellationToken)
    {
        await using var command = _admin.CreateCommand(sql);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<PostgresServer> StartContainerAsync(TextWriter log, CancellationToken cancellationToken)
    {
        log.WriteLine($"Starting {Image} in Docker...");
        const string password = "quiztime";
        var run = await Docker(cancellationToken,
            "run", "--detach", "--rm", "--publish", "127.0.0.1::5432", "--env", $"POSTGRES_PASSWORD={password}",
            "--label", "quiztime=run-checks", Image);
        var containerId = run.Stdout.Trim().Split('\n')[^1];

        try
        {
            // Prints the published address, such as 127.0.0.1:55012.
            var port = (await Docker(cancellationToken, "port", containerId, "5432/tcp")).Stdout.Trim().Split('\n')[0].Split(':')[^1];
            var connectionString = $"Host=127.0.0.1;Port={port};Username=postgres;Password={password};Database=postgres";
            return await ConnectAsync(connectionString, containerId, cancellationToken);
        }
        catch
        {
            await ProcessRunner.RunAsync("docker", ["rm", "--force", containerId], ".", DockerTimeout, CancellationToken.None);
            throw;
        }
    }

    private static async Task<ProcessResult> Docker(CancellationToken cancellationToken, params string[] arguments)
    {
        var result = await ProcessRunner.RunAsync("docker", arguments, ".", DockerTimeout, cancellationToken);
        if (result.ExitCode == 0 && !result.TimedOut) return result;

        var reason = result.TimedOut
            ? "it timed out"
            : result.Stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? $"exit code {result.ExitCode}";
        throw new EnvironmentException(
            $"'docker {arguments[0]}' failed: {reason}. Start Docker, or set {ConnectionVariable} to a PostgreSQL 16 server.");
    }

    /// <summary>Waits for the server to accept connections, then makes sure it is PostgreSQL 16.</summary>
    private static async Task<PostgresServer> ConnectAsync(string connectionString, string? containerId, CancellationToken cancellationToken)
    {
        var admin = NpgsqlDataSource.Create(connectionString);
        try
        {
            var deadline = DateTime.UtcNow + StartupTimeout;
            while (true)
            {
                try
                {
                    await using var command = admin.CreateCommand("SHOW server_version_num");
                    var version = int.Parse((string)(await command.ExecuteScalarAsync(cancellationToken))!);
                    if (version / 10000 != 16)
                        throw new EnvironmentException(
                            $"Checks target PostgreSQL 16, but the server runs {version / 10000}. Point {ConnectionVariable} at " +
                            $"PostgreSQL 16, or unset it to use {Image} in Docker.");
                    return new PostgresServer(connectionString, admin, containerId);
                }
                catch (Exception e) when (e is NpgsqlException or IOException && containerId is not null && DateTime.UtcNow < deadline)
                {
                    // A new container accepts TCP connections only once initdb has finished.
                    await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
                }
                catch (Exception e) when (e is NpgsqlException or IOException)
                {
                    var source = containerId is null ? ConnectionVariable : $"the {Image} container";
                    throw new EnvironmentException($"Could not connect to PostgreSQL from {source}: {e.Message}");
                }
            }
        }
        catch
        {
            await admin.DisposeAsync();
            throw;
        }
    }
}
