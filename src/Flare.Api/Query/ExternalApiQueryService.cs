using ClickHouse.Driver;
using Flare.Api.Model;
using Microsoft.Extensions.Options;

namespace Flare.Api.Query;

public interface IExternalApiQueryService
{
    Task<ExternalDomainsResponse> GetDomainsAsync(ExternalDomainsRequest request, CancellationToken cancellationToken);

    Task<ExternalDomainDetailResponse> GetDomainDetailAsync(ExternalDomainDetailRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// The one component holding an <see cref="IClickHouseClient"/> for the <c>/external-apis</c>
/// page - see <see cref="ExternalApiQueryBuilder"/> for the SQL. Same <c>ExecuteReaderAsync</c> +
/// ordinal-read style as <see cref="MessagingQueryService"/>.
/// </summary>
public sealed class ExternalApiQueryService(IClickHouseClient client, IOptions<QueryLimitsOptions> queryLimits, TimeProvider timeProvider) : IExternalApiQueryService
{
    public async Task<ExternalDomainsResponse> GetDomainsAsync(ExternalDomainsRequest request, CancellationToken cancellationToken)
    {
        var windowMinutes = ExternalApiQueryBuilder.ClampWindowMinutes(request.WindowMinutes);
        var end = ExternalApiQueryBuilder.ResolveWindowEnd(request.EndUnixMs, timeProvider.GetUtcNow());
        var seconds = windowMinutes * 60.0;

        var domains = new List<ExternalDomain>();
        var built = ExternalApiQueryBuilder.BuildDomains(request, windowMinutes, end);
        await using (var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, SafetyOptions(), cancellationToken))
        {
            while (reader.Read() && domains.Count < ExternalApiQueryBuilder.MaxRows)
            {
                var callCount = reader.GetFieldValue<ulong>(1);
                var quantiles = reader.GetFieldValue<double[]>(3);
                domains.Add(new ExternalDomain
                {
                    Domain = reader.GetString(0),
                    CallCount = callCount,
                    ErrorCount = reader.GetFieldValue<ulong>(2),
                    PerSecond = callCount / seconds,
                    P50Ms = NanosToMs(quantiles, 0),
                    P95Ms = NanosToMs(quantiles, 1),
                    P99Ms = NanosToMs(quantiles, 2),
                    ServiceCount = reader.GetFieldValue<ulong>(4),
                    EndpointCount = reader.GetFieldValue<ulong>(5),
                    LastSeenUnixMs = reader.GetFieldValue<long>(6),
                });
            }
        }

        string[] services = [];
        var facetsSql = ExternalApiQueryBuilder.BuildFacets(windowMinutes, end);
        await using (var reader = await client.ExecuteReaderAsync(facetsSql.Sql, facetsSql.Parameters, SafetyOptions(), cancellationToken))
        {
            if (reader.Read())
            {
                services = reader.GetFieldValue<string[]>(0);
            }
        }

        return new ExternalDomainsResponse
        {
            WindowMinutes = windowMinutes,
            Domains = domains,
            Services = services,
        };
    }

    public async Task<ExternalDomainDetailResponse> GetDomainDetailAsync(ExternalDomainDetailRequest request, CancellationToken cancellationToken)
    {
        var windowMinutes = ExternalApiQueryBuilder.ClampWindowMinutes(request.WindowMinutes);
        var end = ExternalApiQueryBuilder.ResolveWindowEnd(request.EndUnixMs, timeProvider.GetUtcNow());
        var seconds = windowMinutes * 60.0;

        var endpoints = new List<ExternalEndpointStats>();
        var endpointsSql = ExternalApiQueryBuilder.BuildEndpoints(request, windowMinutes, end);
        await using (var reader = await client.ExecuteReaderAsync(endpointsSql.Sql, endpointsSql.Parameters, SafetyOptions(), cancellationToken))
        {
            while (reader.Read() && endpoints.Count < ExternalApiQueryBuilder.MaxRows)
            {
                var callCount = reader.GetFieldValue<ulong>(3);
                var quantiles = reader.GetFieldValue<double[]>(5);
                endpoints.Add(new ExternalEndpointStats
                {
                    Method = reader.GetString(0),
                    Endpoint = reader.GetString(1),
                    EndpointSource = (ExternalEndpointSource)reader.GetFieldValue<byte>(2),
                    CallCount = callCount,
                    ErrorCount = reader.GetFieldValue<ulong>(4),
                    PerSecond = callCount / seconds,
                    P50Ms = NanosToMs(quantiles, 0),
                    P95Ms = NanosToMs(quantiles, 1),
                    P99Ms = NanosToMs(quantiles, 2),
                    LastSeenUnixMs = reader.GetFieldValue<long>(6),
                });
            }
        }

        var statusCodes = new List<ExternalStatusCodeCount>();
        var statusSql = ExternalApiQueryBuilder.BuildStatusCodes(request, windowMinutes, end);
        await using (var reader = await client.ExecuteReaderAsync(statusSql.Sql, statusSql.Parameters, SafetyOptions(), cancellationToken))
        {
            while (reader.Read() && statusCodes.Count < ExternalApiQueryBuilder.MaxRows)
            {
                statusCodes.Add(new ExternalStatusCodeCount
                {
                    StatusCode = reader.GetString(0),
                    CallCount = reader.GetFieldValue<ulong>(1),
                });
            }
        }

        var callers = new List<ExternalCallerStats>();
        var callersSql = ExternalApiQueryBuilder.BuildCallers(request, windowMinutes, end);
        await using (var reader = await client.ExecuteReaderAsync(callersSql.Sql, callersSql.Parameters, SafetyOptions(), cancellationToken))
        {
            while (reader.Read() && callers.Count < ExternalApiQueryBuilder.MaxRows)
            {
                var callCount = reader.GetFieldValue<ulong>(1);
                var quantiles = reader.GetFieldValue<double[]>(3);
                callers.Add(new ExternalCallerStats
                {
                    ServiceName = reader.GetString(0),
                    CallCount = callCount,
                    ErrorCount = reader.GetFieldValue<ulong>(2),
                    PerSecond = callCount / seconds,
                    P50Ms = NanosToMs(quantiles, 0),
                    P95Ms = NanosToMs(quantiles, 1),
                    P99Ms = NanosToMs(quantiles, 2),
                });
            }
        }

        var topErrors = new List<ExternalErrorGroup>();
        var errorsSql = ExternalApiQueryBuilder.BuildTopErrors(request, windowMinutes, end);
        await using (var reader = await client.ExecuteReaderAsync(errorsSql.Sql, errorsSql.Parameters, SafetyOptions(), cancellationToken))
        {
            while (reader.Read())
            {
                topErrors.Add(new ExternalErrorGroup
                {
                    Method = reader.GetString(0),
                    Endpoint = reader.GetString(1),
                    EndpointSource = (ExternalEndpointSource)reader.GetFieldValue<byte>(2),
                    StatusCode = reader.GetString(3),
                    ErrorType = reader.GetString(4),
                    CallCount = reader.GetFieldValue<ulong>(5),
                    LastSeenUnixMs = reader.GetFieldValue<long>(6),
                    SampleMessage = reader.GetString(7),
                });
            }
        }

        return new ExternalDomainDetailResponse
        {
            Domain = request.Domain,
            WindowMinutes = windowMinutes,
            Endpoints = endpoints,
            StatusCodes = statusCodes,
            Callers = callers,
            TopErrors = topErrors,
        };
    }

    /// <summary>Same <c>nan</c>-guard as <see cref="MessagingQueryService"/>'s - never hit for a real row (every row has at least one span), kept for safety.</summary>
    private static double NanosToMs(double[] quantiles, int index) =>
        index < quantiles.Length && double.IsFinite(quantiles[index]) ? quantiles[index] / 1_000_000.0 : 0;

    /// <summary>Same scan/time safety cap as <see cref="MessagingQueryService"/>.</summary>
    private QueryOptions SafetyOptions() => QuerySafety.Full(queryLimits.Value);
}
