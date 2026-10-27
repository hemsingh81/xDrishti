using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace XDrishti.Domain.Assistant;

/// <summary>
/// The lexical pre-check of <c>run_sql</c> (spec P9-02 AC 8, P9-00 D10): one statement that starts with <c>select</c> or <c>with</c>, at most 4,000 characters, no comment, ASCII outside quoted text, balanced parentheses and none of the refused keywords. It exists for readable error messages,
/// NOT for safety: the boundary is the database role (SELECT on the <c>aiv</c> views only), the read-only transaction and the cursor. String literals and quoted identifiers are skipped; dollar quoting is refused (a tag inside a name would hide a semicolon), so a keyword inside a literal or the identifier <c>settings_id</c> is no match.
/// </summary>
public static partial class SqlPreCheck
{
    private static readonly HashSet<string> Refused = new(StringComparer.Ordinal)
    {
        "into", "copy", "set", "reset", "call", "do", "insert", "update", "delete", "drop", "alter", "create", "truncate", "grant", "revoke", "vacuum", "analyze", "execute", "prepare",
        "declare", "fetch", "move", "close", "discard", "listen", "notify", "lock", "commit", "rollback", "begin", "savepoint", "load", "refresh", "reindex", "cluster",
    };

    /// <summary>Function families no question about the stored data needs: locks, settings, other databases, large objects, server files. (<c>pg_sleep</c> stays: the time limit is tested with it.)</summary>
    private static readonly string[] RefusedPrefixes = ["pg_advisory", "pg_try_advisory", "set_config", "dblink", "lo_", "pg_read_", "pg_ls_", "pg_stat_file", "pg_terminate", "pg_cancel", "pg_reload", "pg_rotate", "pg_switch", "pg_create_", "pg_drop_", "pg_logical", "pg_replication"];

    public static SqlCheck Check(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return Refuse("empty");
        }

        if (sql.Length > ToolLimits.MaxSqlChars)
        {
            return Refuse("too_long");
        }

        var lexed = Lex(sql);
        if (lexed.Failure is not null)
        {
            return Refuse(lexed.Failure);
        }

        var code = lexed.Code.TrimEnd();
        if (code.EndsWith(';'))
        {
            code = code[..^1].TrimEnd();
        }

        if (code.Contains(';', StringComparison.Ordinal))
        {
            return Refuse("multiple_statements");
        }

        var depth = 0;
        foreach (var c in code)
        {
            depth += c == '(' ? 1 : c == ')' ? -1 : 0;
            if (depth < 0)
            {
                return Refuse("unbalanced");
            }
        }

        if (depth != 0)
        {
            return Refuse("unbalanced");
        }

        var words = Word().Matches(code).Select(m => m.Value.ToLowerInvariant()).ToList();
        if (words.Count == 0 || words[0] is not ("select" or "with") || !code.TrimStart().StartsWith(words[0], StringComparison.OrdinalIgnoreCase))
        {
            return Refuse("not_select");
        }

        var risky = words.FirstOrDefault(w => RefusedPrefixes.Any(p => w.StartsWith(p, StringComparison.Ordinal)));
        if (risky is not null)
        {
            return Refuse(RefusedPrefixes.First(p => risky.StartsWith(p, StringComparison.Ordinal)).TrimEnd('_'));
        }

        var refused = words.FirstOrDefault(Refused.Contains);
        if (refused is not null)
        {
            return Refuse(refused);
        }

        return new SqlCheck(true, string.Empty, ShapeStatement(sql));
    }

    /// <summary>The SHA-256 (lower-case hex) of the whitespace-normalised query text: runs of white space become one space, the ends are trimmed.</summary>
    public static string Hash(string sql)
    {
        ArgumentNullException.ThrowIfNull(sql);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Whitespace().Replace(sql, " ").Trim())));
    }

    private static string ShapeStatement(string sql)
    {
        var trimmed = sql.TrimEnd();
        return trimmed.EndsWith(';') ? trimmed[..^1].TrimEnd() : trimmed;
    }

    private static SqlCheck Refuse(string reason) => new(false, reason, string.Empty);

    private sealed record Lexed(string Code, string? Failure);

    /// <summary>The query with the bodies of literals and quoted identifiers blanked, or the reason it cannot be read as plain SQL.</summary>
    private static Lexed Lex(string sql)
    {
        var code = new StringBuilder(sql.Length);
        var i = 0;
        while (i < sql.Length)
        {
            var c = sql[i];
            switch (c)
            {
                case '\'':
                    var escapes = i > 0 && (sql[i - 1] is 'E' or 'e') && (i < 2 || !IsWordChar(sql[i - 2]));
                    i = SkipQuoted(sql, i, '\'', escapes);
                    if (i < 0)
                    {
                        return new Lexed(string.Empty, "unterminated");
                    }

                    code.Append(' ');
                    break;
                case '"':
                    i = SkipQuoted(sql, i, '"', false);
                    if (i < 0)
                    {
                        return new Lexed(string.Empty, "unterminated");
                    }

                    code.Append(" x ");
                    break;
                case '$':
                    // No dollar quoting and no dollar inside a name: a tag such as a$x$ would hide semicolons from this scan. Only a positional parameter ($1) is left.
                    if (i == 0 || IsWordChar(sql[i - 1]) || i + 1 >= sql.Length || !char.IsAsciiDigit(sql[i + 1]))
                    {
                        return new Lexed(string.Empty, "dollar");
                    }

                    code.Append(c);
                    i++;
                    break;
                case '-' when i + 1 < sql.Length && sql[i + 1] == '-':
                case '/' when i + 1 < sql.Length && sql[i + 1] == '*':
                    return new Lexed(string.Empty, "comment");
                default:
                    if (c > 126 || (c < 32 && c is not ('\n' or '\r' or '\t')) || c == 127)
                    {
                        return new Lexed(string.Empty, "non_ascii");
                    }

                    code.Append(c);
                    i++;
                    break;
            }
        }

        return new Lexed(code.ToString(), null);
    }

    private static bool IsWordChar(char c) => char.IsAsciiLetterOrDigit(c) || c == '_';

    /// <summary>The index after the closing quote of the quoted text that opens at <paramref name="start"/>, or -1 when it is not closed. A doubled quote is one quote; a backslash escapes in an E'' string.</summary>
    private static int SkipQuoted(string sql, int start, char quote, bool backslashEscapes)
    {
        var i = start + 1;
        while (i < sql.Length)
        {
            if (backslashEscapes && sql[i] == '\\')
            {
                i += 2;
                continue;
            }

            if (sql[i] == quote)
            {
                if (i + 1 < sql.Length && sql[i + 1] == quote)
                {
                    i += 2;
                    continue;
                }

                return i + 1;
            }

            i++;
        }

        return -1;
    }

    [GeneratedRegex(@"[A-Za-z_][A-Za-z0-9_$]*", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Word();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Whitespace();
}
