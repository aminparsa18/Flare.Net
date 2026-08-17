namespace Flare.Ingest.Patterns;

/// <summary>The result of matching one log body against the pattern tree.</summary>
/// <param name="PatternId">Deterministic id derived from <paramref name="PatternTemplate"/> (see <see cref="DrainPatternMatcher"/>).</param>
/// <param name="PatternTemplate">The (possibly wildcarded, <c>&lt;*&gt;</c>) template text the body was clustered into.</param>
public sealed record PatternMatch(string PatternId, string PatternTemplate);

/// <summary>
/// Clusters log message bodies into recurring templates (e.g. "GET /api/orders/123" and
/// "GET /api/orders/456" both matching "GET /api/orders/&lt;*&gt;"), per Planning.md's log
/// pattern detection item.
/// </summary>
public interface ILogPatternMatcher
{
    /// <summary>
    /// Matches (or creates) a cluster for <paramref name="body"/>. Never throws on
    /// null/empty/pathological input - the ingest flush path can't afford to fail a whole
    /// batch over one malformed log line.
    /// </summary>
    PatternMatch Match(string? body);
}
