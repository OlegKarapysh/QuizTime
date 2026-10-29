using QuizBank.Checks;
using QuizBank.Yaml;

namespace QuizBank.Tests;

public sealed class SqlScriptTests
{
    // Splitting

    [Fact]
    public void Statements_split_at_semicolons_with_their_lines()
    {
        var statements = SqlScript.Split("CREATE TABLE t (id int);\n\nINSERT INTO t VALUES (1);\nSELECT * FROM t");

        Assert.Equal(
            [new SqlStatement("CREATE TABLE t (id int);", 1), new SqlStatement("INSERT INTO t VALUES (1);", 3), new SqlStatement("SELECT * FROM t", 4)],
            statements);
    }

    [Theory]
    [InlineData("SELECT 'a;b';")]
    [InlineData("SELECT 'it''s; fine';")]
    [InlineData("SELECT E'a\\';b';")]
    [InlineData("SELECT \"odd;name\" FROM t;")]
    [InlineData("SELECT 1 -- not; here\n + 1;")]
    [InlineData("SELECT /* a; /* nested; */ b; */ 1;")]
    [InlineData("DO $$ BEGIN RAISE NOTICE 'x'; END $$;")]
    [InlineData("CREATE FUNCTION f() RETURNS int AS $body$ SELECT 1; $body$ LANGUAGE sql;")]
    [InlineData("CREATE RULE r AS ON INSERT TO t DO ALSO (NOTIFY a; NOTIFY b);")]
    public void Semicolons_inside_quotes_comments_and_parentheses_do_not_split(string sql)
    {
        var statement = Assert.Single(SqlScript.Split(sql));

        Assert.Equal(sql, statement.Text);
    }

    [Fact]
    public void Positional_parameters_are_not_dollar_quotes()
    {
        var statements = SqlScript.Split("PREPARE p AS SELECT $1; EXECUTE p(1);");

        Assert.Equal(2, statements.Count);
    }

    [Fact]
    public void Comments_before_a_statement_are_not_part_of_it()
    {
        var statement = Assert.Single(SqlScript.Split("-- setup\n/* more */\nSELECT 1;"));

        Assert.Equal(new SqlStatement("SELECT 1;", 3), statement);
    }

    [Fact]
    public void Empty_statements_are_skipped()
    {
        Assert.Single(SqlScript.Split(";\nSELECT 1;;\n"));
    }

    [Theory]
    [InlineData("SELECT 'open;", "String is never closed.")]
    [InlineData("SELECT \"open;", "Quoted identifier is never closed.")]
    [InlineData("DO $$ BEGIN;", "Dollar-quoted string is never closed.")]
    [InlineData("SELECT /* open;", "Block comment is never closed.")]
    public void Unclosed_quotes_and_comments_are_reported_where_they_open(string sql, string message)
    {
        var error = Assert.Throws<SqlScriptException>(() => SqlScript.Split("SELECT 1;\n" + sql));

        Assert.Equal(message, error.Message);
        Assert.Equal(2, error.Position.Line);
    }

    // Expected block

    [Fact]
    public void Expected_block_lines_keep_their_file_lines()
    {
        var script = SqlScript.Parse("""
            SELECT 1 AS one;

            -- --- expected ---
            -- one
            -- 1
            --
            -- --- end expected ---
            """);

        Assert.Equal([new ExpectedLine("one", 4), new ExpectedLine("1", 5), new ExpectedLine("", 6)], script.Expected);
        Assert.Equal(7, script.ExpectedEndLine);
        Assert.Equal([new SqlStatement("SELECT 1 AS one;", 1)], script.Statements);
    }

    [Fact]
    public void Missing_expected_block_is_reported()
    {
        var error = Assert.Throws<SqlScriptException>(() => SqlScript.Parse("SELECT 1;\n"));

        Assert.Equal("No '-- --- expected ---' line; end the file with the expected output (see docs/CODE_STYLE.md).", error.Message);
    }

    [Fact]
    public void Unclosed_expected_block_is_reported_at_its_start()
    {
        var error = Assert.Throws<SqlScriptException>(() => SqlScript.Parse("SELECT 1;\n-- --- expected ---\n-- 1\n"));

        Assert.Equal("The expected block has no '-- --- end expected ---' line.", error.Message);
        Assert.Equal(new SourcePosition(2, 1), error.Position);
    }

    [Fact]
    public void Expected_block_lines_must_be_comments()
    {
        var error = Assert.Throws<SqlScriptException>(() =>
            SqlScript.Parse("SELECT 1;\n-- --- expected ---\n1\n-- --- end expected ---\n"));

        Assert.Equal("Every line of the expected block must start with '--'.", error.Message);
        Assert.Equal(new SourcePosition(3, 1), error.Position);
    }

    [Fact]
    public void Statements_after_the_expected_block_are_rejected()
    {
        var error = Assert.Throws<SqlScriptException>(() =>
            SqlScript.Parse("SELECT 1;\n-- --- expected ---\n-- --- end expected ---\nSELECT 2;\n"));

        Assert.Equal("Nothing but comments may follow the expected block.", error.Message);
    }
}
