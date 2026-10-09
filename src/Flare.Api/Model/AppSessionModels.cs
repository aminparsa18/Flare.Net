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
