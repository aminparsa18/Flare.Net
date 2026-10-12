using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>
/// The pure, ClickHouse-free half of the Usage page (same split as <c>IngestionStatsQueryService</c>'s
/// static shapers): clamping, table-name normalisation and the per-service byte apportioning.
/// </summary>
public static class UsageShaper
{
    public const int DefaultDays = 7;
    public const int MaxDays = 30;
    public const int TopServicesPerSignal = 50;
    public const int TopAttributesPerGroup = 15;

    public static int ClampDays(int requested) => Math.Clamp(requested <= 0 ? DefaultDays : requested, 1, MaxDays);

    /// <summary>Cluster mode reports the real storage under <c>*_local</c>; the Distributed front table is empty.</summary>
    public static string NormalizeTableName(string tableName) =>
        tableName.EndsWith("_local", StringComparison.Ordinal) ? tableName[..^"_local".Length] : tableName;

    /// <summary>
    /// Apportions <paramref name="signalCompressedBytes"/> across services by event share,
    /// biggest first, capped to <see cref="TopServicesPerSignal"/> rows.
    /// </summary>
    public static IReadOnlyList<UsageServiceRow> BuildServiceRows(
        string signal,
        IReadOnlyDictionary<string, long> eventsByService,
        long signalCompressedBytes)
    {
        var total = eventsByService.Values.Sum();
        if (total <= 0)
        {
            return [];
        }

        return eventsByService
            .Where(kv => kv.Value > 0)
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Take(TopServicesPerSignal)
            .Select(kv => new UsageServiceRow(
                kv.Key,
                signal,
                kv.Value,
                (long)Math.Round((double)signalCompressedBytes * kv.Value / total)))
            .ToList();
    }

    /// <summary>Keeps the <see cref="TopAttributesPerGroup"/> biggest keys per (signal, scope), biggest first.</summary>
    public static IReadOnlyList<UsageAttributeRow> TopAttributes(IEnumerable<UsageAttributeRow> rows) =>
        rows.GroupBy(r => (r.Signal, r.Scope))
            .SelectMany(g => g.OrderByDescending(r => r.SampledBytes).ThenBy(r => r.Key, StringComparer.Ordinal).Take(TopAttributesPerGroup))
            .ToList();
}
