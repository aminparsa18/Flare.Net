using System.Globalization;
using System.Text;

namespace Flare.Api.Loki;

/// <summary>
/// The Go <c>text/template</c> subset <c>| line_format</c> accepts (ADR-0237): literal text and
/// <c>{{ ... }}</c> actions holding a pipeline of commands. An operand is a label (<c>.name</c>), a string
/// or number literal, or the zero-argument functions <c>__line__</c> and <c>__timestamp__</c>; a command
/// is an operand or a function call, and <c>|</c> passes a command's result as the last argument of the
/// next, as in Go. Functions: <c>ToLower</c>/<c>lower</c>, <c>ToUpper</c>/<c>upper</c>, <c>Title</c>/<c>title</c>,
/// <c>trim</c>, <c>trimAll</c>, <c>trimPrefix</c>, <c>trimSuffix</c>, <c>replace</c>, <c>trunc</c>,
/// <c>default</c>. Control actions (<c>if</c>, <c>range</c>, <c>with</c>, variables) are rejected when the
/// query is parsed. A label that does not exist renders as the empty string. Pure.
/// </summary>
internal sealed class LokiTemplate
{
    private static readonly Dictionary<string, int> FunctionArity = new(StringComparer.Ordinal)
    {
        ["ToLower"] = 1, ["lower"] = 1, ["ToUpper"] = 1, ["upper"] = 1, ["Title"] = 1, ["title"] = 1, ["trim"] = 1,
        ["trimAll"] = 2, ["trimPrefix"] = 2, ["trimSuffix"] = 2, ["replace"] = 3, ["trunc"] = 2, ["default"] = 2,
        ["__line__"] = 0, ["__timestamp__"] = 0,
    };

    private readonly List<Piece> _pieces;

    private LokiTemplate(List<Piece> pieces, string source)
    {
        _pieces = pieces;
        Source = source;
    }

    public string Source { get; }

    /// <summary>Every label the template reads, so a caller can tell whether it needs any.</summary>
    public IEnumerable<string> Labels => _pieces
        .OfType<Action>()
        .SelectMany(a => a.Commands)
        .SelectMany(c => c.Operands.Append(c.Head))
        .Where(o => o.Kind == OperandKind.Label)
        .Select(o => o.Text);

    /// <summary>Parses <paramref name="template"/>, throwing <see cref="LokiException"/> for malformed or unsupported syntax.</summary>
    public static LokiTemplate Parse(string template)
    {
        var pieces = new List<Piece>();
        var pos = 0;
        var trimNextLeading = false;
        while (pos < template.Length)
        {
            var open = template.IndexOf("{{", pos, StringComparison.Ordinal);
            if (open < 0)
            {
                AddText(pieces, template[pos..], trimNextLeading, trimTrailing: false);
                break;
            }

            var close = template.IndexOf("}}", open + 2, StringComparison.Ordinal);
            if (close < 0)
            {
                throw new LokiException("line_format template: unclosed action.");
            }

            var inner = template[(open + 2)..close];
            var trimLeading = inner.StartsWith("- ", StringComparison.Ordinal);
            var trimTrailing = inner.EndsWith(" -", StringComparison.Ordinal);
            if (trimLeading)
            {
                inner = inner[2..];
            }

            if (trimTrailing)
            {
                inner = inner[..^2];
            }

            AddText(pieces, template[pos..open], trimNextLeading, trimLeading);
            pieces.Add(ParseAction(inner));
            trimNextLeading = trimTrailing;
            pos = close + 2;
        }

        return new LokiTemplate(pieces, template);
    }

    /// <summary>Renders the template. <paramref name="label"/> returns a label's value or null; <paramref name="line"/> is the original line.</summary>
    public string Render(Func<string, string?> label, string line, DateTimeOffset timestamp)
    {
        var sb = new StringBuilder();
        foreach (var piece in _pieces)
        {
            switch (piece)
            {
                case Text text:
                    sb.Append(text.Value);
                    break;
                case Action action:
                    sb.Append(action.Evaluate(label, line, timestamp));
                    break;
            }
        }

        return sb.ToString();
    }

