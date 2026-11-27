using ClickHouse.Driver;

namespace Flare.Ingest.Pipeline;

/// <summary>One check-in ping, ready to insert into <c>checkin_events</c> (<c>db/clickhouse/0089_checkin_monitors.sql</c>).</summary>
public sealed record CheckInRecord(DateTimeOffset Time, string Slug, string Status, string Message);

public interface IClickHouseCheckInWriter
{
    Task WriteAsync(CheckInRecord checkIn, CancellationToken cancellationToken = default);
}

/// <summary>Inserts directly, with no Redis stream: one small row per ping, and a ping is worthless late, like feedback (ADR-0193).</summary>
public sealed class ClickHouseCheckInWriter(IClickHouseClient client, ClickHouseInsertColumnTypes columnTypes) : IClickHouseCheckInWriter
{
    private static readonly IReadOnlyList<string> Columns = ["Slug", "Time", "Status", "Message"];

    public async Task WriteAsync(CheckInRecord c, CancellationToken cancellationToken = default) =>
        await client.InsertBinaryAsync(
            "checkin_events",
            Columns,
            [[c.Slug, c.Time.UtcDateTime, c.Status, c.Message]],
            await columnTypes.OptionsAsync("checkin_events", Columns, cancellationToken: cancellationToken),
            cancellationToken);
}
