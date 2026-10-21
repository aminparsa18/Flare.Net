using System.Security.Cryptography;
using System.Text;
using ClickHouse.Driver;
using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Flare.Ingest.Pipeline;

public enum CheckInProvisionResult
{
    /// <summary>A live monitor already had the slug; nothing was written.</summary>
    Exists,

    Created,

    /// <summary><see cref="CheckInIngestOptions.MaxMonitors"/> live monitors exist already.</summary>
    LimitReached,
}

public interface IClickHouseCheckInProvisioner
{
    /// <summary>Creates a monitor for <paramref name="slug"/> from <paramref name="spec"/> unless a live one exists. Never changes an existing monitor.</summary>
    Task<CheckInProvisionResult> EnsureAsync(string slug, CheckInAutoCreate spec, CancellationToken cancellationToken = default);
}

/// <summary>
/// Auto-creates check-in monitors (ADR-0262) by inserting a <c>checkin_monitors</c> version row, as <c>Flare.Api</c> does. The id
/// is derived from the slug, so two pings racing to create it (or two Ingest replicas) write the same row and
/// <c>ReplacingMergeTree</c> keeps one. A slug seen to exist is remembered for <see cref="KnownFor"/>, so a job that sends
/// <c>create=1</c> on every ping costs one read per interval, not one per ping.
/// </summary>
public sealed class ClickHouseCheckInProvisioner(IClickHouseClient client, IMemoryCache cache, IOptions<CheckInIngestOptions> options, TimeProvider timeProvider) : IClickHouseCheckInProvisioner
{
    internal static readonly TimeSpan KnownFor = TimeSpan.FromMinutes(5);

    private const string ExistsSql = """
        SELECT countIf(Slug = {slug:String}) AS SameSlug, count() AS Total
        FROM (SELECT Slug, IsDeleted FROM checkin_monitors ORDER BY UpdatedAt DESC LIMIT 1 BY Id)
        WHERE IsDeleted = 0
        """;

    private const string InsertSql = """
        INSERT INTO checkin_monitors
            (Id, Slug, Name, Description, IsDeleted, Enabled, CronExpression, IntervalSeconds, TimeZone, GraceSeconds, MaxRuntimeSeconds, CreatedAt, UpdatedAt)
        VALUES
            ({id:UUID}, {slug:String}, {name:String}, '', 0, 1, {cron:String}, {intervalSeconds:UInt32}, {timeZone:String}, {graceSeconds:UInt32}, {maxRuntimeSeconds:UInt32}, {now:DateTime64(3)}, {now:DateTime64(3)})
        """;

    /// <summary>The same id for the same slug, on every replica.</summary>
    public static Guid IdFor(string slug) => new(MD5.HashData(Encoding.UTF8.GetBytes("flare:checkin-monitor:" + slug)));

    public async Task<CheckInProvisionResult> EnsureAsync(string slug, CheckInAutoCreate spec, CancellationToken cancellationToken = default)
    {
        var key = "checkin-monitor:" + slug;
        if (cache.TryGetValue(key, out _))
        {
            return CheckInProvisionResult.Exists;
        }

        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("slug", slug);
        await using (var reader = await client.ExecuteReaderAsync(ExistsSql, parameters, SafetyOptions(), cancellationToken))
        {
            reader.Read();
            if (reader.GetFieldValue<ulong>(0) > 0)
            {
                cache.Set(key, true, KnownFor);
                return CheckInProvisionResult.Exists;
            }

            if (reader.GetFieldValue<ulong>(1) >= (ulong)options.Value.MaxMonitors)
            {
                return CheckInProvisionResult.LimitReached;
            }
        }

        var insert = new ClickHouseParameterCollection();
        insert.AddParameter("id", IdFor(slug));
        insert.AddParameter("slug", slug);
        insert.AddParameter("name", spec.Name);
        insert.AddParameter("cron", spec.CronExpression);
        insert.AddParameter("intervalSeconds", (uint)spec.IntervalSeconds);
        insert.AddParameter("timeZone", spec.TimeZone);
        insert.AddParameter("graceSeconds", (uint)spec.GraceSeconds);
        insert.AddParameter("maxRuntimeSeconds", (uint)spec.MaxRuntimeSeconds);
        insert.AddParameter("now", timeProvider.GetUtcNow().UtcDateTime);
        await client.ExecuteNonQueryAsync(InsertSql, insert, SafetyOptions(), cancellationToken);

        cache.Set(key, true, KnownFor);
        return CheckInProvisionResult.Created;
    }

    private static QueryOptions SafetyOptions() => new()
    {
        CustomSettings = new Dictionary<string, object>
        {
            ["max_execution_time"] = 10,
            ["timeout_before_checking_execution_speed"] = 0,
            ["max_rows_to_read"] = 1_000_000,
            ["max_result_rows"] = 10,
            ["result_overflow_mode"] = "break",
        },
    };
}
