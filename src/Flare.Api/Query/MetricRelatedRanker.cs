using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>One metric's name, type, and the service/attribute-key sets it was seen with - a row of <see cref="MetricCatalogQueryBuilder.BuildRelatedCandidates"/>.</summary>
public sealed record MetricRelationCandidate(
    string MetricName,
    MetricPointType Type,
    IReadOnlyCollection<string> Services,
    IReadOnlyCollection<string> AttributeKeys);

/// <summary>
/// Pure ranking of "related metrics" for the catalog's detail view: which other metrics a
/// person looking at this one most likely wants next.
/// </summary>
/// <remarks>
/// <para>
/// Three signals, strongest first: a shared leading name prefix (OTel semantic conventions
/// namespace related instruments - <c>http.server.request.duration</c> and
/// <c>http.server.active_requests</c>), shared attribute keys (instruments recorded against
/// the same dimensions), and shared emitting services. Scored as
/// <c>3 × prefix segments + 2 × shared keys + shared services</c>.
/// </para>
/// <para>
/// A candidate needs a shared prefix or a shared key to qualify at all - sharing only a
/// service would make every metric of a busy service "related" to every other one.
/// </para>
/// </remarks>
public static class MetricRelatedRanker
{
    private static readonly char[] SegmentSeparators = ['.', '_'];

    public static IReadOnlyList<MetricCatalogRelatedMetric> Rank(
        MetricRelationCandidate target,
        IEnumerable<MetricRelationCandidate> candidates,
        int limit)
    {
        var targetKeys = target.AttributeKeys.ToHashSet(StringComparer.Ordinal);
        var targetServices = target.Services.ToHashSet(StringComparer.Ordinal);

        return candidates
            .Where(c => !(c.MetricName == target.MetricName && c.Type == target.Type))
            .Select(c =>
            {
                var (prefix, segments) = SharedPrefix(target.MetricName, c.MetricName);
                var sharedKeys = c.AttributeKeys.Count(targetKeys.Contains);
                var sharedServices = c.Services.Count(targetServices.Contains);
                var related = new MetricCatalogRelatedMetric
                {
                    MetricName = c.MetricName,
                    Type = c.Type,
                    SharedNamePrefix = prefix,
                    SharedAttributeKeyCount = sharedKeys,
                    SharedServiceCount = sharedServices,
                };
                return (Related: related, Qualifies: segments > 0 || sharedKeys > 0, Score: (3 * segments) + (2 * sharedKeys) + sharedServices);
            })
            .Where(r => r.Qualifies)
            .OrderByDescending(r => r.Score)
            .ThenBy(r => r.Related.MetricName, StringComparer.Ordinal)
            .Take(limit)
            .Select(r => r.Related)
            .ToList();
    }

    /// <summary>
    /// The whole leading segments two names share (split on <c>.</c> and <c>_</c>, so both OTel
    /// and Prometheus-style names work), as a prefix of <paramref name="a"/> and a segment count.
    /// A partial segment never counts: <c>http.server</c> and <c>http.service</c> share only <c>http</c>.
    /// </summary>
    internal static (string? Prefix, int Segments) SharedPrefix(string a, string b)
    {
        var segments = 0;
        var end = 0;
        var i = 0;
        while (true)
        {
            var nextA = a.IndexOfAny(SegmentSeparators, i);
            var nextB = b.IndexOfAny(SegmentSeparators, i);
            var segmentEndA = nextA < 0 ? a.Length : nextA;
            var segmentEndB = nextB < 0 ? b.Length : nextB;
            if (segmentEndA != segmentEndB || segmentEndA == i || string.CompareOrdinal(a, i, b, i, segmentEndA - i) != 0)
            {
                break;
            }

            // Both names ending here means they're identical up to this point - the same name,
            // only possible across types. Counts as fully shared.
            segments++;
            end = segmentEndA;
            if (nextA < 0 || nextB < 0 || a[nextA] != b[nextB])
            {
                break;
            }

            i = segmentEndA + 1;
        }

        return segments == 0 ? (null, 0) : (a[..end], segments);
    }
}
