using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Flare.Api.Errors;
using Flare.Api.Model;

namespace Flare.Api.Query;

public sealed record ErrorIssueSql(string Sql, ClickHouseParameterCollection Parameters);

/// <summary>
/// Pure SQL builders for the two reads <see cref="ErrorIssueEvaluator"/> needs from <c>spans</c>:
/// per-issue evidence (exception events after each issue's own <c>StatusChangedAt</c>, per
/// <c>service.version</c>) and a group's known versions at resolve time. Same
/// <c>ARRAY JOIN Events</c> shape as <see cref="ExceptionGroupQueryBuilder"/>.
/// </summary>
public static class ErrorIssueEvidenceQueryBuilder
{
    /// <summary>How far back a group's "known versions" are collected when it is resolved.</summary>
    public static readonly TimeSpan KnownVersionsLookback = TimeSpan.FromDays(30);

    private const string GroupKeySql = "concat(EventAttributes['exception.type'], char(31), EventAttributes['exception.message'])";

    /// <summary>
    /// Columns: group key (<see cref="ErrorIssueFingerprint.Key"/>), service.version, occurrences.
    /// Each event is compared with <em>its own</em> issue's change instant via <c>transform</c>, so
    /// one query serves every issue. Returns null when <paramref name="issues"/> is empty.
    /// </summary>
    public static ErrorIssueSql? BuildEvidence(IReadOnlyList<ErrorIssue> issues)
    {
        if (issues.Count == 0)
        {
            return null;
        }

        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("keys", issues.Select(i => ErrorIssueFingerprint.Key(i.ExceptionType, i.ExceptionMessage)).ToArray());
        parameters.AddParameter("changedMs", issues.Select(i => i.StatusChangedAt.ToUnixTimeMilliseconds()).ToArray());
        // StartTime <= every event time of the span, so the earliest change bounds the scan; the
        // hour of slack covers spans that started before the change but recorded the event after.
        parameters.AddParameter("from", issues.Min(i => i.StatusChangedAt).UtcDateTime.AddHours(-1));

        var sql = $"SELECT {GroupKeySql} AS GroupKey, ResourceAttributes['service.version'] AS Version, count() AS Occurrences\n" +
            "FROM spans\n" +
            "ARRAY JOIN Events.TimeUnixNano AS EventTime, Events.Name AS EventName, Events.Attributes AS EventAttributes\n" +
            "WHERE EventName = 'exception' AND StartTime >= {from:DateTime64(9)}" + ServiceScope.Suffix(parameters) + "\n" +
            "  AND GroupKey IN {keys:Array(String)}\n" +
            "  AND toUnixTimestamp64Milli(EventTime) > transform(GroupKey, {keys:Array(String)}, {changedMs:Array(Int64)}, toInt64(0))\n" +
            "GROUP BY GroupKey, Version";
        return new ErrorIssueSql(sql, parameters);
    }

    /// <summary>Single column: every distinct <c>service.version</c> ("" included) the group's exceptions were seen in recently.</summary>
    public static ErrorIssueSql BuildKnownVersions(string exceptionType, string exceptionMessage, DateTimeOffset now)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("exceptionType", exceptionType);
        parameters.AddParameter("exceptionMessage", exceptionMessage);
        parameters.AddParameter("from", (now - KnownVersionsLookback).UtcDateTime);
        var sql = "SELECT DISTINCT ResourceAttributes['service.version']\n" +
            "FROM spans\n" +
            "ARRAY JOIN Events.Name AS EventName, Events.Attributes AS EventAttributes\n" +
            "WHERE EventName = 'exception' AND StartTime >= {from:DateTime64(9)}" + ServiceScope.Suffix(parameters) + "\n" +
            "  AND EventAttributes['exception.type'] = {exceptionType:String}\n" +
            "  AND EventAttributes['exception.message'] = {exceptionMessage:String}";
        return new ErrorIssueSql(sql, parameters);
    }
}
