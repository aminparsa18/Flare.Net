using System.Text.Json;
using ClickHouse.Driver;
using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.ADO.Readers;
using ClickHouse.Driver.Utility;
using Flare.Api.Model;

namespace Flare.Api.Query;

public interface IDashboardQueryService
{
    Task<Dashboard> CreateAsync(DashboardRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<Dashboard>> ListAsync(CancellationToken cancellationToken);

    Task<Dashboard?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<Dashboard?> UpdateAsync(Guid id, DashboardRequest request, CancellationToken cancellationToken);

    /// <summary>Soft-deletes (inserts a tombstone version) - see 0020_dashboards.sql. Returns false if <paramref name="id"/> doesn't exist.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>
/// The ClickHouse seam for dashboard CRUD - mirrors <see cref="SavedViewQueryService"/>'s
/// role (down to the tombstone-versioning and <c>FINAL</c>-read mechanics) against
/// <c>dashboards</c> instead of <c>saved_views</c>. See
/// <c>docs-internal/adr/0023-custom-dashboards.md</c> for why a dashboard is its own
/// table rather than a <see cref="SavedView"/> page type, and why <see cref="Dashboard.LayoutJson"/>
/// is never deserialized here: panel execution is entirely client-driven against the
/// existing per-domain query endpoints, so this service - like
/// <see cref="SavedViewQueryService"/> - only ever round-trips it as opaque text.
/// </summary>
/// <remarks>
/// <c>dashboards</c> is a <c>ReplacingMergeTree</c>, not a plain <c>MergeTree</c> like
/// <c>logs</c> - every create/update here INSERTs a brand-new row for the same
/// <see cref="Dashboard.Id"/> rather than mutating in place, same reasoning
/// <see cref="SavedViewQueryService"/>'s own remarks give. All reads go through <c>FROM
/// dashboards FINAL WHERE IsDeleted = 0</c>. See db/clickhouse/0020_dashboards.sql for the
/// full rationale.
/// </remarks>
public sealed class DashboardQueryService(IClickHouseClient client, TimeProvider timeProvider) : IDashboardQueryService
{
    private const string DashboardColumns = "Id, Name, Description, LayoutJson, CreatedAt, UpdatedAt";

    public async Task<Dashboard> CreateAsync(DashboardRequest request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var dashboard = new Dashboard
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Description = request.Description ?? "",
            LayoutJson = request.LayoutJson,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await InsertDashboardVersionAsync(dashboard, isDeleted: false, cancellationToken);
        return dashboard;
    }

    public async Task<IReadOnlyList<Dashboard>> ListAsync(CancellationToken cancellationToken)
    {
        var sql = $"SELECT {DashboardColumns} FROM dashboards FINAL WHERE IsDeleted = 0 ORDER BY Name";
        await using var reader = await client.ExecuteReaderAsync(sql, null, SafetyOptions(), cancellationToken);
        return ReadDashboards(reader);
    }

    public async Task<Dashboard?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("id", id);
        var sql = $"SELECT {DashboardColumns} FROM dashboards FINAL WHERE Id = {{id:UUID}} AND IsDeleted = 0";
        await using var reader = await client.ExecuteReaderAsync(sql, parameters, SafetyOptions(), cancellationToken);
        return reader.Read() ? ReadDashboard(reader) : null;
    }

    public async Task<Dashboard?> UpdateAsync(Guid id, DashboardRequest request, CancellationToken cancellationToken)
    {
        var existing = await GetAsync(id, cancellationToken);
        if (existing is null)
        {
            return null;
        }

        var updated = existing with
        {
            Name = request.Name,
            Description = request.Description ?? "",
            LayoutJson = request.LayoutJson,
            UpdatedAt = timeProvider.GetUtcNow(),
        };

        await InsertDashboardVersionAsync(updated, isDeleted: false, cancellationToken);
        return updated;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var existing = await GetAsync(id, cancellationToken);
        if (existing is null)
        {
            return false;
        }

        var tombstone = existing with { UpdatedAt = timeProvider.GetUtcNow() };
        await InsertDashboardVersionAsync(tombstone, isDeleted: true, cancellationToken);
        return true;
    }

    private async Task InsertDashboardVersionAsync(Dashboard dashboard, bool isDeleted, CancellationToken cancellationToken)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("id", dashboard.Id);
        parameters.AddParameter("name", dashboard.Name);
        parameters.AddParameter("description", dashboard.Description);
        parameters.AddParameter("isDeleted", isDeleted ? (byte)1 : (byte)0);
        // GetRawText(), not JsonSerializer.Serialize(dashboard.LayoutJson) - LayoutJson is
        // already a parsed JsonElement (the request body's own "layoutJson" property), so
        // re-serializing it would just reproduce the same text via a slower path. Same
        // convention as SavedViewQueryService's stateJson parameter.
        parameters.AddParameter("layoutJson", dashboard.LayoutJson.GetRawText());
        parameters.AddParameter("createdAt", dashboard.CreatedAt.UtcDateTime);
        parameters.AddParameter("updatedAt", dashboard.UpdatedAt.UtcDateTime);

        const string sql = """
            INSERT INTO dashboards
                (Id, Name, Description, IsDeleted, LayoutJson, CreatedAt, UpdatedAt)
            VALUES
                ({id:UUID}, {name:String}, {description:String}, {isDeleted:UInt8}, {layoutJson:String}, {createdAt:DateTime64(3)}, {updatedAt:DateTime64(3)})
            """;

        await client.ExecuteNonQueryAsync(sql, parameters, SafetyOptions(), cancellationToken);
    }

    private static List<Dashboard> ReadDashboards(ClickHouseDataReader reader)
    {
        var dashboards = new List<Dashboard>();
        while (reader.Read())
        {
            dashboards.Add(ReadDashboard(reader));
        }

        return dashboards;
    }

    private static Dashboard ReadDashboard(ClickHouseDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        Name = reader.GetString(1),
        Description = reader.GetString(2),
        LayoutJson = JsonDocument.Parse(reader.GetString(3)).RootElement,
        CreatedAt = ReadUtc(reader, 4),
        UpdatedAt = ReadUtc(reader, 5),
    };

    /// <summary>See <see cref="LogQueryService"/>'s identical helper's remarks - same <c>DateTime64</c>/<c>Kind=Unspecified</c> driver behavior applies here.</summary>
    private static DateTimeOffset ReadUtc(ClickHouseDataReader reader, int ordinal) =>
        new(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));

    /// <summary>Same query-safety rationale as <see cref="LogQueryService.SafetyOptions"/>, used here for dashboard CRUD.</summary>
    private static QueryOptions SafetyOptions() => new()
    {
        CustomSettings = new Dictionary<string, object>
        {
            ["max_execution_time"] = 30,
            ["timeout_before_checking_execution_speed"] = 0,
            ["max_rows_to_read"] = 1_000_000_000,
            ["max_result_rows"] = 10_000,
            ["result_overflow_mode"] = "break",
        },
    };
}
