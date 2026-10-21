using ClickHouse.Driver;
using Flare.Api.Model;
using Microsoft.Extensions.Options;

namespace Flare.Api.Query;

public interface IAppSessionQueryService
{
    Task<AppSessionsResponse> GetSessionsAsync(AppSessionsRequest request, CancellationToken cancellationToken);

    Task<ReleaseHealthResponse> GetReleaseHealthAsync(ReleaseHealthRequest request, CancellationToken cancellationToken);

    Task<ReleaseHealthTrendResponse> GetReleaseHealthTrendAsync(ReleaseHealthTrendRequest request, CancellationToken cancellationToken);

    /// <summary>Release health per bucket, service and version over [from, to), for the Prometheus API (ADR-0250). <paramref name="truncated"/> is true when the row cap cut it.</summary>
    Task<(IReadOnlyList<ReleaseHealthSeriesPoint> Points, bool Truncated)> GetReleaseHealthSeriesAsync(
        DateTimeOffset from, DateTimeOffset to, int bucketSeconds, string? service, CancellationToken cancellationToken);

    /// <summary>
    /// Distinct users per step over the trailing <paramref name="windowSeconds"/>, for the Prometheus API's <c>flare_release_users_24h</c>
    /// gauges (ADR-0251). Points carry only <c>Users</c> and <c>CrashedUsers</c>; the session counts are 0.
    /// </summary>
    Task<(IReadOnlyList<ReleaseHealthSeriesPoint> Points, bool Truncated)> GetRollingReleaseUsersAsync(
        DateTimeOffset from, DateTimeOffset to, int bucketSeconds, int windowSeconds, string? service, CancellationToken cancellationToken);

    Task<AppPerformanceResponse> GetPerformanceAsync(AppPerformanceRequest request, CancellationToken cancellationToken);

    Task<AppSessionTimelineResponse> GetTimelineAsync(string sessionId, AppSessionTimelineRequest request, CancellationToken cancellationToken);

    Task<AppSessionScreenshotResponse?> GetScreenshotAsync(string sessionId, string spanId, AppSessionTimelineRequest window, CancellationToken cancellationToken);

    Task<AppSessionReplayResponse> GetReplayAsync(string sessionId, AppSessionTimelineRequest window, CancellationToken cancellationToken);

    Task<AppSessionScreenshotResponse?> GetReplayFrameAsync(string sessionId, long timeUnixMs, CancellationToken cancellationToken);

    Task<AppFeedbackResponse> GetFeedbackAsync(AppFeedbackRequest request, CancellationToken cancellationToken);
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
        var from = end.AddMinutes(-windowMinutes);
        IReadOnlySet<string>? replaySessions = null;
        if (request.ReplayOnly)
            replaySessions = await ReadReplaySessionIdsAsync(from, end, request.Service, null, cancellationToken);
        var built = AppSessionQueryBuilder.BuildSessions(request, windowMinutes, end, replaySessions);
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

