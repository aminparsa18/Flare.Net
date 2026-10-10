namespace Flare.Api.Model;

/// <summary>
/// Request body for <c>POST /api/app-sessions/list</c> - the <c>/sessions</c> page's table: one
/// row per client-app session, grouped from spans' <c>session.id</c> attribute (stamped by
/// <c>Flare.Maui</c>, ADR-0166, or any SDK that follows the OTel session convention). Plain
/// JSON, not MemoryPack: small flat rows. See docs-internal/adr/0167-app-sessions-view.md.
/// </summary>
public sealed record AppSessionsRequest
{
    /// <summary>Lookback window; null/non-positive = <see cref="Query.AppSessionQueryBuilder.DefaultWindowMinutes"/>, clamped server-side.</summary>
    public int? WindowMinutes { get; init; }

    /// <summary>Where the window ends, as Unix epoch milliseconds; null = now.</summary>
    public long? EndUnixMs { get; init; }

    /// <summary>Exact <c>ServiceName</c>. Null/empty = all services.</summary>
    public string? Service { get; init; }

    /// <summary>Exact <c>service.version</c> resource attribute. Null/empty = all versions.</summary>
    public string? Version { get; init; }

    /// <summary>Only sessions with at least one error span.</summary>
    public bool ErrorsOnly { get; init; }
}

/// <summary>One session. Times are Unix epoch milliseconds.</summary>
public sealed record AppSession
{
    public required string SessionId { get; init; }

    public required string ServiceName { get; init; }

    /// <summary><c>service.version</c>, or empty.</summary>
    public required string Version { get; init; }

    /// <summary><c>os.type</c> and <c>os.version</c>, space-joined, or empty.</summary>
    public required string Os { get; init; }

    /// <summary><c>device.model.identifier</c>, or empty.</summary>
    public required string Device { get; init; }

    public required long FirstSeenUnixMs { get; init; }

    public required long LastSeenUnixMs { get; init; }

    public required ulong SpanCount { get; init; }

    public required ulong TraceCount { get; init; }

    public required ulong ErrorCount { get; init; }

    /// <summary>Up to <see cref="Query.AppSessionQueryBuilder.MaxScreensPerSession"/> distinct <c>screen.name</c> values.</summary>
    public required IReadOnlyList<string> Screens { get; init; }
}

public sealed record AppSessionsResponse
{
    public required int WindowMinutes { get; init; }

    public required IReadOnlyList<AppSession> Sessions { get; init; }

    /// <summary>True when more sessions matched than <see cref="Query.AppSessionQueryBuilder.MaxRows"/>.</summary>
    public required bool Truncated { get; init; }

    /// <summary>Every service with sessions in the window - the toolbar picker, unaffected by the service filter.</summary>
    public required IReadOnlyList<string> Services { get; init; }

    /// <summary>Every <c>service.version</c> with sessions in the window, unaffected by the version filter.</summary>
    public required IReadOnlyList<string> Versions { get; init; }
}

/// <summary>
/// Request body for <c>POST /api/app-sessions/timeline</c> - one session's spans in start-time order.
/// The window bounds the <c>spans</c> scan; the sessions table passes the session's own first/last
/// seen padded by a minute. See docs-internal/adr/0170-app-session-timeline.md.
/// </summary>
public sealed record AppSessionTimelineRequest
{
    public string? SessionId { get; init; }

    /// <summary>Window start, Unix epoch ms; null = <see cref="Query.AppSessionQueryBuilder.DefaultTimelineLookbackMinutes"/> before the end.</summary>
    public long? FromUnixMs { get; init; }

    /// <summary>Window end, Unix epoch ms; null = now.</summary>
    public long? ToUnixMs { get; init; }
}

/// <summary>One span of a session. Times are Unix epoch milliseconds.</summary>
public sealed record AppSessionTimelineEvent
{
    public required string TraceId { get; init; }

    public required string SpanId { get; init; }

    public required string Name { get; init; }

    public required string ServiceName { get; init; }

    public required long StartUnixMs { get; init; }

    public required double DurationMs { get; init; }

    /// <summary><c>screen.name</c>, or empty.</summary>
    public required string Screen { get; init; }

    public required bool IsError { get; init; }

    public required string StatusMessage { get; init; }

    /// <summary>First <c>exception.type</c> on the span's events, or empty.</summary>
    public required string ExceptionType { get; init; }

    /// <summary>First <c>exception.message</c> on the span's events, or empty.</summary>
    public required string ExceptionMessage { get; init; }

    /// <summary><c>breadcrumb.category</c> of a <c>Flare.Maui</c> breadcrumb span (ADR-0172), or empty.</summary>
    public required string BreadcrumbCategory { get; init; }

    /// <summary><c>breadcrumb.message</c>, or empty.</summary>
    public required string BreadcrumbMessage { get; init; }

    /// <summary>True when an error screenshot was stored for this span (ADR-0174).</summary>
    public bool HasScreenshot { get; init; }
}

