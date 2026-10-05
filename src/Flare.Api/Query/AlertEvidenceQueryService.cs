using ClickHouse.Driver;
using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Flare.Api.Ai;
using Flare.Api.Model;
using Microsoft.Extensions.Options;

namespace Flare.Api.Query;

/// <summary>
/// The ClickHouse seam for the bounded evidence sample behind an AI incident summary (ADR-0104).
/// Every query is capped by <see cref="QuerySafety.AlertEvaluation"/> and by a small row limit -
/// this reads a handful of grouped rows, never raw logs in bulk.
/// </summary>
public interface IAlertEvidenceQueryService
{
    Task<IReadOnlyList<IncidentLogPattern>> GetLogPatternsAsync(LogFilter filter, DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken cancellationToken);

    Task<IReadOnlyList<IncidentExceptionGroup>> GetExceptionGroupsAsync(ExceptionCountCondition condition, DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken cancellationToken);

    Task<IReadOnlyList<IncidentErrorSpan>> GetErrorSpansAsync(string traceId, DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken cancellationToken);
}

public sealed class AlertEvidenceQueryService(IClickHouseClient client, IOptions<QueryLimitsOptions> queryLimits, IPromotedAttributeRegistry promotedAttributes, IErrorIssueQueryService errorIssues) : IAlertEvidenceQueryService
{
    public async Task<IReadOnlyList<IncidentLogPattern>> GetLogPatternsAsync(LogFilter filter, DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken cancellationToken)
    {
        var built = LogFilterSqlBuilder.Build(filter with { From = from, To = to }, to, promotedAttributes.Logs);
        // Unclustered rows (no PatternTemplate) group by their own truncated body.
        var sql = $"""
            SELECT if(PatternTemplate != '', PatternTemplate, substring(Body, 1, 300)) AS Template,
                   any(ServiceName), max(SeverityNumber) AS Severity, count() AS Occurrences, anyIf(TraceId, TraceId != '')
            FROM logs
            WHERE {built.WhereSql}
            GROUP BY Template
            ORDER BY Severity DESC, Occurrences DESC
            LIMIT {Math.Clamp(limit, 1, 50)}
            """;

        await using var reader = await client.ExecuteReaderAsync(sql, built.Parameters, QuerySafety.AlertEvaluation(queryLimits.Value), cancellationToken);
        var rows = new List<IncidentLogPattern>();
        while (reader.Read())
        {
            rows.Add(new IncidentLogPattern(reader.GetString(0), reader.GetString(1), reader.GetByte(2), reader.GetFieldValue<ulong>(3), reader.GetString(4)));
        }

        return rows;
    }

    public async Task<IReadOnlyList<IncidentExceptionGroup>> GetExceptionGroupsAsync(ExceptionCountCondition condition, DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken cancellationToken)
    {
        var ignored = await errorIssues.ListIgnoredKeysAsync(cancellationToken);
        var built = ExceptionCountConditionQueryBuilder.BuildTopGroups(condition, from, to, limit, ignored);
        await using var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, QuerySafety.AlertEvaluation(queryLimits.Value), cancellationToken);
        var rows = new List<IncidentExceptionGroup>();
        while (reader.Read())
        {
            rows.Add(new IncidentExceptionGroup(reader.GetString(0), reader.GetString(1), reader.GetFieldValue<ulong>(2), reader.GetString(3), reader.GetString(4)));
        }

        return rows;
    }

    public async Task<IReadOnlyList<IncidentErrorSpan>> GetErrorSpansAsync(string traceId, DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken cancellationToken)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("traceId", traceId);
        // A trace can start before the alert window and finish inside it; pad the lower bound so
        // the spans' partition pruning doesn't hide them.
        parameters.AddParameter("from", from.UtcDateTime.AddMinutes(-15));
        parameters.AddParameter("to", to.UtcDateTime.AddMinutes(15));
        var sql = """
            SELECT ServiceName, Name, StatusMessage, DurationNano
            FROM spans
            WHERE TraceId = {traceId:String} AND StartTime >= {from:DateTime64(9)} AND StartTime <= {to:DateTime64(9)}
              AND StatusCode = 'STATUS_CODE_ERROR'
            ORDER BY StartTime
            LIMIT
            """ + $" {Math.Clamp(limit, 1, 50)}";

        await using var reader = await client.ExecuteReaderAsync(sql, parameters, QuerySafety.AlertEvaluation(queryLimits.Value), cancellationToken);
        var rows = new List<IncidentErrorSpan>();
        while (reader.Read())
        {
            rows.Add(new IncidentErrorSpan(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetFieldValue<ulong>(3) / 1_000_000.0));
        }

        return rows;
    }
}
