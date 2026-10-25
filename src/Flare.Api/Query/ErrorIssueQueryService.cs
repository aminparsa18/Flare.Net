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

    /// <summary>The tracker issue URL stored for the group ("" when none), without evaluating its status.</summary>
    Task<string> GetIssueUrlAsync(string exceptionType, string exceptionMessage, CancellationToken cancellationToken);

    /// <summary><see cref="ErrorIssueFingerprint.Key"/> of every group that is effectively Ignored right now - what exception-count alert rules skip.</summary>
    Task<IReadOnlyList<string>> ListIgnoredKeysAsync(CancellationToken cancellationToken);

    /// <summary>Explicit assignee per group, keyed by <see cref="ErrorIssueFingerprint.Key"/>; unassigned groups are absent.</summary>
    Task<IReadOnlyDictionary<string, string>> ListAssigneesAsync(CancellationToken cancellationToken);

    /// <summary>Moves every stored issue to the group its message belongs to under the current grouping rules (ADR-0259). Call after the rules change.</summary>
    Task RekeyAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The ClickHouse seam for <c>error_issues</c> - same CRUD-via-tombstone / <see cref="LatestVersionSql"/>
/// pattern as <see cref="MaintenanceWindowQueryService"/>, plus the evidence read that turns stored
/// state into an effective status (<see cref="ErrorIssueEvaluator"/>).
/// </summary>
public sealed class ErrorIssueQueryService(IClickHouseClient client, IOptions<QueryLimitsOptions> queryLimits, TimeProvider timeProvider, IErrorGroupingQueryService grouping) : IErrorIssueQueryService
{
    private const string IssueColumns =
        "Id, ExceptionType, ExceptionMessage, Status, Assignee, StatusChangedAt, StatusChangedBy, IgnoreUntil, IgnoreUntilOccurrences, KnownVersions, CreatedAt, UpdatedAt, IssueUrl";

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

    public async Task<IReadOnlyDictionary<string, string>> ListAssigneesAsync(CancellationToken cancellationToken) =>
        (await ReadStoredAsync(cancellationToken))
            .Where(i => !string.IsNullOrWhiteSpace(i.Assignee))
            .ToDictionary(i => ErrorIssueFingerprint.Key(i.ExceptionType, i.ExceptionMessage), i => i.Assignee);

    public async Task<ErrorIssue?> UpsertAsync(ErrorIssueRequest request, string changedBy, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        // A group's message is its grouped form (ADR-0258, ADR-0259); accept a raw message from API callers too.
        request = request with { ExceptionMessage = (await CanonicalMessagesAsync([(request.ExceptionType, request.ExceptionMessage)], cancellationToken)).GetValueOrDefault((request.ExceptionType, request.ExceptionMessage), request.ExceptionMessage) };
        var id = ErrorIssueFingerprint.Compute(request.ExceptionType, request.ExceptionMessage);
        var existing = await GetStoredAsync(id, cancellationToken);

        var status = request.Status ?? existing?.Status ?? ErrorIssueStatus.Open;
        var assignee = request.Assignee is null ? existing?.Assignee ?? "" : request.Assignee.Trim();
        var issueUrl = request.IssueUrl is null ? existing?.IssueUrl ?? "" : request.IssueUrl.Trim();
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
            IssueUrl = issueUrl,
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

        var cleared = status == ErrorIssueStatus.Open && assignee.Length == 0 && issueUrl.Length == 0;
        if (cleared && existing is null)
        {
            return null;
        }

        await InsertVersionAsync(issue, isDeleted: cleared, cancellationToken);
        return cleared ? null : (await EvaluateAsync([issue], cancellationToken))[0];
    }

    public async Task<string> GetIssueUrlAsync(string exceptionType, string exceptionMessage, CancellationToken cancellationToken)
    {
        var canonical = (await CanonicalMessagesAsync([(exceptionType, exceptionMessage)], cancellationToken)).GetValueOrDefault((exceptionType, exceptionMessage), exceptionMessage);
        return (await GetStoredAsync(ErrorIssueFingerprint.Compute(exceptionType, canonical), cancellationToken))?.IssueUrl ?? "";
    }

