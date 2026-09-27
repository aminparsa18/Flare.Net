namespace Flare.Api.Query;

/// <summary>A parsed <see cref="Model.TraceStructureFilter.Expression"/> node.</summary>
public abstract record TraceStructureNode;

/// <summary>A bare condition letter: the trace has a span matching it.</summary>
public sealed record TraceStructureHas(char Condition) : TraceStructureNode;

/// <summary>
/// <c>Parent -> Child</c> (<see cref="Direct"/>) or <c>Parent => Child</c>: some span
/// matching <see cref="Child"/> is a direct child (or, for <c>=></c>, any descendant) of
/// some span matching <see cref="Parent"/>. Never the same span, even when both conditions
/// match it.
/// </summary>
public sealed record TraceStructureRelation(char Parent, char Child, bool Direct) : TraceStructureNode;

public sealed record TraceStructureNot(TraceStructureNode Operand) : TraceStructureNode;

public sealed record TraceStructureAnd(TraceStructureNode Left, TraceStructureNode Right) : TraceStructureNode;

public sealed record TraceStructureOr(TraceStructureNode Left, TraceStructureNode Right) : TraceStructureNode;

/// <summary>
/// Recursive-descent parser for structural trace expressions. Pure, no ClickHouse
/// dependency. Grammar, loosest to tightest:
/// <code>
/// or       := and (("OR" | "||") and)*
/// and      := not (("AND" | "&amp;&amp;") not)*
/// not      := ("NOT" | "!") not | relation
/// relation := LETTER ("->" | "=>") LETTER | LETTER | "(" or ")"
/// </code>
/// Keywords are case-insensitive, letters too (normalized to upper case). A relation's
/// operands are single letters only - <c>A -> B -> C</c> is rejected rather than guessed
/// at, since "B is A's child and C is that same B's child" can't be expressed as two
/// independent pairwise tests.
/// </summary>
public static class TraceStructureExpression
{
    public const int MaxLength = 500;

    /// <summary>Throws <see cref="ArgumentException"/> with a user-facing message on a syntax error.</summary>
    public static TraceStructureNode Parse(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            throw new ArgumentException("A trace structure needs an expression, e.g. \"A -> B\".");
        }

        if (expression.Length > MaxLength)
        {
            throw new ArgumentException($"The trace structure expression is longer than {MaxLength} characters.");
        }

        var parser = new Parser(Tokenize(expression));
        var node = parser.ParseOr();
        if (!parser.AtEnd)
        {
            throw Error($"Unexpected '{parser.Peek.Text}'.");
        }

