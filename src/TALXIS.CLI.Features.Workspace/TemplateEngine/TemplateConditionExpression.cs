namespace TALXIS.CLI.Features.Workspace.TemplateEngine;

/// <summary>
/// Minimal evaluator for the boolean expressions templates use in <c>isEnabled</c> /
/// <c>isRequired</c> conditions — string equality/inequality combined with <c>&amp;&amp;</c>,
/// <c>||</c> and parentheses, e.g. <c>(AttributeType == "Lookup") || (AttributeType == "Customer")</c>.
/// Identifiers resolve to supplied values (missing = empty string); a bare identifier is
/// truthy when its value is <c>"true"</c>.
/// </summary>
public static class TemplateConditionExpression
{
    /// <summary>Evaluates <paramref name="expression"/> against <paramref name="variables"/>.</summary>
    public static bool Evaluate(string expression, IReadOnlyDictionary<string, string> variables)
    {
        var tokens = Tokenize(expression);
        var pos = 0;
        var value = ParseOr(tokens, ref pos, variables);
        return value;
    }

    private enum Kind { LParen, RParen, Or, And, Eq, Neq, Str, Ident }
    private readonly record struct Token(Kind Kind, string Text);

    private static List<Token> Tokenize(string s)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < s.Length)
        {
            var c = s[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            switch (c)
            {
                case '(': tokens.Add(new(Kind.LParen, "(")); i++; break;
                case ')': tokens.Add(new(Kind.RParen, ")")); i++; break;
                case '|' when Next(s, i) == '|': tokens.Add(new(Kind.Or, "||")); i += 2; break;
                case '&' when Next(s, i) == '&': tokens.Add(new(Kind.And, "&&")); i += 2; break;
                case '=' when Next(s, i) == '=': tokens.Add(new(Kind.Eq, "==")); i += 2; break;
                case '!' when Next(s, i) == '=': tokens.Add(new(Kind.Neq, "!=")); i += 2; break;
                case '"':
                case '\'':
                    {
                        var quote = c;
                        var start = ++i;
                        while (i < s.Length && s[i] != quote) i++;
                        tokens.Add(new(Kind.Str, s[start..Math.Min(i, s.Length)]));
                        i++; // closing quote
                        break;
                    }
                default:
                    {
                        var start = i;
                        while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] is '_' or '.' or '(' or ')'))
                        {
                            // Identifiers like OptionSet(Global) contain parens; only absorb them
                            // when they're clearly part of a value token, not grouping.
                            if (s[i] is '(' or ')') break;
                            i++;
                        }
                        if (i == start) { i++; break; } // skip unknown char
                        tokens.Add(new(Kind.Ident, s[start..i]));
                        break;
                    }
            }
        }
        return tokens;
    }

    private static char Next(string s, int i) => i + 1 < s.Length ? s[i + 1] : '\0';

    private static bool ParseOr(List<Token> t, ref int pos, IReadOnlyDictionary<string, string> vars)
    {
        var left = ParseAnd(t, ref pos, vars);
        while (Peek(t, pos)?.Kind == Kind.Or)
        {
            pos++;
            var right = ParseAnd(t, ref pos, vars);
            left = left || right;
        }
        return left;
    }

    private static bool ParseAnd(List<Token> t, ref int pos, IReadOnlyDictionary<string, string> vars)
    {
        var left = ParsePrimary(t, ref pos, vars);
        while (Peek(t, pos)?.Kind == Kind.And)
        {
            pos++;
            var right = ParsePrimary(t, ref pos, vars);
            left = left && right;
        }
        return left;
    }

    private static bool ParsePrimary(List<Token> t, ref int pos, IReadOnlyDictionary<string, string> vars)
    {
        var tok = Peek(t, pos);
        if (tok is null) return false;

        if (tok.Value.Kind == Kind.LParen)
        {
            pos++; // (
            var inner = ParseOr(t, ref pos, vars);
            if (Peek(t, pos)?.Kind == Kind.RParen) pos++; // )
            return inner;
        }

        // Comparison: operand (== | !=) operand, or a bare operand used as a boolean.
        var left = ParseOperand(t, ref pos, vars);
        var op = Peek(t, pos);
        if (op?.Kind == Kind.Eq || op?.Kind == Kind.Neq)
        {
            pos++;
            var right = ParseOperand(t, ref pos, vars);
            var equal = string.Equals(left, right, StringComparison.Ordinal);
            return op.Value.Kind == Kind.Eq ? equal : !equal;
        }
        return string.Equals(left, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static string ParseOperand(List<Token> t, ref int pos, IReadOnlyDictionary<string, string> vars)
    {
        var tok = Peek(t, pos);
        if (tok is null) return "";
        pos++;
        return tok.Value.Kind switch
        {
            Kind.Str => tok.Value.Text,
            Kind.Ident => vars.TryGetValue(tok.Value.Text, out var v) ? v : "",
            _ => "",
        };
    }

    private static Token? Peek(List<Token> t, int pos) => pos < t.Count ? t[pos] : null;
}
