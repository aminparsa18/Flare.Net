using ClickHouse.Driver;
using Flare.Api.Model;
using Microsoft.Extensions.Options;

namespace Flare.Api.Query;

public interface INPlusOneQueryService
{
    Task<NPlusOneResponse> GetOffendersAsync(NPlusOneRequest request, CancellationToken cancellationToken);
}

/// <summary>The one component holding an <see cref="IClickHouseClient"/> for N+1 detection - see <see cref="NPlusOneQueryBuilder"/> for the SQL.</summary>
public sealed class NPlusOneQueryService(IClickHouseClient client, IOptions<QueryLimitsOptions> queryLimits, TimeProvider timeProvider) : INPlusOneQueryService
{
    public async Task<NPlusOneResponse> GetOffendersAsync(NPlusOneRequest request, CancellationToken cancellationToken)
    {
        var windowMinutes = NPlusOneQueryBuilder.ClampWindowMinutes(request.WindowMinutes);
        var minRepeats = NPlusOneQueryBuilder.ClampMinRepeats(request.MinRepeats);
        var end = HostInventoryQueryBuilder.ResolveWindowEnd(request.EndUnixMs, timeProvider.GetUtcNow());

        var built = NPlusOneQueryBuilder.Build(request, windowMinutes, minRepeats, end);
        var offenders = new List<NPlusOneOffender>();
        await using var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, QuerySafety.Full(queryLimits.Value), cancellationToken);
        while (reader.Read())
        {
            offenders.Add(new NPlusOneOffender
            {
                ServiceName = reader.GetString(0),
                Statement = reader.GetString(1),
                TraceCount = reader.GetFieldValue<ulong>(2),
                MaxRepeats = reader.GetFieldValue<ulong>(3),
                TotalRepeats = reader.GetFieldValue<ulong>(4),
                TotalDurationMs = reader.GetDouble(5),
                ExampleTraceId = reader.GetString(6),
            });
        }

        return new NPlusOneResponse { WindowMinutes = windowMinutes, MinRepeats = minRepeats, Offenders = offenders };
    }
}