    private static void AddText(List<Piece> pieces, string text, bool trimLeading, bool trimTrailing)
    {
        if (trimLeading)
        {
            text = text.TrimStart();
        }

        if (trimTrailing)
        {
            text = text.TrimEnd();
        }

        if (text.Length > 0)
        {
            pieces.Add(new Text(text));
        }
    }

    private static Action ParseAction(string inner)
    {
        var reader = new ActionReader(inner);
        var commands = new List<Command>();
        while (true)
        {
            var command = reader.ReadCommand();
            if (command.IsCall)
            {
                // A command behind a pipe receives the previous result as its last argument.
                var given = command.Operands.Count + (commands.Count > 0 ? 1 : 0);
                var expected = FunctionArity[command.Head.Text];
                if (given != expected)
                {
                    throw new LokiException($"line_format template: {command.Head.Text} takes {expected} argument{(expected == 1 ? "" : "s")}, got {given}.");
                }
            }

            commands.Add(command);
            if (!reader.TryConsume('|'))
            {
                break;
            }
        }

        reader.ExpectEnd();
        return new Action(commands);
    }

    private abstract record Piece;

    private sealed record Text(string Value) : Piece;

    private sealed record Action(List<Command> Commands) : Piece
    {
        public string Evaluate(Func<string, string?> label, string line, DateTimeOffset timestamp)
        {
            string? piped = null;
            foreach (var command in Commands)
            {
                piped = command.Evaluate(label, line, timestamp, piped);
            }

            return piped ?? string.Empty;
        }
    }

    private enum OperandKind
    {
        Label,
        Literal,
        Function,
    }

    private sealed record Operand(OperandKind Kind, string Text)
    {
        public string Evaluate(Func<string, string?> label, string line, DateTimeOffset timestamp) => Kind switch
        {
            OperandKind.Label => label(Text) ?? string.Empty,
            OperandKind.Literal => Text,
            _ => Text == "__line__" ? line : timestamp.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffffZ", CultureInfo.InvariantCulture),
        };
    }

    /// <summary>
    /// A pipeline command: <see cref="Head"/> alone, or the function named by <see cref="Head"/> applied to
    /// <see cref="Operands"/> (plus the previous command's result as the last argument).
    /// </summary>
    private sealed record Command(Operand Head, List<Operand> Operands, bool IsCall)
    {
        public string Evaluate(Func<string, string?> label, string line, DateTimeOffset timestamp, string? piped)
        {
            if (!IsCall)
            {
                return piped is not null
                    ? throw new LokiException("line_format template: can't give a value to a pipeline of operands.")
                    : Head.Evaluate(label, line, timestamp);
            }

            if (Head.Text is "__line__" or "__timestamp__")
            {
                return Head.Evaluate(label, line, timestamp);
            }

            var args = Operands.Select(o => o.Evaluate(label, line, timestamp)).ToList();
            if (piped is not null)
            {
                args.Add(piped);
            }

            return Apply(Head.Text, args);
        }

        private static string Apply(string function, List<string> a)
        {
            switch (function)
            {
                case "ToLower" or "lower":
                    return a[0].ToLowerInvariant();
                case "ToUpper" or "upper":
                    return a[0].ToUpperInvariant();
                case "Title" or "title":
                    return Title(a[0]);
                case "trim":
                    return a[0].Trim();
                case "trimAll":
                    return a[1].Trim([.. a[0]]);
                case "trimPrefix":
                    return a[1].StartsWith(a[0], StringComparison.Ordinal) ? a[1][a[0].Length..] : a[1];
                case "trimSuffix":
                    return a[1].EndsWith(a[0], StringComparison.Ordinal) ? a[1][..^a[0].Length] : a[1];
                case "replace":
                    return a[0].Length == 0 ? a[2] : a[2].Replace(a[0], a[1], StringComparison.Ordinal);
                case "trunc":
                    return Truncate(a[0], a[1]);
                default:
                    // "default": a[0] is the fallback, a[1] the value.
                    return a[1].Length == 0 ? a[0] : a[1];
            }
        }

        private static string Title(string value)
        {
            var sb = new StringBuilder(value.Length);
            var startOfWord = true;
            foreach (var c in value)
            {
                sb.Append(startOfWord ? char.ToUpperInvariant(c) : c);
                startOfWord = char.IsWhiteSpace(c);
            }

            return sb.ToString();
        }

