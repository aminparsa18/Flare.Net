namespace Flare.Api.Model;

/// <summary>
/// The recorded chunks of one browser session (ADR-0264), oldest first, so the dashboard can size the player and fetch the
/// events without downloading any payload up front. Request: <see cref="AppSessionTimelineRequest"/>.
/// </summary>
public sealed record WebReplayResponse
{
    public required string SessionId { get; init; }

    /// <summary>The service the first chunk came from; empty when the session has no replay.</summary>
    public required string ServiceName { get; init; }

    public required IReadOnlyList<WebReplayChunk> Chunks { get; init; }

    /// <summary>True when the session has more chunks than the response carries (the newest were cut).</summary>
    public bool Truncated { get; init; }
}

/// <summary>One batch of rrweb events. <see cref="StartUnixMs"/> addresses it together with <see cref="ChunkId"/>.</summary>
public sealed record WebReplayChunk
{
    public required string ChunkId { get; init; }

    public required long StartUnixMs { get; init; }

    public required long EndUnixMs { get; init; }

    public required int EventCount { get; init; }

    /// <summary>The stored size of the events as JSON text, so the player can warn before loading a very large session.</summary>
    public required long PayloadBytes { get; init; }
}

/// <summary>Request body for <c>POST /api/web-replay/chunk</c>. <c>StartUnixMs</c> narrows the read to the chunk's partition.</summary>
public sealed record WebReplayChunkRequest
{
    public string? SessionId { get; init; }

    public string? ChunkId { get; init; }

    public long? StartUnixMs { get; init; }
}
