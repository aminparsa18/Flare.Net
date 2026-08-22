using Flare.Api.Model;

namespace Flare.Api.Query.LogQl;

/// <summary>
/// Recursive-descent parser for the SQL-query-row grammar:
/// <code>select (count(*) | *) from stream [where &lt;bool-expr&gt;] [group by time(&lt;dur&gt;) [, service|level]]</code>
/// Keywords/column/group names are case-insensitive. See <c>LogQlAst.cs</c> for the
/// produced tree and this feature's plan doc for the full grammar writeup, including why
/// it's deliberately this small (no bare/numeric literals, no un-time-bucketed group by,
/// group by only valid with count(*)).
/// </summary>
public static class LogQlParser
{
    public static LogQlQuery Parse(string text)
    {
        var tokens = LogQlLexer.Tokenize(text);
        var pos = 0;

        LogQlToken Current() => tokens[pos];

        LogQlToken Advance()
        {
            var t = tokens[pos];
            if (pos < tokens.Count - 1)
            {
                pos++;
            }

            return t;
        }

        bool IsIdentifier(LogQlToken t, string keyword) =>
            t.Kind == LogQlTokenKind.Identifier && string.Equals(t.Text, keyword, StringComparison.OrdinalIgnoreCase);

        LogQlToken ExpectIdentifier(string keyword)
        {
            var t = Current();
            if (!IsIdentifier(t, keyword))
            {
                throw new LogQlParseException($"Expected '{keyword}' at position {t.Position}, found '{DescribeToken(t)}'.");
            }

            return Advance();
        }

        LogQlToken ExpectKind(LogQlTokenKind kind, string what)
        {
            var t = Current();
            if (t.Kind != kind)
            {
                throw new LogQlParseException($"Expected {what} at position {t.Position}, found '{DescribeToken(t)}'.");
            }

            return Advance();
        }

        LogQlColumn ResolveColumn(LogQlToken t) => t.Text.ToUpperInvariant() switch
        {
            "SERVICE" => LogQlColumn.Service,
            "LEVEL" or "SEVERITY" => LogQlColumn.Level,
            "BODY" => LogQlColumn.Body,
            "TRACEID" => LogQlColumn.TraceId,
            "SPANID" => LogQlColumn.SpanId,
            _ => throw new LogQlParseException(
                $"Unknown column '{t.Text}' at position {t.Position}. Supported columns: Service, Level, Body, TraceId, SpanId."),
        };

        LogQlOp ResolveOp(string opText) => opText switch
        {
            "=" => LogQlOp.Eq,
            "!=" or "<>" => LogQlOp.NotEq,
            "<" => LogQlOp.Lt,
            "<=" => LogQlOp.Lte,
            ">" => LogQlOp.Gt,
            ">=" => LogQlOp.Gte,
            _ => throw new LogQlParseException($"Unsupported operator '{opText}'."), // unreachable - the lexer only ever produces one of the above
        };

        LogQlExpr ParseComparison()
        {
            var columnToken = ExpectKind(LogQlTokenKind.Identifier, "a column name");
            var column = ResolveColumn(columnToken);

            if (IsIdentifier(Current(), "like"))
            {
                Advance();
                var literal = ExpectKind(LogQlTokenKind.String, "a quoted string");
                return new LogQlComparison(column, LogQlOp.Like, literal.Text);
            }

            if (IsIdentifier(Current(), "not"))
            {
                // Only reachable after a column has already been consumed above, so this
                // is unambiguously "col NOT LIKE '...'" - the unary-NOT prefix (ParseNot
                // below) is checked before a comparison is ever entered.
                Advance();
                ExpectIdentifier("like");
                var literal = ExpectKind(LogQlTokenKind.String, "a quoted string");
                return new LogQlComparison(column, LogQlOp.NotLike, literal.Text);
            }

            var opToken = ExpectKind(LogQlTokenKind.Op, "one of = != <> < <= > >=");
            var op = ResolveOp(opToken.Text);
            var value = ExpectKind(LogQlTokenKind.String, "a quoted string");
            return new LogQlComparison(column, op, value.Text);
        }

        LogQlExpr ParsePrimary()
        {
            if (Current().Kind == LogQlTokenKind.LParen)
            {
                Advance();
                var inner = ParseOr();
                ExpectKind(LogQlTokenKind.RParen, "')'");
                return inner;
            }

            return ParseComparison();
        }

        LogQlExpr ParseNot()
        {
            if (IsIdentifier(Current(), "not"))
            {
                Advance();
                return new LogQlNot(ParseNot());
            }

            return ParsePrimary();
        }

        LogQlExpr ParseAnd()
        {
            var left = ParseNot();
            while (IsIdentifier(Current(), "and"))
            {
                Advance();
                left = new LogQlBinary(LogQlBoolOp.And, left, ParseNot());
            }

            return left;
        }

        LogQlExpr ParseOr()
        {
            var left = ParseAnd();
            while (IsIdentifier(Current(), "or"))
            {
                Advance();
                left = new LogQlBinary(LogQlBoolOp.Or, left, ParseAnd());
            }

            return left;
        }

        int ParseDurationSeconds()
        {
            var token = ExpectKind(LogQlTokenKind.Duration, "a duration like '1h', '15m', '30s', or '7d'");
            var digitsEnd = 0;
            while (digitsEnd < token.Text.Length && char.IsDigit(token.Text[digitsEnd]))
            {
                digitsEnd++;
            }

            var unit = token.Text[digitsEnd..];
            if (digitsEnd == 0 || unit.Length != 1 || !int.TryParse(token.Text[..digitsEnd], out var amount) || amount <= 0)
            {
                throw new LogQlParseException(
                    $"Invalid duration '{token.Text}' at position {token.Position} - expected e.g. '1h', '15m', '30s', or '7d'.");
            }

            var unitSeconds = unit[0] switch
            {
                's' or 'S' => 1,
                'm' or 'M' => 60,
                'h' or 'H' => 3600,
                'd' or 'D' => 86_400,
                _ => throw new LogQlParseException(
                    $"Invalid duration unit '{unit}' at position {token.Position} - expected one of s, m, h, d."),
            };

            return amount * unitSeconds;
        }

        LogQlGroupBy ParseGroupBy()
        {
            ExpectIdentifier("time");
            ExpectKind(LogQlTokenKind.LParen, "'('");
            var seconds = ParseDurationSeconds();
            ExpectKind(LogQlTokenKind.RParen, "')'");

            var secondary = LogAggregateGroupBy.None;
            if (Current().Kind == LogQlTokenKind.Comma)
            {
                Advance();
                var columnToken = ExpectKind(LogQlTokenKind.Identifier, "'service' or 'level'");
                secondary = columnToken.Text.ToUpperInvariant() switch
                {
                    "SERVICE" => LogAggregateGroupBy.Service,
                    "LEVEL" or "SEVERITY" => LogAggregateGroupBy.Level,
                    _ => throw new LogQlParseException(
                        $"Unsupported group by column '{columnToken.Text}' at position {columnToken.Position} - expected 'service' or 'level'."),
                };
            }

            return new LogQlGroupBy(seconds, secondary);
        }

        // ---- entry point -------------------------------------------------

        ExpectIdentifier("select");

        LogQlSelectKind selectKind;
        if (Current().Kind == LogQlTokenKind.Star)
        {
            Advance();
            selectKind = LogQlSelectKind.Raw;
        }
        else if (IsIdentifier(Current(), "count"))
        {
            Advance();
            ExpectKind(LogQlTokenKind.LParen, "'('");
            ExpectKind(LogQlTokenKind.Star, "'*'");
            ExpectKind(LogQlTokenKind.RParen, "')'");
            selectKind = LogQlSelectKind.Count;
        }
        else
        {
            var t = Current();
            throw new LogQlParseException($"Expected '*' or 'count(*)' after SELECT at position {t.Position}, found '{DescribeToken(t)}'.");
        }

        ExpectIdentifier("from");
        ExpectIdentifier("stream");

        LogQlExpr? where = null;
        if (IsIdentifier(Current(), "where"))
        {
            Advance();
            where = ParseOr();
        }

        LogQlGroupBy? groupBy = null;
        if (IsIdentifier(Current(), "group"))
        {
            Advance();
            ExpectIdentifier("by");
            groupBy = ParseGroupBy();
        }

        if (Current().Kind != LogQlTokenKind.Eof)
        {
            var t = Current();
            throw new LogQlParseException($"Unexpected '{DescribeToken(t)}' at position {t.Position}.");
        }

        if (groupBy is not null && selectKind != LogQlSelectKind.Count)
        {
            throw new LogQlParseException("'group by' requires 'select count(*)' - 'select *' can't be grouped.");
        }

        return new LogQlQuery(selectKind, where, groupBy);
    }

    private static string DescribeToken(LogQlToken t) => t.Kind == LogQlTokenKind.Eof ? "end of query" : t.Text;
}
