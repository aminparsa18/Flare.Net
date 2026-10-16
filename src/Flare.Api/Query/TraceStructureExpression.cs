namespace Flare.Api.Query;

/// <summary>A parsed <see cref="Model.TraceStructureFilter.Expression"/> node.</summary>
public abstract record TraceStructureNode;

/// <summary>A bare condition letter: the trace has a span matching it.</summary>
public sealed record TraceStructureHas(char Condition) : TraceStructureNode;

/// <summary>How two spans are related in a <see cref="TraceStructureRelation"/>.</summary>
public enum TraceRelationKind
{
    /// <summary><c>-></c>: the right-hand span's parent is the left-hand span.</summary>
    Child,

    /// <summary><c>=></c>: the left-hand span is any ancestor of the right-hand span.</summary>
    Descendant,

    /// <summary><c>~</c>: the two spans have the same parent (two root spans of one trace are siblings, as in Tempo) and are not the same span.</summary>
    Sibling,

    /// <summary><c>&lt;-</c> (ADR-0235): the right-hand span is the parent of the left-hand span - <see cref="Child"/> with the two sides exchanged.</summary>
    Parent,

    /// <summary><c>&lt;=</c> (ADR-0235): the right-hand span is an ancestor of the left-hand span - <see cref="Descendant"/> with the two sides exchanged.</summary>
    Ancestor,
}

/// <summary>
/// <c>Left -> Right</c>, <c>Left => Right</c> or <c>Left ~ Right</c>: some span matching <see cref="Child"/> (the right-hand side) is a direct
/// child of, a descendant of, or a sibling of some span matching <see cref="Parent"/> (the left-hand side). Never the same span, even when
/// both conditions match it. With <see cref="Negated"/> (<c>!-></c>, <c>!=></c>, <c>!~</c>) it is some right-hand span that has <em>no</em> such
/// left-hand span - one with no matching parent, ancestor or sibling at all, so a trace with no left-hand span matches whenever it has a
/// right-hand one, as in Tempo. <c>Left &lt;- Right</c> and <c>Left &lt;= Right</c> (ADR-0235) turn the relation round: some right-hand span is the
/// parent, or an ancestor, of some left-hand one; negated, some right-hand span is the parent, or an ancestor, of none. The names stay those of
/// the sides (<see cref="Parent"/> is always the left-hand letter, <see cref="Child"/> the right-hand one); <see cref="Upper"/> and
/// <see cref="Lower"/> say which of them is higher in the trace.
/// </summary>
public sealed record TraceStructureRelation(char Parent, char Child, TraceRelationKind Kind, bool Negated = false) : TraceStructureNode
{
    /// <summary>Reads as the original two forms: <c>-></c> when <paramref name="direct"/>, else <c>=></c>.</summary>
    public TraceStructureRelation(char parent, char child, bool direct)
        : this(parent, child, direct ? TraceRelationKind.Child : TraceRelationKind.Descendant)
    {
    }

    /// <summary>A direct-child relation (<see cref="TraceRelationKind.Child"/>).</summary>
    public bool Direct => Kind == TraceRelationKind.Child;

    /// <summary>Whether the right-hand span is the one higher in the trace (<see cref="TraceRelationKind.Parent"/> and <see cref="TraceRelationKind.Ancestor"/>).</summary>
    public bool Reversed => Kind is TraceRelationKind.Parent or TraceRelationKind.Ancestor;

    /// <summary>The letter of the side whose spans are the parent or ancestor (the left-hand one, unless <see cref="Reversed"/>).</summary>
    public char Upper => Reversed ? Child : Parent;

    /// <summary>The letter of the side whose spans are the child or descendant.</summary>
    public char Lower => Reversed ? Parent : Child;
}

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
/// relation := LETTER ("->" | "=>" | "~" | "&lt;-" | "&lt;=" | "!->" | "!=>" | "!~" | "!&lt;-" | "!&lt;=") LETTER | LETTER | "(" or ")"
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
        Relation,
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

            var three = i + 2 < expression.Length ? expression.Substring(i, 3) : string.Empty;
            if (three is "!->" or "!=>" or "!<-" or "!<=")
            {
                tokens.Add(new Token(TokenKind.Relation, three));
                i += 3;
                continue;
            }

            var two = i + 1 < expression.Length ? expression.Substring(i, 2) : string.Empty;
            switch (two)
            {
                case "->" or "=>" or "!~" or "<-" or "<=":
                    tokens.Add(new Token(TokenKind.Relation, two));
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
                case '~':
                    tokens.Add(new Token(TokenKind.Relation, "~"));
                    break;
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

    private static (TraceRelationKind Kind, bool Negated) RelationOf(string op) => op switch
    {
        "->" => (TraceRelationKind.Child, false),
        "=>" => (TraceRelationKind.Descendant, false),
        "~" => (TraceRelationKind.Sibling, false),
        "<-" => (TraceRelationKind.Parent, false),
        "<=" => (TraceRelationKind.Ancestor, false),
        "!->" => (TraceRelationKind.Child, true),
        "!=>" => (TraceRelationKind.Descendant, true),
        "!<-" => (TraceRelationKind.Parent, true),
        "!<=" => (TraceRelationKind.Ancestor, true),
        _ => (TraceRelationKind.Sibling, true),
    };

    /// <summary>The operator that writes <paramref name="kind"/> (negated or not) in this grammar.</summary>
    public static string Operator(TraceRelationKind kind, bool negated) => (kind, negated) switch
    {
        (TraceRelationKind.Child, false) => "->",
        (TraceRelationKind.Descendant, false) => "=>",
        (TraceRelationKind.Sibling, false) => "~",
        (TraceRelationKind.Parent, false) => "<-",
        (TraceRelationKind.Ancestor, false) => "<=",
        (TraceRelationKind.Child, true) => "!->",
        (TraceRelationKind.Descendant, true) => "!=>",
        (TraceRelationKind.Parent, true) => "!<-",
        (TraceRelationKind.Ancestor, true) => "!<=",
        _ => "!~",
    };

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
                    if (Peek.Kind != TokenKind.Relation)
                    {
                        return new TraceStructureHas(token.Text[0]);
                    }

                    var op = Next();
                    var child = Next();
                    if (child.Kind != TokenKind.Letter)
                    {
                        throw Error($"'{op.Text}' needs a condition letter on each side.");
                    }

                    if (Peek.Kind == TokenKind.Relation)
                    {
                        throw Error($"Chains like 'A {op.Text} B {Peek.Text} C' aren't supported - write each pair separately, joined with AND.");
                    }

                    var (kind, negated) = RelationOf(op.Text);
                    return new TraceStructureRelation(token.Text[0], child.Text[0], kind, negated);
                }

                case TokenKind.Relation:
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
