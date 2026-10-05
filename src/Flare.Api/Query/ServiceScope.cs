using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Flare.Identity.Projects;

namespace Flare.Api.Query;

/// <summary>
/// The service allow-list the current request is restricted to (ADR-0123 phase 2), or null for
/// unrestricted. Set per request by <c>ProjectScopeMiddleware</c> and read by the filter SQL
/// builders, so no individual endpoint has to remember to apply it. Ambient (an
/// <see cref="AsyncLocal{T}"/>) on purpose: the alternative is threading a scope through every
/// handler and builder, and one forgotten call site is a data leak. Background workers and the
/// unit tests never set it and stay unrestricted.
/// </summary>
public static class ServiceScope
{
    private static readonly AsyncLocal<IReadOnlyList<string>?> CurrentPatterns = new();

    public static IReadOnlyList<string>? Current
    {
        get => CurrentPatterns.Value;
        set => CurrentPatterns.Value = value;
    }

    /// <summary>Adds the scope clause for <paramref name="column"/>, if the request is restricted.</summary>
    public static void Append(List<string> clauses, ClickHouseParameterCollection parameters, string column = "ServiceName", string paramSuffix = "")
    {
        if (Current is not { } patterns)
        {
            return;
        }

        var exact = patterns.Where(p => !p.EndsWith('*')).ToArray();
        var prefixes = patterns.Where(p => p.EndsWith('*')).Select(p => p[..^1]).ToArray();
        var parts = new List<string>();
        if (exact.Length > 0)
        {
            parameters.AddParameter($"scopeExact{paramSuffix}", exact);
            parts.Add($"{column} IN {{scopeExact{paramSuffix}:Array(String)}}");
        }

        if (prefixes.Length > 0)
        {
            parameters.AddParameter($"scopePrefixes{paramSuffix}", prefixes);
            parts.Add($"arrayExists(p -> startsWith({column}, p), {{scopePrefixes{paramSuffix}:Array(String)}})");
        }

        // An empty allow-list (a user in no project) matches nothing.
        clauses.Add(parts.Count == 0 ? "0" : $"({string.Join(" OR ", parts)})");
    }

    /// <summary>The scope clause as an <c>" AND ..."</c> suffix (empty when unrestricted), for builders that assemble SQL by concatenation.</summary>
    public static string Suffix(ClickHouseParameterCollection parameters, string column = "ServiceName")
    {
        var clauses = new List<string>();
        Append(clauses, parameters, column);
        return string.Concat(clauses.Select(c => " AND " + c));
    }

    public static bool Allows(string serviceName) =>
        Current is not { } patterns || ProjectServicePattern.MatchesAny(patterns, serviceName);
}
