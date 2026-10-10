using ClickHouse.Driver;
using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.ADO.Readers;
using ClickHouse.Driver.Utility;
using Flare.Api.Model;
using Microsoft.Extensions.Options;

namespace Flare.Api.Query;

public interface IReleaseQueryService
{
    /// <summary>Marked releases, newest deploy first; <paramref name="service"/> narrows to one service and adds <see cref="Release.NewErrorCount"/>.</summary>
    Task<IReadOnlyList<Release>> ListAsync(string? service, CancellationToken cancellationToken);

    /// <summary>Creates the release or updates the one already marked for the same service and version.</summary>
    Task<Release> UpsertAsync(ReleaseRequest request, string createdBy, CancellationToken cancellationToken);

    /// <summary>Removes the marker (not telemetry). False when there was none.</summary>
    Task<bool> DeleteAsync(string service, string version, CancellationToken cancellationToken);

    /// <summary>Exception groups first seen under <paramref name="version"/>; null when that release isn't marked.</summary>
    Task<IReadOnlyList<ReleaseNewError>?> ListNewErrorsAsync(string service, string version, CancellationToken cancellationToken);
}

/// <summary>The ClickHouse seam for <c>releases</c> - same CRUD-via-tombstone / <see cref="LatestVersionSql"/> pattern as <see cref="ErrorIssueQueryService"/>.</summary>
public sealed class ReleaseQueryService(IClickHouseClient client, IOptions<QueryLimitsOptions> queryLimits, TimeProvider timeProvider) : IReleaseQueryService
{
    private const string Columns = "Id, ServiceName, Version, CommitSha, Url, Notes, DeployedAt, CreatedBy, CreatedAt";

    public async Task<IReadOnlyList<Release>> ListAsync(string? service, CancellationToken cancellationToken)
    {
        var scoped = new List<string>();
        var parameters = new ClickHouseParameterCollection();
        ServiceScope.Append(scoped, parameters);
        if (!string.IsNullOrEmpty(service))
        {
            parameters.AddParameter("service", service);
            scoped.Add("ServiceName = {service:String}");
        }

        var sql = LatestVersionSql.Select("releases", Columns, latestWhere: scoped.Count == 0 ? null : string.Join(" AND ", scoped), orderBy: "DeployedAt DESC LIMIT 500");
        var releases = await ReadAsync(sql, parameters, cancellationToken);
        if (string.IsNullOrEmpty(service) || releases.Count == 0)
        {
            return releases;
        }

        var counts = new Dictionary<string, long>();
        var built = ReleaseQueryBuilder.BuildNewErrorCounts(service, releases.Min(r => r.DeployedAt));
        await using (var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, SafetyOptions(), cancellationToken))
        {
            while (reader.Read())
            {
                counts[reader.GetString(0)] = (long)reader.GetFieldValue<ulong>(1);
            }
        }