/// <summary>Request body for <c>POST /api/app-sessions/screenshot</c>: one span's screenshot.</summary>
public sealed record AppSessionScreenshotRequest
{
    public string? SessionId { get; init; }

    public string? SpanId { get; init; }

    /// <summary>Window start, Unix epoch ms; same defaulting as the timeline's window.</summary>
    public long? FromUnixMs { get; init; }

    public long? ToUnixMs { get; init; }
}

/// <summary>A stored screenshot, base64-encoded so the dashboard can show it as a data URL.</summary>
public sealed record AppSessionScreenshotResponse
{
    public required string ContentType { get; init; }

    public required string ImageBase64 { get; init; }
}

public sealed record AppSessionTimelineResponse
{
    public required string SessionId { get; init; }

    /// <summary>Empty when the session has no spans in the window.</summary>
    public required string ServiceName { get; init; }

    public required string Version { get; init; }

    public required string Os { get; init; }

    public required string Device { get; init; }

    public required IReadOnlyList<AppSessionTimelineEvent> Events { get; init; }

    /// <summary>True when the session has more spans than <see cref="Query.AppSessionQueryBuilder.MaxTimelineRows"/>.</summary>
    public required bool Truncated { get; init; }
}

/// <summary>
/// Request body for <c>POST /api/app-sessions/release-health</c>: crash-free sessions and users per app
/// version over the window. See docs-internal/adr/0175-release-health.md.
/// </summary>
public sealed record ReleaseHealthRequest
{
    /// <summary>Lookback window; null/non-positive = <see cref="Query.AppSessionQueryBuilder.DefaultWindowMinutes"/>, clamped server-side.</summary>
    public int? WindowMinutes { get; init; }

    /// <summary>Where the window ends, as Unix epoch milliseconds; null = now.</summary>
    public long? EndUnixMs { get; init; }

    /// <summary>Exact <c>ServiceName</c>. Null/empty = all services.</summary>
    public string? Service { get; init; }
}

/// <summary>One app version's health. Rates are derived by the caller: <c>1 - crashed / total</c>.</summary>
public sealed record ReleaseHealthVersion
{
    /// <summary><c>service.version</c>, or empty when the app reports none.</summary>
    public required string Version { get; init; }

    public required ulong Sessions { get; init; }

    /// <summary>Sessions with a fatal unhandled exception.</summary>
    public required ulong CrashedSessions { get; init; }

    /// <summary>Sessions with at least one error span (a superset of the crashed ones).</summary>
    public required ulong ErroredSessions { get; init; }

    /// <summary>Distinct <c>user.id</c> values; 0 when the app sets none.</summary>
    public required ulong Users { get; init; }

    public required ulong CrashedUsers { get; init; }
}

public sealed record ReleaseHealthResponse
{
    public required int WindowMinutes { get; init; }

    public required IReadOnlyList<ReleaseHealthVersion> Versions { get; init; }
}

/// <summary>
/// Request for <c>POST /api/app-sessions/performance</c>: app start and per-screen load and frame health over a window.
/// See docs-internal/adr/0180-maui-mobile-performance.md.
/// </summary>
public sealed record AppPerformanceRequest
{
    /// <summary>Lookback window; null/non-positive = <see cref="Query.AppSessionQueryBuilder.DefaultWindowMinutes"/>, clamped server-side.</summary>
    public int? WindowMinutes { get; init; }

    /// <summary>Where the window ends, as Unix epoch milliseconds; null = now.</summary>
    public long? EndUnixMs { get; init; }

    /// <summary>Exact <c>ServiceName</c>. Null/empty = all services.</summary>
    public string? Service { get; init; }

    /// <summary>Exact <c>service.version</c>. Null/empty = all versions.</summary>
    public string? Version { get; init; }
}

/// <summary>Cold or warm app starts of one version, as <c>app.start</c> span durations.</summary>
public sealed record AppStartStat
{
    public required string Version { get; init; }

    /// <summary><c>cold</c> or <c>warm</c>.</summary>
    public required string Type { get; init; }

    public required ulong Count { get; init; }

    public required double P50Ms { get; init; }

    public required double P95Ms { get; init; }
}

/// <summary>One screen's load time (<c>screen.load</c> spans) and frame health (<c>screen.frames</c> spans).</summary>
public sealed record ScreenPerformance
{
    public required string Screen { get; init; }

    public required ulong Loads { get; init; }

    public required double LoadP50Ms { get; init; }

    public required double LoadP95Ms { get; init; }

    /// <summary>Screen visits that reported frames.</summary>
    public required ulong Visits { get; init; }

    public required ulong Frames { get; init; }

    /// <summary>Frames over the slow threshold, frozen ones included.</summary>
    public required ulong SlowFrames { get; init; }

    public required ulong FrozenFrames { get; init; }
}

public sealed record AppPerformanceResponse
{
    public required int WindowMinutes { get; init; }

    public required IReadOnlyList<AppStartStat> Starts { get; init; }

    public required IReadOnlyList<ScreenPerformance> Screens { get; init; }
}
