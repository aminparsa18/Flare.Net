using ClickHouse.Driver;
using Flare.Api.Model;
using Microsoft.Extensions.Options;

namespace Flare.Api.Query;

public interface IAppSessionQueryService
{
    Task<AppSessionsResponse> GetSessionsAsync(AppSessionsRequest request, CancellationToken cancellationToken);

    Task<AppSessionTimelineResponse> GetTimelineAsync(string sessionId, AppSessionTimelineRequest request, CancellationToken cancellationToken);
}

/// <summary>The one component holding an <see cref="IClickHouseClient"/> for the <c>/sessions</c> page - see <see cref="AppSessionQueryBuilder"/> for the SQL.</summary>
public sealed class AppSessionQueryService(IClickHouseClient client, IOptions<QueryLimitsOptions> queryLimits, TimeProvider timeProvider) : IAppSessionQueryService
{
    public async Task<AppSessionsResponse> GetSessionsAsync(AppSessionsRequest request, CancellationToken cancellationToken)
    {
        var windowMinutes = AppSessionQueryBuilder.ClampWindowMinutes(request.WindowMinutes);
        var end = AppSessionQueryBuilder.ResolveWindowEnd(request.EndUnixMs, timeProvider.GetUtcNow());

        var sessions = new List<AppSession>();
        var truncated = false;
        var built = AppSessionQueryBuilder.BuildSessions(request, windowMinutes, end);
        await using (var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, QuerySafety.Full(queryLimits.Value), cancellationToken))
        {
            while (reader.Read())
            {
                if (sessions.Count >= AppSessionQueryBuilder.MaxRows)
                {
                    truncated = true;
                    break;
                }

                sessions.Add(new AppSession
                {
                    SessionId = reader.GetString(0),
                    ServiceName = reader.GetString(1),
                    Version = reader.GetString(2),
                    Os = reader.GetString(3),
                    Device = reader.GetString(4),
                    FirstSeenUnixMs = reader.GetFieldValue<long>(5),
                    LastSeenUnixMs = reader.GetFieldValue<long>(6),
                    SpanCount = reader.GetFieldValue<ulong>(7),
                    TraceCount = reader.GetFieldValue<ulong>(8),
                    ErrorCount = reader.GetFieldValue<ulong>(9),
                    Screens = reader.GetFieldValue<string[]>(10),
                });
            }
        }

        string[] services = [];
        string[] versions = [];
        var facets = AppSessionQueryBuilder.BuildFacets(windowMinutes, end);
        await using (var reader = await client.ExecuteReaderAsync(facets.Sql, facets.Parameters, QuerySafety.Full(queryLimits.Value), cancellationToken))
        {
            if (reader.Read())
            {
                services = reader.GetFieldValue<string[]>(0);
                versions = reader.GetFieldValue<string[]>(1);
            }
        }

        return new AppSessionsResponse
        {
            WindowMinutes = windowMinutes,
            Sessions = sessions,
            Truncated = truncated,
            Services = services,
            Versions = versions,
        };
    }

    public async Task<AppSessionTimelineResponse> GetTimelineAsync(string sessionId, AppSessionTimelineRequest request, CancellationToken cancellationToken)
    {
        var (from, to) = AppSessionQueryBuilder.ResolveTimelineWindow(request, timeProvider.GetUtcNow());
        var built = AppSessionQueryBuilder.BuildTimeline(sessionId, from, to);

        var events = new List<AppSessionTimelineEvent>();
        var truncated = false;
        string version = string.Empty, os = string.Empty, device = string.Empty, service = string.Empty;
        await using var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, QuerySafety.Full(queryLimits.Value), cancellationToken);
        while (reader.Read())
        {
            if (events.Count >= AppSessionQueryBuilder.MaxTimelineRows)
            {
                truncated = true;
                break;
            }

            if (events.Count == 0)
            {
                service = reader.GetString(3);
                version = reader.GetString(11);
                os = reader.GetString(12);
                device = reader.GetString(13);
            }

            events.Add(new AppSessionTimelineEvent
            {
                TraceId = reader.GetString(0),
                SpanId = reader.GetString(1),
                Name = reader.GetString(2),
                ServiceName = reader.GetString(3),
                StartUnixMs = reader.GetFieldValue<long>(4),
                DurationMs = reader.GetFieldValue<double>(5),
                Screen = reader.GetString(6),
                IsError = reader.GetFieldValue<byte>(7) != 0,
                StatusMessage = reader.GetString(8),
                ExceptionType = reader.GetString(9),
                ExceptionMessage = reader.GetString(10),
            });
        }

        return new AppSessionTimelineResponse
        {
            SessionId = sessionId,
            ServiceName = service,
            Version = version,
            Os = os,
            Device = device,
            Events = events,
            Truncated = truncated,
        };
    }
}