        return releases.Select(r => r with { NewErrorCount = counts.GetValueOrDefault(r.Version) }).ToList();
    }

    public async Task<Release> UpsertAsync(ReleaseRequest request, string createdBy, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var id = ReleaseQueryBuilder.ComputeId(request.Service, request.Version);
        var existing = await GetAsync(id, cancellationToken);
        var release = new Release
        {
            Id = id,
            Service = request.Service,
            Version = request.Version,
            Commit = request.Commit ?? "",
            Url = request.Url ?? "",
            Notes = request.Notes ?? "",
            DeployedAt = request.DeployedAt ?? existing?.DeployedAt ?? now,
            CreatedBy = existing?.CreatedBy is { Length: > 0 } by ? by : createdBy,
            CreatedAt = existing?.CreatedAt ?? now,
        };

        await InsertVersionAsync(release, release.CreatedAt, now, isDeleted: false, cancellationToken);
        return release;
    }

    public async Task<bool> DeleteAsync(string service, string version, CancellationToken cancellationToken)
    {
        var existing = await GetAsync(ReleaseQueryBuilder.ComputeId(service, version), cancellationToken);
        if (existing is null)
        {
            return false;
        }

        await InsertVersionAsync(existing, existing.CreatedAt, timeProvider.GetUtcNow(), isDeleted: true, cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<ReleaseNewError>?> ListNewErrorsAsync(string service, string version, CancellationToken cancellationToken)
    {
        if (!ServiceScope.Allows(service) || await GetAsync(ReleaseQueryBuilder.ComputeId(service, version), cancellationToken) is not { } release)
        {
            return null;
        }

        var built = ReleaseQueryBuilder.BuildNewErrors(service, version, release.DeployedAt);
        await using var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, SafetyOptions(), cancellationToken);
        var errors = new List<ReleaseNewError>();
        while (reader.Read())
        {
            errors.Add(new ReleaseNewError
            {
                ExceptionType = reader.GetString(0),
                ExceptionMessage = reader.GetString(1),
                Occurrences = (long)reader.GetFieldValue<ulong>(2),
                FirstSeenUnixMs = ToUnixMs(reader.GetDateTime(3)),
                LastSeenUnixMs = ToUnixMs(reader.GetDateTime(4)),
            });
        }

        return errors;
    }

    private async Task<Release?> GetAsync(string id, CancellationToken cancellationToken)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("id", id);
        var sql = LatestVersionSql.Select("releases", Columns, idWhere: "Id = {id:String}");
        return (await ReadAsync(sql, parameters, cancellationToken)).FirstOrDefault();
    }

    private async Task<List<Release>> ReadAsync(string sql, ClickHouseParameterCollection parameters, CancellationToken cancellationToken)
    {
        await using var reader = await client.ExecuteReaderAsync(sql, parameters, SafetyOptions(), cancellationToken);
        var releases = new List<Release>();
        while (reader.Read())
        {
            releases.Add(ReadRelease(reader));
        }

        return releases;
    }

    private async Task InsertVersionAsync(Release release, DateTimeOffset createdAt, DateTimeOffset updatedAt, bool isDeleted, CancellationToken cancellationToken)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("id", release.Id);
        parameters.AddParameter("service", release.Service);
        parameters.AddParameter("version", release.Version);
        parameters.AddParameter("isDeleted", isDeleted ? (byte)1 : (byte)0);
        parameters.AddParameter("commit", release.Commit);
        parameters.AddParameter("url", release.Url);
        parameters.AddParameter("notes", release.Notes);
        parameters.AddParameter("deployedAt", release.DeployedAt.UtcDateTime);
        parameters.AddParameter("createdBy", release.CreatedBy);
        parameters.AddParameter("createdAt", createdAt.UtcDateTime);
        parameters.AddParameter("updatedAt", updatedAt.UtcDateTime);

        const string sql = """
            INSERT INTO releases
                (Id, ServiceName, Version, IsDeleted, CommitSha, Url, Notes, DeployedAt, CreatedBy, CreatedAt, UpdatedAt)
            VALUES
                ({id:String}, {service:String}, {version:String}, {isDeleted:UInt8}, {commit:String}, {url:String}, {notes:String}, {deployedAt:DateTime64(3)}, {createdBy:String}, {createdAt:DateTime64(3)}, {updatedAt:DateTime64(3)})
            """;

        await client.ExecuteNonQueryAsync(sql, parameters, SafetyOptions(), cancellationToken);
    }

    private static Release ReadRelease(ClickHouseDataReader reader) => new()
    {
        Id = reader.GetString(0),
        Service = reader.GetString(1),
        Version = reader.GetString(2),
        Commit = reader.GetString(3),
        Url = reader.GetString(4),
        Notes = reader.GetString(5),
        DeployedAt = ReadUtc(reader, 6),
        CreatedBy = reader.GetString(7),
        CreatedAt = ReadUtc(reader, 8),
    };

    private static DateTimeOffset ReadUtc(ClickHouseDataReader reader, int ordinal) =>
        new(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));

    private static long ToUnixMs(DateTime utc) => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();

    private QueryOptions SafetyOptions() => QuerySafety.Full(queryLimits.Value);
}