        if (replaySessions is null && sessions.Count > 0)
            replaySessions = await ReadReplaySessionIdsAsync(from, end, request.Service, sessions.Select(s => s.SessionId).ToArray(), cancellationToken);
        if (replaySessions is { Count: > 0 })
        {
            for (var i = 0; i < sessions.Count; i++)
            {
                if (replaySessions.Contains(sessions[i].SessionId)) sessions[i] = sessions[i] with { HasReplay = true };
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

    /// <summary>
    /// The sessions with a browser recording (ADR-0264) starting in the window. A missing <c>web_replay_chunks</c> table (migration
    /// 0090 not applied) means no recordings, not a failed sessions page.
    /// </summary>
    private async Task<IReadOnlySet<string>> ReadReplaySessionIdsAsync(
        DateTimeOffset from, DateTimeOffset to, string? service, IReadOnlyCollection<string>? sessionIds, CancellationToken cancellationToken)
    {
        var ids = new HashSet<string>();
        var built = WebReplayQueryBuilder.BuildSessionIds(from, to, service, sessionIds);
        try
        {
            await using var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, QuerySafety.Full(queryLimits.Value), cancellationToken);
            while (reader.Read()) ids.Add(reader.GetString(0));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ids.Clear();
        }

        return ids;
    }

    public async Task<ReleaseHealthResponse> GetReleaseHealthAsync(ReleaseHealthRequest request, CancellationToken cancellationToken)
    {
        var windowMinutes = AppSessionQueryBuilder.ClampWindowMinutes(request.WindowMinutes);
        var end = AppSessionQueryBuilder.ResolveWindowEnd(request.EndUnixMs, timeProvider.GetUtcNow());
        var built = AppSessionQueryBuilder.BuildReleaseHealth(request, windowMinutes, end);

        var versions = new List<ReleaseHealthVersion>();
        await using var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, QuerySafety.Full(queryLimits.Value), cancellationToken);
        while (reader.Read())
        {
            versions.Add(new ReleaseHealthVersion
            {
                Version = reader.GetString(0),
                Sessions = reader.GetFieldValue<ulong>(1),
                CrashedSessions = reader.GetFieldValue<ulong>(2),
                ErroredSessions = reader.GetFieldValue<ulong>(3),
                Users = reader.GetFieldValue<ulong>(4),
                CrashedUsers = reader.GetFieldValue<ulong>(5),
            });
        }

        return new ReleaseHealthResponse { WindowMinutes = windowMinutes, Versions = versions };
    }

    public async Task<ReleaseHealthTrendResponse> GetReleaseHealthTrendAsync(ReleaseHealthTrendRequest request, CancellationToken cancellationToken)
    {
        var windowMinutes = AppSessionQueryBuilder.ClampWindowMinutes(request.WindowMinutes);
        var bucketMinutes = AppSessionQueryBuilder.ResolveTrendBucketMinutes(request.BucketMinutes, windowMinutes);
        var end = AppSessionQueryBuilder.ResolveWindowEnd(request.EndUnixMs, timeProvider.GetUtcNow());
        var built = AppSessionQueryBuilder.BuildReleaseHealthTrend(request, windowMinutes, bucketMinutes, end);

        var points = new List<ReleaseHealthTrendPoint>();
        await using var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, QuerySafety.Full(queryLimits.Value), cancellationToken);
        while (reader.Read())
        {
            points.Add(new ReleaseHealthTrendPoint
            {
                BucketUnixMs = reader.GetFieldValue<long>(0),
                Version = reader.GetString(1),
                Sessions = reader.GetFieldValue<ulong>(2),
                CrashedSessions = reader.GetFieldValue<ulong>(3),
            });
        }

