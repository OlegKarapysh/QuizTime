using Npgsql;
using QuizBank.Validation;

namespace QuizBank.Checks;

/// <summary>The output of a SQL script: one line per row, and the line of the statement that failed, if one did.</summary>
public sealed record SqlOutput(IReadOnlyList<string> Lines, int? FailedStatementLine);

/// <summary>
/// Runs a check.sql in its own database, one statement at a time like psql, and compares the output with the
/// file's expected block. Values come back in PostgreSQL's text format, as psql shows them.
/// </summary>
public static class SqlCheck
{
    private static readonly TimeSpan StatementTimeout = TimeSpan.FromSeconds(60);

    public static async Task<CheckResult> RunAsync(CheckJob job, string root, string connectionString, CancellationToken cancellationToken)
    {
        SqlScript script;
        try
        {
            script = SqlScript.Parse(await File.ReadAllTextAsync(Path.Combine(root, job.Path), cancellationToken));
        }
        catch (SqlScriptException e)
        {
            return CheckResult.Fail(job, new Diagnostic(job.Path, e.Position.Line, e.Position.Column, e.Message));
        }

        SqlOutput output;
        try
        {
            output = await ExecuteAsync(script.Statements, connectionString, cancellationToken);
        }
        catch (NpgsqlException e)
        {
            // Statement errors are part of the output; this is the connection itself failing.
            return CheckResult.Fail(job, new Diagnostic(job.Path, null, null, $"The PostgreSQL connection failed: {e.Message}"));
        }

        return Compare(job, script, output);
    }

    /// <summary>
    /// Each result set prints a header line of column names and one line per row, values separated by |, NULL as NULL.
    /// A failing statement prints only "ERROR &lt;SQLSTATE&gt;: &lt;message&gt;", not the rows it returned first, and stops
    /// the script, as psql does. Blank lines separate the blocks. Statements without a result set (DDL, plain INSERT)
    /// print nothing.
    /// </summary>
    public static async Task<SqlOutput> ExecuteAsync(IReadOnlyList<SqlStatement> statements, string connectionString, CancellationToken cancellationToken)
    {
        var lines = new List<string>();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        foreach (var statement in statements)
        {
            var printed = new List<string>();
            try
            {
                await using var command = new NpgsqlCommand(statement.Text, connection)
                {
                    AllResultTypesAreUnknown = true,
                    CommandTimeout = (int)StatementTimeout.TotalSeconds,
                };
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                do
                {
                    if (reader.FieldCount == 0) continue;
                    if (lines.Count + printed.Count > 0) printed.Add("");
                    printed.Add(string.Join("|", Enumerable.Range(0, reader.FieldCount).Select(reader.GetName)));
                    while (await reader.ReadAsync(cancellationToken))
                        printed.Add(string.Join("|", Enumerable.Range(0, reader.FieldCount).Select(i => Value(reader, i))));
                }
                while (await reader.NextResultAsync(cancellationToken));
            }
            catch (PostgresException e)
            {
                if (lines.Count > 0) lines.Add("");
                lines.Add($"ERROR {e.SqlState}: {e.MessageText}");
                return new SqlOutput(lines, statement.Line);
            }

            lines.AddRange(printed);
        }

        return new SqlOutput(lines, null);
    }

    private static string Value(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? "NULL" : reader.GetString(ordinal).Replace("\n", "\\n");

    private static CheckResult Compare(CheckJob job, SqlScript script, SqlOutput output)
    {
        // Trailing spaces can't be kept in the file (.editorconfig trims them), so they don't count on either side.
        var expected = TrimTrailingBlankLines(script.Expected.Select(e => e.Text.TrimEnd()).ToList());
        var actual = TrimTrailingBlankLines(output.Lines.Select(l => l.TrimEnd()).ToList());

        for (var i = 0; i < Math.Max(expected.Count, actual.Count); i++)
        {
            if (i < expected.Count && i < actual.Count && expected[i] == actual[i]) continue;

            var line = i < expected.Count ? script.Expected[i].Line : script.ExpectedEndLine;
            var message = (i < expected.Count, i < actual.Count) switch
            {
                (true, true) => $"the expected block says '{expected[i]}' here, but the output has '{actual[i]}'",
                (true, false) => $"the expected block says '{expected[i]}' here, but the output ended",
                _ => $"the output continues with '{actual[i]}' after the expected block ends",
            };
            if (output.FailedStatementLine is { } failed) message += $" (the statement at line {failed} failed)";
            return CheckResult.Fail(job, actual,
                new Diagnostic(job.Path, line, 1, $"FAIL {job.Id}: {message}. The full output follows."));
        }

        var shown = actual.Count == 0 ? "no output" : $"'{string.Join(" / ", actual.Where(l => l.Length > 0))}'";
        return CheckResult.Pass(job, $"PASS {job.Id}: printed {shown}, as the expected block says");
    }

    private static List<string> TrimTrailingBlankLines(List<string> lines)
    {
        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        return lines;
    }
}
