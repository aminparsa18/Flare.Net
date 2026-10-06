using Flare.Ingest.Model;

namespace Flare.Ingest.Pipeline;

/// <summary>
/// Pure <see cref="ProfileSampleRecord"/> → ClickHouse row mapping for <c>profile_samples</c>
/// (<c>db/clickhouse/0065_profile_samples.sql</c>), no ClickHouse dependency, same style as
/// <see cref="ClickHouseSpanRowMapper"/>.
/// </summary>
public static class ClickHouseProfileRowMapper
{
    /// <summary>
    /// Column order every row agrees on, matching the DDL's declaration order. <c>StackHash</c> is
    /// <c>MATERIALIZED</c> in the DDL, so it's computed by ClickHouse and not part of the insert.
    /// </summary>
    public static readonly IReadOnlyList<string> Columns =
    [
        "Timestamp",
        "ProfileId",
        "DurationNano",
        "ServiceName",
        "SampleType",
        "SampleUnit",
        "Stack",
        "Value",
        "TraceId",
        "SpanId",
        "ResourceAttributes",
        "SampleAttributes",
        "IngestedAt",
    ];

    public static object[] ToRow(ProfileSampleRecord sample) =>
    [
        sample.Timestamp.UtcDateTime,
        sample.ProfileId ?? string.Empty,
        sample.DurationNano,
        sample.ServiceName ?? string.Empty,
        sample.SampleType ?? string.Empty,
        sample.SampleUnit ?? string.Empty,
        sample.Stack.ToArray(),
        sample.Value,
        sample.TraceId ?? string.Empty,
        sample.SpanId ?? string.Empty,
        new Dictionary<string, string>(sample.ResourceAttributes),
        new Dictionary<string, string>(sample.SampleAttributes),
        sample.IngestedAt.UtcDateTime,
    ];

    public static IReadOnlyList<object[]> ToRows(IReadOnlyList<ProfileSampleRecord> samples)
    {
        var rows = new object[samples.Count][];
        for (var i = 0; i < samples.Count; i++)
        {
            rows[i] = ToRow(samples[i]);
        }
        return rows;
    }
}