        return new ReleaseHealthTrendResponse { WindowMinutes = windowMinutes, BucketMinutes = bucketMinutes, Points = points };
    }

    public Task<(IReadOnlyList<ReleaseHealthSeriesPoint> Points, bool Truncated)> GetReleaseHealthSeriesAsync(
        DateTimeOffset from, DateTimeOffset to, int bucketSeconds, string? service, CancellationToken cancellationToken) =>
        ReadSeriesAsync(AppSessionQueryBuilder.BuildReleaseHealthSeries(from, to, bucketSeconds, service), rolling: false, cancellationToken);

    public Task<(IReadOnlyList<ReleaseHealthSeriesPoint> Points, bool Truncated)> GetRollingReleaseUsersAsync(
        DateTimeOffset from, DateTimeOffset to, int bucketSeconds, int windowSeconds, string? service, CancellationToken cancellationToken) =>
        ReadSeriesAsync(AppSessionQueryBuilder.BuildRollingReleaseUsers(from, to, bucketSeconds, windowSeconds, service), rolling: true, cancellationToken);

    private async Task<(IReadOnlyList<ReleaseHealthSeriesPoint> Points, bool Truncated)> ReadSeriesAsync(
        AppSessionSql built, bool rolling, CancellationToken cancellationToken)
    {
        var points = new List<ReleaseHealthSeriesPoint>();
        var truncated = false;
        await using var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, QuerySafety.Full(queryLimits.Value), cancellationToken);
        while (reader.Read())
        {
            if (points.Count >= AppSessionQueryBuilder.MaxSeriesRows)
            {
                truncated = true;
                break;
            }

            // The rolling query has no session counts: its columns are BucketUnixMs, ServiceName, Version, Users, CrashedUsers.
            var users = rolling ? 3 : 5;
            points.Add(new ReleaseHealthSeriesPoint
            {
                BucketUnixMs = reader.GetFieldValue<long>(0),
                ServiceName = reader.GetString(1),
                Version = reader.GetString(2),
                Sessions = rolling ? 0 : reader.GetFieldValue<ulong>(3),
                CrashedSessions = rolling ? 0 : reader.GetFieldValue<ulong>(4),
                Users = reader.GetFieldValue<ulong>(users),
                CrashedUsers = reader.GetFieldValue<ulong>(users + 1),
            });
        }

        return (points, truncated);
    }

    public async Task<AppPerformanceResponse> GetPerformanceAsync(AppPerformanceRequest request, CancellationToken cancellationToken)
    {
        var windowMinutes = AppSessionQueryBuilder.ClampWindowMinutes(request.WindowMinutes);
        var end = AppSessionQueryBuilder.ResolveWindowEnd(request.EndUnixMs, timeProvider.GetUtcNow());

        var starts = new List<AppStartStat>();
        var startsSql = AppSessionQueryBuilder.BuildAppStarts(request, windowMinutes, end);
        await using (var reader = await client.ExecuteReaderAsync(startsSql.Sql, startsSql.Parameters, QuerySafety.Full(queryLimits.Value), cancellationToken))
        {
            while (reader.Read())
            {
                starts.Add(new AppStartStat
                {
                    Version = reader.GetString(0),
                    Type = reader.GetString(1),
                    Count = reader.GetFieldValue<ulong>(2),
                    P50Ms = reader.GetFieldValue<double>(3),
                    P95Ms = reader.GetFieldValue<double>(4),
                });
            }
        }

        var screens = new List<ScreenPerformance>();
        var screensSql = AppSessionQueryBuilder.BuildScreenPerformance(request, windowMinutes, end);
        await using (var reader = await client.ExecuteReaderAsync(screensSql.Sql, screensSql.Parameters, QuerySafety.Full(queryLimits.Value), cancellationToken))
        {
            while (reader.Read())
            {
                screens.Add(new ScreenPerformance
                {
                    Screen = reader.GetString(0),
                    Loads = reader.GetFieldValue<ulong>(1),
                    LoadP50Ms = reader.GetFieldValue<double>(2),
                    LoadP95Ms = reader.GetFieldValue<double>(3),
                    Visits = reader.GetFieldValue<ulong>(4),
                    Frames = reader.GetFieldValue<ulong>(5),
                    SlowFrames = reader.GetFieldValue<ulong>(6),
                    FrozenFrames = reader.GetFieldValue<ulong>(7),
                });
            }
        }

        return new AppPerformanceResponse { WindowMinutes = windowMinutes, Starts = starts, Screens = screens };
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
                BreadcrumbCategory = reader.GetString(14),
                BreadcrumbMessage = reader.GetString(15),
            });
        }

        // Screenshots are keyed by span id; a failure here must not hide the timeline itself.
        var withScreenshot = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            var shots = AppSessionQueryBuilder.BuildScreenshotSpans(sessionId, from, to);
            await using var shotReader = await client.ExecuteReaderAsync(shots.Sql, shots.Parameters, QuerySafety.Full(queryLimits.Value), cancellationToken);
            while (shotReader.Read()) withScreenshot.Add(shotReader.GetString(0));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Table missing (migration 0079 not applied yet) or a transient error: no thumbnails, same timeline.
        }

        return new AppSessionTimelineResponse
        {
            SessionId = sessionId,
            ServiceName = service,
            Version = version,
            Os = os,
            Device = device,
            Events = withScreenshot.Count == 0 ? events : events.Select(e => withScreenshot.Contains(e.SpanId) ? e with { HasScreenshot = true } : e).ToList(),
            Truncated = truncated,
        };
    }

    public async Task<AppSessionScreenshotResponse?> GetScreenshotAsync(string sessionId, string spanId, AppSessionTimelineRequest window, CancellationToken cancellationToken)
    {
        var (from, to) = AppSessionQueryBuilder.ResolveTimelineWindow(window, timeProvider.GetUtcNow());
        var built = AppSessionQueryBuilder.BuildScreenshot(sessionId, spanId, from, to);
        await using var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, QuerySafety.ExecutionTimeOnly(queryLimits.Value), cancellationToken);
        return reader.Read()
            ? new AppSessionScreenshotResponse { ContentType = reader.GetString(0), ImageBase64 = reader.GetString(1) }
            : null;
    }

    public async Task<AppSessionReplayResponse> GetReplayAsync(string sessionId, AppSessionTimelineRequest window, CancellationToken cancellationToken)
    {
        var (from, to) = AppSessionQueryBuilder.ResolveTimelineWindow(window, timeProvider.GetUtcNow());
        var built = AppSessionQueryBuilder.BuildReplayFrames(sessionId, from, to);
        var frames = new List<long>();
        try
        {
            await using var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, QuerySafety.Full(queryLimits.Value), cancellationToken);
            while (reader.Read()) frames.Add(reader.GetFieldValue<long>(0));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Table or Kind column missing (migration 0079/0081 not applied yet): an empty replay, same page.
            frames.Clear();
        }

        var truncated = frames.Count > AppSessionQueryBuilder.MaxReplayFrames;
        return new AppSessionReplayResponse
        {
            SessionId = sessionId,
            FrameUnixMs = truncated ? frames.Take(AppSessionQueryBuilder.MaxReplayFrames).ToList() : frames,
            Truncated = truncated,
        };
    }

    public async Task<AppSessionScreenshotResponse?> GetReplayFrameAsync(string sessionId, long timeUnixMs, CancellationToken cancellationToken)
    {
        var built = AppSessionQueryBuilder.BuildReplayFrame(sessionId, timeUnixMs);
        await using var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, QuerySafety.ExecutionTimeOnly(queryLimits.Value), cancellationToken);
        return reader.Read()
            ? new AppSessionScreenshotResponse { ContentType = reader.GetString(0), ImageBase64 = reader.GetString(1) }
            : null;
    }

    public async Task<AppFeedbackResponse> GetFeedbackAsync(AppFeedbackRequest request, CancellationToken cancellationToken)
    {
        var windowMinutes = AppSessionQueryBuilder.ClampWindowMinutes(request.WindowMinutes);
        var end = AppSessionQueryBuilder.ResolveWindowEnd(request.EndUnixMs, timeProvider.GetUtcNow());
        var built = AppSessionQueryBuilder.BuildFeedback(request, windowMinutes, end);

        var items = new List<AppFeedback>();
        var truncated = false;
        try
        {
            await using var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, QuerySafety.Full(queryLimits.Value), cancellationToken);
            while (reader.Read())
            {
                if (items.Count >= AppSessionQueryBuilder.MaxFeedbackRows)
                {
                    truncated = true;
                    break;
                }

                items.Add(new AppFeedback
                {
                    FeedbackId = reader.GetString(0),
                    TimeUnixMs = reader.GetFieldValue<long>(1),
                    ServiceName = reader.GetString(2),
                    SessionId = reader.GetString(3),
                    UserId = reader.GetString(4),
                    Name = reader.GetString(5),
                    Email = reader.GetString(6),
                    AppVersion = reader.GetString(7),
                    Message = reader.GetString(8),
                });
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Table missing (migration 0082 not applied yet): an empty inbox, same page.
            items.Clear();
            truncated = false;
        }

        return new AppFeedbackResponse { Items = items, Truncated = truncated };
    }
}
