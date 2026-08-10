using System.Text.Json;
using ClickHouse.Driver;
using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.ADO.Readers;
using ClickHouse.Driver.Utility;
using Flare.Api.Model;

namespace Flare.Api.Query;

public interface ISavedViewQueryService
{
    Task<SavedView> CreateAsync(SavedViewRequest request, CancellationToken cancellationToken);

    /// <summary>Null <paramref name="pageType"/> lists every view (the <c>/views</c> management page); a value scopes it to one dashboard page (a toolbar's own view picker).</summary>
    Task<IReadOnlyList<SavedView>> ListAsync(SavedViewPageType? pageType, CancellationToken cancellationToken);

    Task<SavedView?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<SavedView?> UpdateAsync(Guid id, SavedViewRequest request, CancellationToken cancellationToken);

    /// <summary>Soft-deletes (inserts a tombstone version) - see 0009_saved_views.sql. Returns false if <paramref name="id"/> doesn't exist.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>
/// The ClickHouse seam for saved-view CRUD - mirrors <see cref="AlertQueryService"/>'s role
/// (the one place in this concern that holds an <see cref="IClickHouseClient"/>), against
/// <c>saved_views</c> instead of <c>alert_rules</c>. No evaluation/history counterpart
/// exists here - saved views have no hot per-tick read path the way alert rules do.
/// </summary>
/// <remarks>
/// <c>saved_views</c> is a <c>ReplacingMergeTree</c>, not a plain <c>MergeTree</c> like
/// <c>logs</c> - every create/update here INSERTs a brand-new row for the same
/// <see cref="SavedView.Id"/> rather than mutating in place, same reasoning
/// <see cref="AlertQueryService"/>'s own remarks give. All reads go through <c>FROM
/// saved_views FINAL WHERE IsDeleted = 0</c>. See db/clickhouse/0009_saved_views.sql for
/// the full rationale.
/// </remarks>
public sealed class SavedViewQueryService(IClickHouseClient client, TimeProvider timeProvider) : ISavedViewQueryService
{
    private const string ViewColumns = "Id, Name, Description, PageType, StateJson, CreatedAt, UpdatedAt";

    public async Task<SavedView> CreateAsync(SavedViewRequest request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var view = new SavedView
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Description = request.Description ?? "",
            PageType = request.PageType,
            State = request.State,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await InsertViewVersionAsync(view, isDeleted: false, cancellationToken);
        return view;
    }

    public async Task<IReadOnlyList<SavedView>> ListAsync(SavedViewPageType? pageType, CancellationToken cancellationToken)
    {
        if (pageType is null)
        {
            var sql = $"SELECT {ViewColumns} FROM saved_views FINAL WHERE IsDeleted = 0 ORDER BY Name";
            await using var reader = await client.ExecuteReaderAsync(sql, null, SafetyOptions(), cancellationToken);
            return ReadViews(reader);
        }
        else
        {
            var parameters = new ClickHouseParameterCollection();
            parameters.AddParameter("pageType", pageType.Value.ToString());
            var sql = $"SELECT {ViewColumns} FROM saved_views FINAL WHERE IsDeleted = 0 AND PageType = {{pageType:String}} ORDER BY Name";
            await using var reader = await client.ExecuteReaderAsync(sql, parameters, SafetyOptions(), cancellationToken);
            return ReadViews(reader);
        }
    }

    public async Task<SavedView?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("id", id);
        var sql = $"SELECT {ViewColumns} FROM saved_views FINAL WHERE Id = {{id:UUID}} AND IsDeleted = 0";
        await using var reader = await client.ExecuteReaderAsync(sql, parameters, SafetyOptions(), cancellationToken);
        return reader.Read() ? ReadView(reader) : null;
    }

    public async Task<SavedView?> UpdateAsync(Guid id, SavedViewRequest request, CancellationToken cancellationToken)
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
            PageType = request.PageType,
            State = request.State,
            UpdatedAt = timeProvider.GetUtcNow(),
        };

        await InsertViewVersionAsync(updated, isDeleted: false, cancellationToken);
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
        await InsertViewVersionAsync(tombstone, isDeleted: true, cancellationToken);
        return true;
    }

    private async Task InsertViewVersionAsync(SavedView view, bool isDeleted, CancellationToken cancellationToken)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("id", view.Id);
        parameters.AddParameter("name", view.Name);
        parameters.AddParameter("description", view.Description);
        parameters.AddParameter("isDeleted", isDeleted ? (byte)1 : (byte)0);
        parameters.AddParameter("pageType", view.PageType.ToString());
        // GetRawText(), not JsonSerializer.Serialize(view.State) - State is already a
        // parsed JsonElement (the request body's own "state" property), so re-serializing
        // it would just reproduce the same text via a slower path.
        parameters.AddParameter("stateJson", view.State.GetRawText());
        parameters.AddParameter("createdAt", view.CreatedAt.UtcDateTime);
        parameters.AddParameter("updatedAt", view.UpdatedAt.UtcDateTime);

        const string sql = """
            INSERT INTO saved_views
                (Id, Name, Description, IsDeleted, PageType, StateJson, CreatedAt, UpdatedAt)
            VALUES
                ({id:UUID}, {name:String}, {description:String}, {isDeleted:UInt8}, {pageType:String}, {stateJson:String}, {createdAt:DateTime64(3)}, {updatedAt:DateTime64(3)})
            """;

        await client.ExecuteNonQueryAsync(sql, parameters, SafetyOptions(), cancellationToken);
    }

    private static List<SavedView> ReadViews(ClickHouseDataReader reader)
    {
        var views = new List<SavedView>();
        while (reader.Read())
        {
            views.Add(ReadView(reader));
        }

        return views;
    }

    private static SavedView ReadView(ClickHouseDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        Name = reader.GetString(1),
        Description = reader.GetString(2),
        PageType = Enum.Parse<SavedViewPageType>(reader.GetString(3)),
        State = JsonDocument.Parse(reader.GetString(4)).RootElement,
        CreatedAt = ReadUtc(reader, 5),
        UpdatedAt = ReadUtc(reader, 6),
    };

    /// <summary>See <see cref="LogQueryService"/>'s identical helper's remarks - same <c>DateTime64</c>/<c>Kind=Unspecified</c> driver behavior applies here.</summary>
    private static DateTimeOffset ReadUtc(ClickHouseDataReader reader, int ordinal) =>
        new(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));

    /// <summary>Same query-safety rationale as <see cref="LogQueryService.SafetyOptions"/>, used here for view CRUD.</summary>
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
