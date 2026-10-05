using ClickHouse.Driver;
using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.ADO.Readers;
using ClickHouse.Driver.Utility;
using Flare.Api.Errors;
using Flare.Api.Model;
using Microsoft.Extensions.Options;

namespace Flare.Api.Query;

public interface IErrorIssueQueryService
{
    /// <summary>Every triaged group with its effective status (lapsed ignores read Open, recurring resolved groups Regressed).</summary>
    Task<IReadOnlyList<ErrorIssue>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Applies <paramref name="request"/> to the group's state and returns the effective result, or null when it ends up Open and unassigned (the row is tombstoned).</summary>
    Task<ErrorIssue?> UpsertAsync(ErrorIssueRequest request, string changedBy, CancellationToken cancellationToken);

    /// <summary><see cref="ErrorIssueFingerprint.Key"/> of every group that is effectively Ignored right now - what exception-count alert rules skip.</summary>
    Task<IReadOnlyList<string>> ListIgnoredKeysAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The ClickHouse seam for <c>error_issues</c> - same CRUD-via-tombstone / <see cref="LatestVersionSql"/>
/// pattern as <see cref="MaintenanceWindowQueryService"/>, plus the evidence read that turns stored
/// state into an effective status (<see cref="ErrorIssueEvaluator"/>).
/// </summary>
public sealed class ErrorIssueQueryService(IClickHouseClient client, IOptions<QueryLimitsOptions> queryLimits, TimeProvider timeProvider) : IErrorIssueQueryService
{
    private const string IssueColumns =
        "Id, ExceptionType, ExceptionMessage, Status, Assignee, StatusChangedAt, StatusChangedBy, IgnoreUntil, IgnoreUntilOccurrences, KnownVersions, CreatedAt, UpdatedAt";

    public async Task<IReadOnlyList<ErrorIssue>> ListAsync(CancellationToken cancellationToken)
    {
        var stored = await ReadStoredAsync(cancellationToken);
        return await EvaluateAsync(stored, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ListIgnoredKeysAsync(CancellationToken cancellationToken)
    {
        var stored = (await ReadStoredAsync(cancellationToken)).Where(i => i.Status == ErrorIssueStatus.Ignored).ToList();
        if (stored.Count == 0)
        {
            return [];
        }

        var evaluated = await EvaluateAsync(stored, cancellationToken);
        return evaluated.Where(ErrorIssueEvaluator.IsMuted).Select(i => ErrorIssueFingerprint.Key(i.ExceptionType, i.ExceptionMessage)).ToList();
    }

    public async Task<ErrorIssue?> UpsertAsync(ErrorIssueRequest request, string changedBy, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var id = ErrorIssueFingerprint.Compute(request.ExceptionType, request.ExceptionMessage);
        var existing = await GetStoredAsync(id, cancellationToken);

        var status = request.Status ?? existing?.Status ?? ErrorIssueStatus.Open;
        var assignee = request.Assignee is null ? existing?.Assignee ?? "" : request.Assignee.Trim();
        var statusChanged = existing is null ? status != ErrorIssueStatus.Open : request.Status is { } s && s != existing.Status;
        // Re-applying Ignored/Resolved restarts the clock too (new ignore limits, new resolve baseline).
        statusChanged |= request.Status is ErrorIssueStatus.Ignored or ErrorIssueStatus.Resolved;

        var issue = new ErrorIssue
        {
            Id = id,
            ExceptionType = request.ExceptionType,
            ExceptionMessage = request.ExceptionMessage,
            Status = status,
            Assignee = assignee,
            StatusChangedAt = statusChanged || existing is null ? now : existing.StatusChangedAt,
            StatusChangedBy = statusChanged || existing is null ? changedBy : existing.StatusChangedBy,
            IgnoreUntil = status == ErrorIssueStatus.Ignored ? (statusChanged ? request.IgnoreUntil : existing?.IgnoreUntil) : null,
            IgnoreUntilOccurrences = status == ErrorIssueStatus.Ignored ? (statusChanged ? request.IgnoreUntilOccurrences : existing?.IgnoreUntilOccurrences) : null,
            KnownVersions = status == ErrorIssueStatus.Resolved
                ? (statusChanged || existing is null ? await ReadKnownVersionsAsync(request, now, cancellationToken) : existing.KnownVersions)
                : [],
            CreatedAt = existing?.CreatedAt ?? now,
            UpdatedAt = now,
        };

        var cleared = status == ErrorIssueStatus.Open && assignee.Length == 0;
        if (cleared && existing is null)
        {
            return null;
        }

        await InsertVersionAsync(issue, isDeleted: cleared, cancellationToken);
        return cleared ? null : (await EvaluateAsync([issue], cancellationToken))[0];
    }

    private async Task<IReadOnlyList<ErrorIssue>> EvaluateAsync(IReadOnlyList<ErrorIssue> stored, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        // Only Resolved and count-limited Ignored issues need to look at spans.
        var needEvidence = stored.Where(i => i.Status == ErrorIssueStatus.Resolved || (i.Status == ErrorIssueStatus.Ignored && i.IgnoreUntilOccurrences is not null)).ToList();
        var evidence = new Dictionary<string, List<ErrorIssueEvidence>>();
        if (ErrorIssueEvidenceQueryBuilder.BuildEvidence(needEvidence) is { } built)
        {
            await using var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, SafetyOptions(), cancellationToken);
            while (reader.Read())
            {
                var key = reader.GetString(0);
                if (!evidence.TryGetValue(key, out var list))
                {
                    evidence[key] = list = [];
                }

                list.Add(new ErrorIssueEvidence(reader.GetString(1), (long)reader.GetFieldValue<ulong>(2)));
            }
        }

        return stored
            .Select(i => ErrorIssueEvaluator.Evaluate(
                i,
                evidence.TryGetValue(ErrorIssueFingerprint.Key(i.ExceptionType, i.ExceptionMessage), out var list) ? list : [],
                now))
            .ToList();
    }

    private async Task<IReadOnlyList<string>> ReadKnownVersionsAsync(ErrorIssueRequest request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var built = ErrorIssueEvidenceQueryBuilder.BuildKnownVersions(request.ExceptionType, request.ExceptionMessage, now);
        await using var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, SafetyOptions(), cancellationToken);
        var versions = new List<string>();
        while (reader.Read())
        {
            versions.Add(reader.GetString(0));
        }

        return versions;
    }

