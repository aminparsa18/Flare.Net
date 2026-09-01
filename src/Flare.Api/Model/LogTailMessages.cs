using MemoryPack;

namespace Flare.Api.Model;

/// <summary>The kind of control message a live-tail client sends over <c>GET /api/logs/tail</c>.</summary>
public enum LogTailClientMessageType
{
    /// <summary>(Re)subscribe with a new <see cref="LogTailClientMessage.Filter"/>, replacing any previous one.</summary>
    Subscribe,

    /// <summary>Stop delivering events until <see cref="Resume"/>. Events published while paused are dropped, not queued.</summary>
    Pause,

    Resume,
}

/// <summary>
/// One control message from a live-tail client. A connection must send a
/// <see cref="LogTailClientMessageType.Subscribe"/> message before it receives anything -
/// mirrors <c>/api/logs/search</c>'s "send your query, then get results" shape rather than
/// an implicit firehose-on-connect.
/// </summary>
[MemoryPackable]
public sealed partial record LogTailClientMessage
{
    public required LogTailClientMessageType Type { get; init; }

    /// <summary>Required (and replaces any prior filter) when <see cref="Type"/> is <see cref="LogTailClientMessageType.Subscribe"/>; ignored otherwise.</summary>
    public LogFilter? Filter { get; init; }
}

/// <summary>The kind of message the live-tail endpoint pushes to a subscribed client.</summary>
public enum LogTailServerMessageType
{
    /// <summary>A matching log event - see <see cref="LogTailServerMessage.Event"/>.</summary>
    Event,

    /// <summary>
    /// This connection's channel overflowed (a slow reader) and one or more events since
    /// the last <see cref="Dropped"/> message were discarded rather than queued - see
    /// <see cref="LogTailServerMessage.DroppedCount"/>.
    /// </summary>
    Dropped,

    /// <summary>A malformed or unsupported client message was received; the connection stays open.</summary>
    Error,
}

/// <summary>One message pushed to a live-tail client.</summary>
[MemoryPackable]
public sealed partial record LogTailServerMessage
{
    public required LogTailServerMessageType Type { get; init; }

    public LogEventDto? Event { get; init; }

    public int? DroppedCount { get; init; }

    public string? Error { get; init; }
}