        /// <summary>Sprig's <c>trunc</c>: the first <c>n</c> characters, or the last <c>-n</c> when negative.</summary>
        private static string Truncate(string length, string value)
        {
            if (!int.TryParse(length, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
            {
                throw new LokiException($"line_format template: trunc needs a whole number, got \"{length}\".");
            }

            if (n >= 0)
            {
                return n >= value.Length ? value : value[..n];
            }

            return -n >= value.Length ? value : value[^(-n)..];
        }
    }

    /// <summary>Scans the inside of one <c>{{ }}</c> action.</summary>
    private sealed class ActionReader(string text)
    {
        private int _pos;

        public bool TryConsume(char c)
        {
            SkipWhitespace();
            if (_pos < text.Length && text[_pos] == c)
            {
                _pos++;
                return true;
            }

            return false;
        }

        public void ExpectEnd()
        {
            SkipWhitespace();
            if (_pos < text.Length)
            {
                throw new LokiException($"line_format template: unexpected '{text[_pos]}'.");
            }
        }

        public Command ReadCommand()
        {
            SkipWhitespace();
            if (_pos >= text.Length)
            {
                throw new LokiException("line_format template: empty action.");
            }

            var c = text[_pos];
            if (char.IsAsciiLetter(c) || c == '_')
            {
                var name = ReadIdentifier();
                if (name is "if" or "else" or "end" or "range" or "with" or "define" or "template" or "block" or "break" or "continue")
                {
                    throw new LokiException($"line_format template: '{name}' is not supported; only value substitution and the functions lower, upper, title, trim, trimAll, trimPrefix, trimSuffix, replace, trunc and default are.");
                }

                if (!FunctionArity.ContainsKey(name))
                {
                    throw new LokiException($"line_format template: function \"{name}\" is not supported.");
                }

                var args = new List<Operand>();
                while (true)
                {
                    SkipWhitespace();
                    if (_pos >= text.Length || text[_pos] == '|')
                    {
                        break;
                    }

                    args.Add(ReadOperand());
                }

                return new Command(new Operand(OperandKind.Function, name), args, IsCall: true);
            }

            return new Command(ReadOperand(), [], IsCall: false);
        }

        private Operand ReadOperand()
        {
            SkipWhitespace();
            var c = text[_pos];
            if (c == '.')
            {
                _pos++;
                var name = ReadIdentifier();
                return new Operand(OperandKind.Label, name);
            }

            if (c is '"' or '`')
            {
                return new Operand(OperandKind.Literal, ReadQuoted(c));
            }

            if (char.IsAsciiDigit(c) || c == '-')
            {
                var start = _pos++;
                while (_pos < text.Length && (char.IsAsciiDigit(text[_pos]) || text[_pos] == '.'))
                {
                    _pos++;
                }

                return new Operand(OperandKind.Literal, text[start.._pos]);
            }

            if (char.IsAsciiLetter(c) || c == '_')
            {
                var name = ReadIdentifier();
                return name is "__line__" or "__timestamp__"
                    ? new Operand(OperandKind.Function, name)
                    : throw new LokiException($"line_format template: \"{name}\" is not a value; labels are written .{name}.");
            }

            throw new LokiException($"line_format template: unexpected '{c}'.");
        }

        private string ReadIdentifier()
        {
            var start = _pos;
            while (_pos < text.Length && (char.IsAsciiLetterOrDigit(text[_pos]) || text[_pos] == '_'))
            {
                _pos++;
            }

            return _pos > start ? text[start.._pos] : throw new LokiException("line_format template: expected a name after '.'.");
        }

        private string ReadQuoted(char quote)
        {
            _pos++;
            var sb = new StringBuilder();
            while (_pos < text.Length)
            {
                var c = text[_pos++];
                if (c == quote)
                {
                    return sb.ToString();
                }

                if (c == '\\' && quote == '"' && _pos < text.Length)
                {
                    var escaped = text[_pos++];
                    sb.Append(escaped switch { 'n' => '\n', 't' => '\t', 'r' => '\r', _ => escaped });
                    continue;
                }

                sb.Append(c);
            }

            throw new LokiException("line_format template: unterminated string.");
        }

        private void SkipWhitespace()
        {
            while (_pos < text.Length && char.IsWhiteSpace(text[_pos]))
            {
                _pos++;
            }
        }
    }
}
