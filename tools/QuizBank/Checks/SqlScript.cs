using QuizBank.Yaml;

namespace QuizBank.Checks;

/// <summary>One SQL statement and the 1-based line it starts on.</summary>
public sealed record SqlStatement(string Text, int Line);

/// <summary>One line of the expected block: its text without the leading "--", and its line in the file.</summary>
public sealed record ExpectedLine(string Text, int Line);

/// <summary>A check.sql that could not be read: the message and where it points.</summary>
public sealed class SqlScriptException(string message, SourcePosition position) : Exception(message)
{
    public SourcePosition Position { get; } = position;
}

/// <summary>
/// A check.sql: statements, then an expected block of comment lines between "-- --- expected ---" and
/// "-- --- end expected ---" that holds the output the statements must produce.
/// </summary>
public sealed record SqlScript(IReadOnlyList<SqlStatement> Statements, IReadOnlyList<ExpectedLine> Expected, int ExpectedEndLine)
{
    public const string ExpectedStart = "-- --- expected ---";
    public const string ExpectedEnd = "-- --- end expected ---";

    public static SqlScript Parse(string text)
    {
        var lines = text.Split('\n');
        var start = Array.FindIndex(lines, line => line.TrimEnd() == ExpectedStart);
        if (start < 0)
            throw new SqlScriptException(
                $"No '{ExpectedStart}' line; end the file with the expected output (see docs/CODE_STYLE.md).", new SourcePosition(1, 1));

        var end = Array.FindIndex(lines, start + 1, line => line.TrimEnd() == ExpectedEnd);
        if (end < 0)
            throw new SqlScriptException($"The expected block has no '{ExpectedEnd}' line.", new SourcePosition(start + 1, 1));

        var expected = new List<ExpectedLine>();
        for (var i = start + 1; i < end; i++)
        {
            if (!lines[i].StartsWith("--", StringComparison.Ordinal))
                throw new SqlScriptException("Every line of the expected block must start with '--'.", new SourcePosition(i + 1, 1));
            var content = lines[i][2..];
            expected.Add(new ExpectedLine(content.StartsWith(' ') ? content[1..] : content, i + 1));
        }

        if (lines.Skip(end + 1).Any(line => line.Trim().Length > 0 && !line.TrimStart().StartsWith("--", StringComparison.Ordinal)))
            throw new SqlScriptException("Nothing but comments may follow the expected block.", new SourcePosition(end + 2, 1));

        // The expected block is comments, so splitting the whole file finds only the statements.
        return new SqlScript(Split(text), expected, end + 1);
    }

    /// <summary>
    /// Splits a script into statements at semicolons, the way psql does: not inside quotes, dollar quotes,
    /// comments or parentheses. SQL-standard function bodies (BEGIN ATOMIC) are not supported.
    /// </summary>
    public static IReadOnlyList<SqlStatement> Split(string text)
    {
        var statements = new List<SqlStatement>();
        int? start = null;
        var depth = 0;
        var i = 0;

        while (i < text.Length)
        {
            var c = text[i];
            var next = i + 1 < text.Length ? text[i + 1] : '\0';

            if (c == '-' && next == '-')
            {
                i = text.IndexOf('\n', i) is var eol and >= 0 ? eol : text.Length;
                continue;
            }

            if (c == '/' && next == '*')
            {
                i = SkipBlockComment(text, i);
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            start ??= i;
            switch (c)
            {
                case '\'':
                    i = SkipQuoted(text, i, '\'', backslashEscapes: IsEscapeStringPrefix(text, i));
                    continue;
                case '"':
                    i = SkipQuoted(text, i, '"', backslashEscapes: false);
                    continue;
                case '$' when DollarTag(text, i) is { } tag:
                    var close = text.IndexOf(tag, i + tag.Length, StringComparison.Ordinal);
                    if (close < 0) throw Error(text, i, "Dollar-quoted string is never closed.");
                    i = close + tag.Length;
                    continue;
                case '(':
                    depth++;
                    break;
                case ')':
                    depth = Math.Max(depth - 1, 0);
                    break;
                case ';' when depth == 0:
                    // A lone semicolon is an empty statement, which psql skips too.
                    if (i > start) statements.Add(new SqlStatement(text[start.Value..(i + 1)], LineOf(text, start.Value)));
                    start = null;
                    break;
            }

            i++;
        }

        if (start is not null) statements.Add(new SqlStatement(text[start.Value..].TrimEnd(), LineOf(text, start.Value)));
        return statements;
    }

    private static int SkipBlockComment(string text, int i)
    {
        // Block comments nest in PostgreSQL.
        var opened = i;
        var nesting = 0;
        while (i < text.Length - 1)
        {
            if (text[i] == '/' && text[i + 1] == '*')
            {
                nesting++;
                i += 2;
            }
            else if (text[i] == '*' && text[i + 1] == '/')
            {
                nesting--;
                i += 2;
                if (nesting == 0) return i;
            }
            else
            {
                i++;
            }
        }

        throw Error(text, opened, "Block comment is never closed.");
    }

    private static int SkipQuoted(string text, int i, char quote, bool backslashEscapes)
    {
        var opened = i;
        for (i++; i < text.Length; i++)
        {
            if (backslashEscapes && text[i] == '\\')
            {
                i++;
            }
            else if (text[i] == quote)
            {
                // A doubled quote is an escaped quote.
                if (i + 1 < text.Length && text[i + 1] == quote) i++;
                else return i + 1;
            }
        }

        throw Error(text, opened, quote == '\'' ? "String is never closed." : "Quoted identifier is never closed.");
    }

    // E'...' strings take backslash escapes; a plain word ending in e (like "type'") is not a prefix.
    private static bool IsEscapeStringPrefix(string text, int quote) =>
        quote > 0 && text[quote - 1] is 'E' or 'e' && (quote < 2 || !IsIdentifierChar(text[quote - 2]));

    /// <summary>The tag ($$ or $name$) of a dollar quote starting at <paramref name="i"/>, or null for $1 parameters and identifiers.</summary>
    private static string? DollarTag(string text, int i)
    {
        if (i > 0 && IsIdentifierChar(text[i - 1])) return null;

        var end = i + 1;
        while (end < text.Length && (char.IsLetter(text[end]) || text[end] == '_' || (end > i + 1 && char.IsDigit(text[end]))))
            end++;
        return end < text.Length && text[end] == '$' ? text[i..(end + 1)] : null;
    }

    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c is '_' or '$';

    private static int LineOf(string text, int offset) => 1 + text.AsSpan(0, offset).Count('\n');

    private static SqlScriptException Error(string text, int offset, string message) =>
        new(message, new SourcePosition(LineOf(text, offset), offset - (text.LastIndexOf('\n', Math.Max(offset - 1, 0)) + 1) + 1));
}