    private async Task<IReadOnlyList<ErrorIssue>> EvaluateAsync(IReadOnlyList<ErrorIssue> stored, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        // Only Resolved and count-limited Ignored issues need to look at spans.
        var needEvidence = stored.Where(i => i.Status == ErrorIssueStatus.Resolved || (i.Status == ErrorIssueStatus.Ignored && i.IgnoreUntilOccurrences is not null)).ToList();
        var evidence = new Dictionary<string, List<ErrorIssueEvidence>>();
        if (ErrorIssueEvidenceQueryBuilder.BuildEvidence(needEvidence, await grouping.GetAsync(cancellationToken)) is { } built)
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

    public async Task RekeyAsync(CancellationToken cancellationToken)
    {
        var stored = await ReadStoredAsync(cancellationToken);
        if (stored.Count == 0)
        {
            return;
        }

        var canonical = await CanonicalMessagesAsync(stored.Select(i => (i.ExceptionType, i.ExceptionMessage)).Distinct().ToList(), cancellationToken);
        var newMessages = stored.ToDictionary(i => i.Id, i => canonical.GetValueOrDefault((i.ExceptionType, i.ExceptionMessage), i.ExceptionMessage));
        var plan = ErrorIssueRekey.Plan(stored, newMessages, timeProvider.GetUtcNow());

        var byId = stored.ToDictionary(i => i.Id);
        foreach (var id in plan.Tombstones)
        {
            await InsertVersionAsync(byId[id] with { UpdatedAt = timeProvider.GetUtcNow() }, isDeleted: true, cancellationToken);
        }

        foreach (var issue in plan.Writes)
        {
            await InsertVersionAsync(issue, isDeleted: false, cancellationToken);
        }
    }

    private async Task<Dictionary<(string Type, string Message), string>> CanonicalMessagesAsync(IReadOnlyList<(string Type, string Message)> pairs, CancellationToken cancellationToken)
    {
        var built = ErrorIssueEvidenceQueryBuilder.BuildCanonicalMessages(pairs, await grouping.GetAsync(cancellationToken));
        await using var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, SafetyOptions(), cancellationToken);
        var result = new Dictionary<(string, string), string>();
        while (reader.Read())
        {
            result[(reader.GetString(0), reader.GetString(1))] = reader.GetString(2);
        }

        return result;
    }

    private async Task<IReadOnlyList<string>> ReadKnownVersionsAsync(ErrorIssueRequest request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var built = ErrorIssueEvidenceQueryBuilder.BuildKnownVersions(request.ExceptionType, request.ExceptionMessage, now, await grouping.GetAsync(cancellationToken));
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
        parameters.AddParameter("issueUrl", issue.IssueUrl);

        const string sql = """
            INSERT INTO error_issues
                (Id, ExceptionType, ExceptionMessage, IsDeleted, Status, Assignee, StatusChangedAt, StatusChangedBy, IgnoreUntil, IgnoreUntilOccurrences, KnownVersions, CreatedAt, UpdatedAt, IssueUrl)
            VALUES
                ({id:String}, {exceptionType:String}, {exceptionMessage:String}, {isDeleted:UInt8}, {status:String}, {assignee:String}, {statusChangedAt:DateTime64(3)}, {statusChangedBy:String}, {ignoreUntil:Nullable(DateTime64(3))}, {ignoreUntilOccurrences:Nullable(UInt32)}, {knownVersions:Array(String)}, {createdAt:DateTime64(3)}, {updatedAt:DateTime64(3)}, {issueUrl:String})
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
        IssueUrl = reader.GetString(12),
    };

    private static DateTimeOffset ReadUtc(ClickHouseDataReader reader, int ordinal) =>
        new(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));

    private QueryOptions SafetyOptions() => QuerySafety.Full(queryLimits.Value);
}
