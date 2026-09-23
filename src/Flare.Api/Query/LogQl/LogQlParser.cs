using Flare.Api.Model;

namespace Flare.Api.Query.LogQl;

/// <summary>
/// Recursive-descent parser for the SQL-query-row grammar:
/// <code>select (* | col[, col...] | count(*) | avg(col) | sum(col)) from stream [where &lt;bool-expr&gt;] [group by time(&lt;dur&gt;) [, service|level]]</code>
/// Keywords/column/group names are case-insensitive. See <c>LogQlAst.cs</c> for the
/// produced tree and this feature's plan doc for the full grammar writeup, including why
/// it's deliberately this small (no bare/numeric literals, no un-time-bucketed group by,
/// group by only valid with an aggregate select).
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

        // Only used by ParseAttrComparison to look past 'not' and decide "not has" vs. "not
        // like" without consuming either token yet.
        LogQlToken PeekAt(int offset)
        {
            var idx = pos + offset;
            return idx < tokens.Count ? tokens[idx] : tokens[^1];
        }

        LogQlColumn ResolveColumn(LogQlToken t) => t.Text.ToUpperInvariant() switch
        {
            "SERVICE" => LogQlColumn.Service,
            "LEVEL" or "SEVERITY" => LogQlColumn.Level,
            "BODY" => LogQlColumn.Body,
            "TRACEID" => LogQlColumn.TraceId,
            "SPANID" => LogQlColumn.SpanId,
            "SEVERITYNUMBER" => LogQlColumn.SeverityNumber,
            _ => throw new LogQlParseException(
                $"Unknown column '{t.Text}' at position {t.Position}. Supported columns: Service, Level, Body, TraceId, SpanId, SeverityNumber."),
        };

        bool IsNumericColumn(LogQlColumn column) => column == LogQlColumn.SeverityNumber;

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

        // Shared by ParseComparison and ParseJsonComparison - both end the same way once
        // their left-hand side (a bare column vs. a json(Body, '...') path) is resolved:
        // "like '...'" / "not like '...'" / one of the = != <> < <= > >= operators followed
        // by a quoted string.
        (LogQlOp Op, string Literal) ParseOpAndLiteral()
        {
            if (IsIdentifier(Current(), "like"))
            {
                Advance();
                var literal = ExpectKind(LogQlTokenKind.String, "a quoted string");
                return (LogQlOp.Like, literal.Text);
            }

            if (IsIdentifier(Current(), "not"))
            {
                // Only reachable after a left-hand side has already been consumed, so this
                // is unambiguously "... NOT LIKE '...'" - the unary-NOT prefix (ParseNot
                // below) is checked before a comparison is ever entered.
                Advance();
                ExpectIdentifier("like");
                var literal = ExpectKind(LogQlTokenKind.String, "a quoted string");
                return (LogQlOp.NotLike, literal.Text);
            }

            var opToken = ExpectKind(LogQlTokenKind.Op, "one of = != <> < <= > >=");
            var op = ResolveOp(opToken.Text);
            var value = ExpectKind(LogQlTokenKind.String, "a quoted string");
            return (op, value.Text);
        }

        LogQlExpr ParseComparison()
        {
            var columnToken = ExpectKind(LogQlTokenKind.Identifier, "a column name");
            var column = ResolveColumn(columnToken);
            if (column == LogQlColumn.SeverityNumber)
            {
                throw new LogQlParseException(
                    $"SeverityNumber isn't supported in 'where' yet at position {columnToken.Position} - only in 'select' and avg()/sum().");
            }

            var (op, literal) = ParseOpAndLiteral();
            return new LogQlComparison(column, op, literal);
        }

        // json(Body, 'path.segments') op '...' - the LogQL-reachable form of
        // Model.BodyJsonFilter (see LogQlJsonComparison's own remarks). Only Body carries
        // JSON payloads worth path-filtering into, so unlike ParseComparison's general
        // column name this always expects the literal keyword 'Body'.
        LogQlExpr ParseJsonComparison()
        {
            Advance(); // the 'json' identifier itself (already peeked by ParsePrimary)
            ExpectKind(LogQlTokenKind.LParen, "'('");
            ExpectIdentifier("body");
            ExpectKind(LogQlTokenKind.Comma, "','");
            var pathToken = ExpectKind(LogQlTokenKind.String, "a quoted JSON path like 'user.id'");
            var segments = pathToken.Text.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                throw new LogQlParseException($"json() path at position {pathToken.Position} must not be empty.");
            }

            ExpectKind(LogQlTokenKind.RParen, "')'");

            var (op, literal) = ParseOpAndLiteral();
            return new LogQlJsonComparison(pathToken.Text, op, literal);
        }

        LogQlAttributeBag ResolveAttributeBag(LogQlToken t) => t.Text.ToUpperInvariant() switch
        {
            "LOG" => LogQlAttributeBag.Log,
            "RESOURCE" => LogQlAttributeBag.Resource,
            "SCOPE" => LogQlAttributeBag.Scope,
            _ => throw new LogQlParseException(
                $"Unknown attribute bag '{t.Text}' at position {t.Position}. Supported bags: log, resource, scope."),
        };

        // attr(log|resource|scope, 'key') op '...' / attr(...) has / attr(...) not has - the
        // LogQL-reachable form of Model.AttributeFilter (see LogQlAttributeComparison/
        // LogQlAttributeExists's own remarks). 'has'/'not has' are checked before falling
        // through to ParseOpAndLiteral, since that shared helper only ever produces an
        // op+literal pair and existence checks have no literal to parse.
        LogQlExpr ParseAttrComparison()
        {
            Advance(); // the 'attr' identifier itself (already peeked by ParsePrimary)
            ExpectKind(LogQlTokenKind.LParen, "'('");
            var bagToken = ExpectKind(LogQlTokenKind.Identifier, "'log', 'resource', or 'scope'");
            var bag = ResolveAttributeBag(bagToken);
            ExpectKind(LogQlTokenKind.Comma, "','");
            var keyToken = ExpectKind(LogQlTokenKind.String, "a quoted attribute key");
            if (keyToken.Text.Length == 0)
            {
                throw new LogQlParseException($"attr() key at position {keyToken.Position} must not be empty.");
            }

            ExpectKind(LogQlTokenKind.RParen, "')'");

            if (IsIdentifier(Current(), "has"))
            {
                Advance();
                return new LogQlAttributeExists(bag, keyToken.Text, Negate: false);
            }

            if (IsIdentifier(Current(), "not") && IsIdentifier(PeekAt(1), "has"))
            {
                Advance();
                Advance();
                return new LogQlAttributeExists(bag, keyToken.Text, Negate: true);
            }

            var (op, literal) = ParseOpAndLiteral();
            return new LogQlAttributeComparison(bag, keyToken.Text, op, literal);
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

            if (IsIdentifier(Current(), "json"))
            {
                return ParseJsonComparison();
            }

            if (IsIdentifier(Current(), "attr"))
            {
                return ParseAttrComparison();
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

        LogQlSelect ParseAggregate(LogQlAggFunc func)
        {
            Advance(); // the function-name identifier itself (already peeked by the caller)
            ExpectKind(LogQlTokenKind.LParen, "'('");
            if (func == LogQlAggFunc.Count)
            {
                ExpectKind(LogQlTokenKind.Star, "'*'");
                ExpectKind(LogQlTokenKind.RParen, "')'");
                return new LogQlSelectAggregate(LogQlAggFunc.Count, null);
            }

            var columnToken = ExpectKind(LogQlTokenKind.Identifier, "a column name");
            var column = ResolveColumn(columnToken);
            if (!IsNumericColumn(column))
            {
                var funcName = func == LogQlAggFunc.Avg ? "avg" : "sum";
                throw new LogQlParseException(
                    $"{funcName}() needs a numeric column at position {columnToken.Position} - SeverityNumber is the only numeric column today.");
            }

            ExpectKind(LogQlTokenKind.RParen, "')'");
            return new LogQlSelectAggregate(func, column);
        }

        LogQlSelect ParseSelectColumns()
        {
            var columns = new List<LogQlColumn> { ResolveColumn(ExpectKind(LogQlTokenKind.Identifier, "a column name, '*', or an aggregate like count(*)")) };
            while (Current().Kind == LogQlTokenKind.Comma)
            {
                Advance();
                columns.Add(ResolveColumn(ExpectKind(LogQlTokenKind.Identifier, "a column name")));
            }

            return new LogQlSelectColumns(columns);
        }

        // ---- entry point -------------------------------------------------

        ExpectIdentifier("select");

        LogQlSelect select;
        if (Current().Kind == LogQlTokenKind.Star)
        {
            Advance();
            select = new LogQlSelectStar();
        }
        else if (IsIdentifier(Current(), "count"))
        {
            select = ParseAggregate(LogQlAggFunc.Count);
        }
        else if (IsIdentifier(Current(), "avg"))
        {
            select = ParseAggregate(LogQlAggFunc.Avg);
        }
        else if (IsIdentifier(Current(), "sum"))
        {
            select = ParseAggregate(LogQlAggFunc.Sum);
        }
        else if (Current().Kind == LogQlTokenKind.Identifier)
        {
            select = ParseSelectColumns();
        }
        else
        {
            var t = Current();
            throw new LogQlParseException(
                $"Expected '*', a column list, or an aggregate (count(*), avg(...), sum(...)) after SELECT at position {t.Position}, found '{DescribeToken(t)}'.");
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

        if (groupBy is not null && select is not LogQlSelectAggregate)
        {
            throw new LogQlParseException("'group by' requires an aggregate select (count(*), avg(...), or sum(...)).");
        }

        return new LogQlQuery(select, where, groupBy);
    }

    private static string DescribeToken(LogQlToken t) => t.Kind == LogQlTokenKind.Eof ? "end of query" : t.Text;
}