        return node;
    }

    /// <summary>Every condition letter <paramref name="node"/> mentions.</summary>
    public static IReadOnlySet<char> Letters(TraceStructureNode node)
    {
        var letters = new SortedSet<char>();
        Collect(node, letters);
        return letters;
    }

    private static void Collect(TraceStructureNode node, SortedSet<char> letters)
    {
        switch (node)
        {
            case TraceStructureHas has:
                letters.Add(has.Condition);
                break;
            case TraceStructureRelation relation:
                letters.Add(relation.Parent);
                letters.Add(relation.Child);
                break;
            case TraceStructureNot not:
                Collect(not.Operand, letters);
                break;
            case TraceStructureAnd and:
                Collect(and.Left, letters);
                Collect(and.Right, letters);
                break;
            case TraceStructureOr or:
                Collect(or.Left, letters);
                Collect(or.Right, letters);
                break;
        }
    }

    private enum TokenKind
    {
        Letter,
        DirectChild,
        Descendant,
        And,
        Or,
        Not,
        Open,
        Close,
        End,
    }

    private readonly record struct Token(TokenKind Kind, string Text);

    private static List<Token> Tokenize(string expression)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < expression.Length)
        {
            var c = expression[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (char.IsAsciiLetter(c))
            {
                var start = i;
                while (i < expression.Length && char.IsAsciiLetterOrDigit(expression[i]))
                {
                    i++;
                }

                var word = expression[start..i];
                tokens.Add(word.ToUpperInvariant() switch
                {
                    "AND" => new Token(TokenKind.And, word),
                    "OR" => new Token(TokenKind.Or, word),
                    "NOT" => new Token(TokenKind.Not, word),
                    _ when word.Length == 1 => new Token(TokenKind.Letter, word.ToUpperInvariant()),
                    _ => throw Error($"Unknown word '{word}' - conditions are single letters (A-Z)."),
                });
                continue;
            }

            var two = i + 1 < expression.Length ? expression.Substring(i, 2) : string.Empty;
            switch (two)
            {
                case "->":
                    tokens.Add(new Token(TokenKind.DirectChild, two));
                    i += 2;
                    continue;
                case "=>":
                    tokens.Add(new Token(TokenKind.Descendant, two));
                    i += 2;
                    continue;
                case "&&":
                    tokens.Add(new Token(TokenKind.And, two));
                    i += 2;
                    continue;
                case "||":
                    tokens.Add(new Token(TokenKind.Or, two));
                    i += 2;
                    continue;
            }

            switch (c)
            {
                case '!':
                    tokens.Add(new Token(TokenKind.Not, "!"));
                    break;
                case '(':
                    tokens.Add(new Token(TokenKind.Open, "("));
                    break;
                case ')':
                    tokens.Add(new Token(TokenKind.Close, ")"));
                    break;
                default:
                    throw Error($"Unexpected character '{c}'.");
            }

            i++;
        }

        tokens.Add(new Token(TokenKind.End, "end of expression"));
        return tokens;
    }

    private static ArgumentException Error(string message) => new($"Invalid trace structure expression: {message}");

    private sealed class Parser(List<Token> tokens)
    {
        private int _position;

        public Token Peek => tokens[_position];

        public bool AtEnd => Peek.Kind == TokenKind.End;

        public TraceStructureNode ParseOr()
        {
            var left = ParseAnd();
            while (Peek.Kind == TokenKind.Or)
            {
                _position++;
                left = new TraceStructureOr(left, ParseAnd());
            }

            return left;
        }

        private TraceStructureNode ParseAnd()
        {
            var left = ParseNot();
            while (Peek.Kind == TokenKind.And)
            {
                _position++;
                left = new TraceStructureAnd(left, ParseNot());
            }

            return left;
        }

        private TraceStructureNode ParseNot()
        {
            if (Peek.Kind == TokenKind.Not)
            {
                _position++;
                return new TraceStructureNot(ParseNot());
            }

            return ParseRelation();
        }

        private TraceStructureNode ParseRelation()
        {
            var token = Next();
            switch (token.Kind)
            {
                case TokenKind.Open:
                {
                    var inner = ParseOr();
                    if (Next().Kind != TokenKind.Close)
                    {
                        throw Error("Missing ')'.");
                    }

                    return inner;
                }

                case TokenKind.Letter:
                {
                    if (Peek.Kind is not (TokenKind.DirectChild or TokenKind.Descendant))
                    {
                        return new TraceStructureHas(token.Text[0]);
                    }

                    var op = Next();
                    var child = Next();
                    if (child.Kind != TokenKind.Letter)
                    {
                        throw Error($"'{op.Text}' needs a condition letter on each side.");
                    }

                    if (Peek.Kind is TokenKind.DirectChild or TokenKind.Descendant)
                    {
                        throw Error($"Chains like 'A {op.Text} B {Peek.Text} C' aren't supported - write each pair separately, joined with AND.");
                    }

                    return new TraceStructureRelation(token.Text[0], child.Text[0], op.Kind == TokenKind.DirectChild);
                }

                case TokenKind.DirectChild or TokenKind.Descendant:
                    throw Error($"'{token.Text}' needs a condition letter on each side.");

                case TokenKind.End:
                    throw Error("The expression ends too early.");

                default:
                    throw Error($"Unexpected '{token.Text}'.");
            }
        }

        private Token Next() => tokens[_position < tokens.Count - 1 ? _position++ : _position];
    }
}
