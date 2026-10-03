using ClickHouse.Driver;
using Flare.Api.Model;
using Flare.Identity.LlmPrices;
using Microsoft.Extensions.Options;

namespace Flare.Api.Query;

public interface ILlmQueryService
{
    Task<LlmModelsResponse> GetModelsAsync(LlmModelsRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// The one component holding an <see cref="IClickHouseClient"/> for the <c>/llm</c> page - see
/// <see cref="LlmQueryBuilder"/> for the SQL. Same <c>ExecuteReaderAsync</c> + ordinal-read
/// style as <see cref="ExternalApiQueryService"/>.
/// </summary>
public sealed class LlmQueryService(IClickHouseClient client, IOptions<QueryLimitsOptions> queryLimits, ILlmModelPriceStore priceStore, TimeProvider timeProvider) : ILlmQueryService
{
    public async Task<LlmModelsResponse> GetModelsAsync(LlmModelsRequest request, CancellationToken cancellationToken)
    {
        var windowMinutes = LlmQueryBuilder.ClampWindowMinutes(request.WindowMinutes);
        var end = LlmQueryBuilder.ResolveWindowEnd(request.EndUnixMs, timeProvider.GetUtcNow());
        var seconds = windowMinutes * 60.0;

        var models = new List<LlmModel>();
        var built = LlmQueryBuilder.BuildModels(request, windowMinutes, end);
        await using (var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, SafetyOptions(), cancellationToken))
        {
            while (reader.Read() && models.Count < LlmQueryBuilder.MaxRows)
            {
                var callCount = reader.GetFieldValue<ulong>(2);
                var quantiles = reader.GetFieldValue<double[]>(4);
                models.Add(new LlmModel
                {
                    Provider = reader.GetString(0),
                    Model = reader.GetString(1),
                    CallCount = callCount,
                    ErrorCount = reader.GetFieldValue<ulong>(3),
                    PerSecond = callCount / seconds,
                    P50Ms = NanosToMs(quantiles, 0),
                    P95Ms = NanosToMs(quantiles, 1),
                    P99Ms = NanosToMs(quantiles, 2),
                    InputTokens = reader.GetFieldValue<ulong>(5),
                    OutputTokens = reader.GetFieldValue<ulong>(6),
                    ServiceCount = reader.GetFieldValue<ulong>(7),
                    LastSeenUnixMs = reader.GetFieldValue<long>(8),
                });
            }
        }

        string[] services = [];
        var facetsSql = LlmQueryBuilder.BuildFacets(windowMinutes, end);
        await using (var reader = await client.ExecuteReaderAsync(facetsSql.Sql, facetsSql.Parameters, SafetyOptions(), cancellationToken))
        {
            if (reader.Read())
            {
                services = reader.GetFieldValue<string[]>(0);
            }
        }

        return new LlmModelsResponse
        {
            WindowMinutes = windowMinutes,
            Models = LlmPricing.Apply(models, await priceStore.GetAllAsync(cancellationToken)),
            Services = services,
        };
    }

    /// <summary>Same <c>nan</c>-guard as <see cref="ExternalApiQueryService"/>'s.</summary>
    private static double NanosToMs(double[] quantiles, int index) =>
        index < quantiles.Length && double.IsFinite(quantiles[index]) ? quantiles[index] / 1_000_000.0 : 0;

    /// <summary>Same scan/time safety cap as <see cref="ExternalApiQueryService"/>.</summary>
    private QueryOptions SafetyOptions() => QuerySafety.Full(queryLimits.Value);
}
