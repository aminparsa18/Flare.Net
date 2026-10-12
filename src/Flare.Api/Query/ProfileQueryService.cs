using ClickHouse.Driver;
using Flare.Api.Model;
using Microsoft.Extensions.Options;

namespace Flare.Api.Query;

public interface IProfileQueryService
{
    Task<ProfileTypesResponse> GetTypesAsync(ProfileTypesRequest request, CancellationToken cancellationToken);

    Task<FlameGraphResponse> GetFlameGraphAsync(FlameGraphRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// The one component holding an <see cref="IClickHouseClient"/> for the profiles API - SQL lives in
/// <see cref="ProfileQueryBuilder"/>, tree assembly in <see cref="FlameGraphBuilder"/>. Same
/// <c>ExecuteReaderAsync</c> + ordinal-read style as <see cref="ExternalApiQueryService"/>.
/// </summary>
public sealed class ProfileQueryService(IClickHouseClient client, IOptions<QueryLimitsOptions> queryLimits, TimeProvider timeProvider) : IProfileQueryService
{
    public async Task<ProfileTypesResponse> GetTypesAsync(ProfileTypesRequest request, CancellationToken cancellationToken)
    {
        var windowMinutes = ProfileQueryBuilder.ClampWindowMinutes(request.WindowMinutes);
        var end = ProfileQueryBuilder.ResolveWindowEnd(request.EndUnixMs, timeProvider.GetUtcNow());
        var built = ProfileQueryBuilder.BuildTypes(windowMinutes, end);

        var types = new List<ProfileTypeInfo>();
        await using var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, QuerySafety.Full(queryLimits.Value), cancellationToken);
        while (reader.Read())
        {
            types.Add(new ProfileTypeInfo
            {
                Service = reader.GetString(0),
                SampleType = reader.GetString(1),
                SampleUnit = reader.GetString(2),
                SampleCount = reader.GetFieldValue<ulong>(3),
                LastSeenUnixMs = reader.GetFieldValue<long>(4),
            });
        }

        return new ProfileTypesResponse { WindowMinutes = windowMinutes, Types = types };
    }

    public async Task<FlameGraphResponse> GetFlameGraphAsync(FlameGraphRequest request, CancellationToken cancellationToken)
    {
        var windowMinutes = ProfileQueryBuilder.ClampWindowMinutes(request.WindowMinutes);
        var end = ProfileQueryBuilder.ResolveWindowEnd(request.EndUnixMs, timeProvider.GetUtcNow());
        var built = ProfileQueryBuilder.BuildFlameGraph(request, windowMinutes, end);

        var stacks = new List<(IReadOnlyList<string> Stack, long Value)>();
        var unit = string.Empty;
        var truncated = false;
        await using (var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, QuerySafety.Full(queryLimits.Value), cancellationToken))
        {
            while (reader.Read())
            {
                if (stacks.Count == ProfileQueryBuilder.MaxStacks)
                {
                    truncated = true;
                    break;
                }

                stacks.Add((reader.GetFieldValue<string[]>(0), reader.GetFieldValue<long>(1)));
                unit = reader.GetString(2);
            }
        }

        return new FlameGraphResponse
        {
            WindowMinutes = windowMinutes,
            SampleUnit = unit,
            StackCount = stacks.Count,
            Truncated = truncated,
            Root = FlameGraphBuilder.Build(stacks),
        };
    }
}
