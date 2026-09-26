using ClickHouse.Driver.ADO.Readers;

namespace Flare.Api.Query;

/// <summary>
/// Reads one <see cref="HistogramTemporalitySql.ExponentialAggregates"/> column block
/// into an <see cref="ExponentialHistogramBuckets"/> slice - shared by
/// <see cref="MetricQueryService"/> and <see cref="AlertQueryService"/>, whose queries place the
/// block at different ordinals.
/// </summary>
internal static class ExponentialHistogramRowReader
{
    public static ExponentialHistogramBuckets Read(ClickHouseDataReader reader, int firstOrdinal) => new()
    {
        Count = reader.GetFieldValue<long>(firstOrdinal),
        Sum = reader.GetFieldValue<double>(firstOrdinal + 1),
        Scale = reader.GetFieldValue<int>(firstOrdinal + 2),
        ZeroCount = reader.GetFieldValue<long>(firstOrdinal + 3),
        ZeroThreshold = reader.GetFieldValue<double>(firstOrdinal + 4),
        PositiveIndices = reader.GetFieldValue<int[]>(firstOrdinal + 5),
        PositiveCounts = reader.GetFieldValue<long[]>(firstOrdinal + 6),
        NegativeIndices = reader.GetFieldValue<int[]>(firstOrdinal + 7),
        NegativeCounts = reader.GetFieldValue<long[]>(firstOrdinal + 8),
        Min = reader.IsDBNull(firstOrdinal + 9) ? null : reader.GetFieldValue<double>(firstOrdinal + 9),
        Max = reader.IsDBNull(firstOrdinal + 10) ? null : reader.GetFieldValue<double>(firstOrdinal + 10),
    };

    /// <summary>Number of columns <see cref="Read"/> consumes.</summary>
    public const int ColumnCount = 11;
}