    private async Task<List<ErrorIssue>> ReadStoredAsync(CancellationToken cancellationToken)
    {
        var sql = LatestVersionSql.Select("error_issues", IssueColumns, orderBy: "UpdatedAt DESC");
        await using var reader = await client.ExecuteReaderAsync(sql, null, SafetyOptions(), cancellationToken);
        var issues = new List<ErrorIssue>();
        while (reader.Read())
        {
            issues.Add(ReadIssue(reader));
        }

        return issues;
    }

    private async Task<ErrorIssue?> GetStoredAsync(string id, CancellationToken cancellationToken)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("id", id);
        var sql = LatestVersionSql.Select("error_issues", IssueColumns, idWhere: "Id = {id:String}");
        await using var reader = await client.ExecuteReaderAsync(sql, parameters, SafetyOptions(), cancellationToken);
        return reader.Read() ? ReadIssue(reader) : null;
    }

    private async Task InsertVersionAsync(ErrorIssue issue, bool isDeleted, CancellationToken cancellationToken)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("id", issue.Id);
        parameters.AddParameter("exceptionType", issue.ExceptionType);
        parameters.AddParameter("exceptionMessage", issue.ExceptionMessage);
        parameters.AddParameter("isDeleted", isDeleted ? (byte)1 : (byte)0);
        parameters.AddParameter("status", issue.Status.ToString());
        parameters.AddParameter("assignee", issue.Assignee);
        parameters.AddParameter("statusChangedAt", issue.StatusChangedAt.UtcDateTime);
        parameters.AddParameter("statusChangedBy", issue.StatusChangedBy);
        parameters.AddParameter("ignoreUntil", (object?)issue.IgnoreUntil?.UtcDateTime ?? DBNull.Value);
        parameters.AddParameter("ignoreUntilOccurrences", (object?)(issue.IgnoreUntilOccurrences is { } n ? (uint)n : null) ?? DBNull.Value);
        parameters.AddParameter("knownVersions", issue.KnownVersions.ToArray());
        parameters.AddParameter("createdAt", issue.CreatedAt.UtcDateTime);
        parameters.AddParameter("updatedAt", issue.UpdatedAt.UtcDateTime);

        const string sql = """
            INSERT INTO error_issues
                (Id, ExceptionType, ExceptionMessage, IsDeleted, Status, Assignee, StatusChangedAt, StatusChangedBy, IgnoreUntil, IgnoreUntilOccurrences, KnownVersions, CreatedAt, UpdatedAt)
            VALUES
                ({id:String}, {exceptionType:String}, {exceptionMessage:String}, {isDeleted:UInt8}, {status:String}, {assignee:String}, {statusChangedAt:DateTime64(3)}, {statusChangedBy:String}, {ignoreUntil:Nullable(DateTime64(3))}, {ignoreUntilOccurrences:Nullable(UInt32)}, {knownVersions:Array(String)}, {createdAt:DateTime64(3)}, {updatedAt:DateTime64(3)})
            """;

        await client.ExecuteNonQueryAsync(sql, parameters, SafetyOptions(), cancellationToken);
    }

    private static ErrorIssue ReadIssue(ClickHouseDataReader reader) => new()
    {
        Id = reader.GetString(0),
        ExceptionType = reader.GetString(1),
        ExceptionMessage = reader.GetString(2),
        Status = Enum.TryParse<ErrorIssueStatus>(reader.GetString(3), out var status) ? status : ErrorIssueStatus.Open,
        Assignee = reader.GetString(4),
        StatusChangedAt = ReadUtc(reader, 5),
        StatusChangedBy = reader.GetString(6),
        IgnoreUntil = reader.IsDBNull(7) ? null : ReadUtc(reader, 7),
        IgnoreUntilOccurrences = reader.IsDBNull(8) ? null : (int)reader.GetFieldValue<uint>(8),
        KnownVersions = reader.GetFieldValue<string[]>(9),
        CreatedAt = ReadUtc(reader, 10),
        UpdatedAt = ReadUtc(reader, 11),
    };

    private static DateTimeOffset ReadUtc(ClickHouseDataReader reader, int ordinal) =>
        new(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));

    private QueryOptions SafetyOptions() => QuerySafety.Full(queryLimits.Value);
}
