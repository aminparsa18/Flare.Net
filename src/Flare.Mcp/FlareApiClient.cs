namespace Flare.Mcp;

/// <summary>
/// The Flare.Api endpoint the tools query. <paramref name="NotReadyMessage"/> is non-null when
/// the host already knows the API can't answer (e.g. the CLI's standing instance isn't
/// initialized) so tools fail fast with an actionable message.
/// </summary>
internal sealed record FlareApiClient(HttpClient Http, string? NotReadyMessage = null);
