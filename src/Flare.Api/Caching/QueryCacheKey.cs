using System.Security.Cryptography;
using MemoryPack;

namespace Flare.Api.Caching;

/// <summary>
/// Builds a stable Redis key for one cached query-service call. Every Flare.Api request
/// DTO is already <c>[MemoryPackable]</c> for the dashboard wire format (see
/// <c>Json.ApiSerialization</c>), so hashing that same encoding gives two requests with
/// identical filter/time-range/paging fields the same key without hand-listing each field
/// here - and a field this type gains later is covered automatically, not silently ignored.
/// </summary>
public static class QueryCacheKey
{
    public static string Build<TRequest>(string endpoint, TRequest request)
    {
        var encoded = MemoryPackSerializer.Serialize(request);
        var hash = SHA256.HashData(encoded);
        return $"flare:qcache:{endpoint}:{Convert.ToHexString(hash)}";
    }
}
