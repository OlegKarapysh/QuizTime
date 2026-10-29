using Npgsql;
using QuizBank.Model;
using QuizBank.Validation;

namespace QuizBank.Checks;

/// <summary>Gate 2: runs each question's check and collects the results in id order.</summary>
public static class CheckRunner
{
    private const int Parallelism = 4;

    /// <summary>
    /// The checks of the given questions: method run or sql, not retired. A C# check needs a database
    /// when it reads QUIZTIME_PG; a SQL check always does.
    /// </summary>
    public static IReadOnlyList<CheckJob> Jobs(string root, IEnumerable<BankFile<Question>> questions) =>
        questions
            .Select(file => file.Value)
            .Where(q => q.Status != QuestionStatus.Retired && q.Verification is { Method: not VerificationMethod.Docs, Check: not null })
            .Select(q => q.Verification!.Method == VerificationMethod.Sql
                ? new CheckJob(q.Id, CheckKind.Sql, q.Verification.Check!, NeedsDatabase: true)
                : new CheckJob(q.Id, CheckKind.Run, q.Verification.Check!,
                    File.ReadAllText(Path.Combine(root, q.Verification.Check!)).Contains(PostgresServer.ConnectionVariable, StringComparison.Ordinal)))
            .OrderBy(job => job.Id, StringComparer.Ordinal)
            .ToList();

    /// <param name="server">Required when any job needs a database. The caller owns it.</param>
    public static async Task<IReadOnlyList<CheckResult>> RunAsync(
        string root, IReadOnlyList<CheckJob> jobs, PostgresServer? server, CancellationToken cancellationToken)
    {
        var results = new CheckResult[jobs.Count];
        var options = new ParallelOptions { MaxDegreeOfParallelism = Parallelism, CancellationToken = cancellationToken };
        await Parallel.ForEachAsync(Enumerable.Range(0, jobs.Count), options,
            async (i, token) => results[i] = await RunOneAsync(root, jobs[i], server, token));
        return results;
    }

    private static async Task<CheckResult> RunOneAsync(string root, CheckJob job, PostgresServer? server, CancellationToken cancellationToken)
    {
        if (!job.NeedsDatabase) return await DotnetCheck.RunAsync(job, root, connectionString: null, cancellationToken);
        if (server is null) throw new InvalidOperationException($"Check {job.Id} needs a database, but no server was started.");

        var connectionString = await server.CreateDatabaseAsync(job.DatabaseName, cancellationToken);
        try
        {
            return job.Kind == CheckKind.Sql
                ? await SqlCheck.RunAsync(job, root, connectionString, cancellationToken)
                : await DotnetCheck.RunAsync(job, root, connectionString, cancellationToken);
        }
        catch (NpgsqlException e) when (e is not PostgresException)
        {
            return CheckResult.Fail(job, new Diagnostic(job.Path, null, null, $"Lost the PostgreSQL connection: {e.Message}"));
        }
        finally
        {
            await server.DropDatabaseAsync(job.DatabaseName, CancellationToken.None);
        }
    }
}
