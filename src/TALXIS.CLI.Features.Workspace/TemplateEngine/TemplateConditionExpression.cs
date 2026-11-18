namespace TALXIS.CLI.Features.Workspace.TemplateEngine;

/// <summary>
/// Evaluates the boolean expressions templates use in <c>isEnabled</c> / <c>isRequired</c>, such as
/// <c>(IsSystemEntity == false)</c> or <c>(Kind == "Lookup") || !IsGlobal</c>: <c>==</c>, <c>!=</c>,
/// <c>&amp;&amp;</c>, <c>||</c>, <c>!</c> and parentheses. Unquoted <c>true</c>, <c>false</c> and numbers are literals,
/// other names resolve to the supplied values (missing = empty).
/// </summary>
public static class TemplateConditionExpression
{
    /// <summary>
    /// Returns the value of <paramref name="expression"/>, or <see langword="null"/> when it can't be parsed.
    /// </summary>
    public static bool? TryEvaluate(string expression, IReadOnlyDictionary<string, string> variables)
    {
        var tokens = Tokenize(expression);
        if (tokens is null) return null;

        var pos = 0;
        var value = ParseOr(tokens, ref pos, variables);
        return value is not null && pos == tokens.Count ? value : null;
    }

    private enum Kind { LParen, RParen, Or, And, Not, Eq, Neq, Literal, Ident }

    private readonly record struct Token(Kind Kind, string Text);

    private static List<Token>? Tokenize(string s)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < s.Length)
        {
            var c = s[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            switch (c)
            {
                case '(': tokens.Add(new(Kind.LParen, "(")); i++; break;
                case ')': tokens.Add(new(Kind.RParen, ")")); i++; break;
                case '|' when Next(s, i) == '|': tokens.Add(new(Kind.Or, "||")); i += 2; break;
                case '&' when Next(s, i) == '&': tokens.Add(new(Kind.And, "&&")); i += 2; break;
                case '=' when Next(s, i) == '=': tokens.Add(new(Kind.Eq, "==")); i += 2; break;
                case '!' when Next(s, i) == '=': tokens.Add(new(Kind.Neq, "!=")); i += 2; break;
                case '!': tokens.Add(new(Kind.Not, "!")); i++; break;
                case '"':
                case '\'':
                {
                    var end = s.IndexOf(c, i + 1);
                    if (end < 0) return null;
                    tokens.Add(new(Kind.Literal, s[(i + 1)..end]));
                    i = end + 1;
                    break;
                }
                default:
                {
                    var start = i;
                    while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] is '_' or '.' or '-')) i++;
                    if (i == start) return null;

                    var word = s[start..i];
                    var isLiteral = bool.TryParse(word, out _) || double.TryParse(word, System.Globalization.CultureInfo.InvariantCulture, out _);
                    tokens.Add(new(isLiteral ? Kind.Literal : Kind.Ident, word));
                    break;
                }
            }
        }
        return tokens;
    }

    private static char Next(string s, int i) => i + 1 < s.Length ? s[i + 1] : '\0';

    private static bool? ParseOr(List<Token> t, ref int pos, IReadOnlyDictionary<string, string> vars)
    {
        var left = ParseAnd(t, ref pos, vars);
        while (left is not null && Peek(t, pos)?.Kind == Kind.Or)
        {
            pos++;
            var right = ParseAnd(t, ref pos, vars);
            left = right is null ? null : left.Value || right.Value;
        }
        return left;
    }

    private static bool? ParseAnd(List<Token> t, ref int pos, IReadOnlyDictionary<string, string> vars)
    {
        var left = ParseUnary(t, ref pos, vars);
        while (left is not null && Peek(t, pos)?.Kind == Kind.And)
        {
            pos++;
            var right = ParseUnary(t, ref pos, vars);
            left = right is null ? null : left.Value && right.Value;
        }
        return left;
    }

    private static bool? ParseUnary(List<Token> t, ref int pos, IReadOnlyDictionary<string, string> vars)
    {
        if (Peek(t, pos)?.Kind != Kind.Not) return ParsePrimary(t, ref pos, vars);

        pos++;
        var operand = ParseUnary(t, ref pos, vars);
        return operand is null ? null : !operand.Value;
    }

    private static bool? ParsePrimary(List<Token> t, ref int pos, IReadOnlyDictionary<string, string> vars)
    {
        var tok = Peek(t, pos);
        if (tok is null) return null;

        if (tok.Value.Kind == Kind.LParen)
        {
            pos++;
            var inner = ParseOr(t, ref pos, vars);
            if (Peek(t, pos)?.Kind != Kind.RParen) return null;
            pos++;
            return inner;
        }

        var left = ParseOperand(t, ref pos, vars);
        if (left is null) return null;

        var op = Peek(t, pos);
        if (op?.Kind is not (Kind.Eq or Kind.Neq)) return IsTrue(left);

        pos++;
        var right = ParseOperand(t, ref pos, vars);
        if (right is null) return null;

        var equal = ValuesEqual(left, right);
        return op.Value.Kind == Kind.Eq ? equal : !equal;
    }

    private static string? ParseOperand(List<Token> t, ref int pos, IReadOnlyDictionary<string, string> vars)
    {
        var tok = Peek(t, pos);
        if (tok?.Kind is not (Kind.Literal or Kind.Ident)) return null;

        pos++;
        if (tok.Value.Kind == Kind.Literal) return tok.Value.Text;
        return vars.TryGetValue(tok.Value.Text, out var v) ? v : "";
    }

    // Bool parameters arrive as "True"/"true"/"false", so booleans compare by value, other strings exactly.
    private static bool ValuesEqual(string left, string right)
    {
        if (bool.TryParse(left, out var l) && bool.TryParse(right, out var r)) return l == r;
        return string.Equals(left, right, StringComparison.Ordinal);
    }

    private static bool IsTrue(string value) => bool.TryParse(value, out var b) && b;

    private static Token? Peek(List<Token> t, int pos) => pos < t.Count ? t[pos] : null;
}
