using MemoryPack;

namespace Flare.Api.Model;

/// <summary>
/// Request body for <c>POST /api/external-apis/domains</c> - the <c>/external-apis</c> page's
/// domain list. Same window/end/service shape as <see cref="MessagingDestinationsRequest"/>,
/// so it carries <c>[GenerateTypeScript]</c> too. See
/// docs-internal/adr/0071-external-api-monitoring.md.
/// </summary>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record ExternalDomainsRequest
{
    /// <summary>Lookback window; null/non-positive = <see cref="Query.ExternalApiQueryBuilder.DefaultWindowMinutes"/>, clamped server-side.</summary>
    public int? WindowMinutes { get; init; }

    /// <summary>Where the window ends, as Unix epoch milliseconds; null = now. Same convention as <see cref="HostMetricsRequest.EndUnixMs"/>.</summary>
    public long? EndUnixMs { get; init; }

    /// <summary>Exact <c>ServiceName</c> of the calling span. Null/empty = all services.</summary>
    public string? Service { get; init; }
}

/// <summary>One external domain's outbound-call figures for the window - one row of the <c>/external-apis</c> page's table.</summary>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record ExternalDomain
{
    /// <summary><c>server.address</c>, else <c>net.peer.name</c>, else the URL's host - see <see cref="Query.ExternalApiQueryBuilder.DomainExpr"/>.</summary>
    public required string Domain { get; init; }

    public required ulong CallCount { get; init; }

    public required ulong ErrorCount { get; init; }

    /// <summary><see cref="CallCount"/> divided by the window length in seconds.</summary>
    public required double PerSecond { get; init; }

    public required double P50Ms { get; init; }

    public required double P95Ms { get; init; }

    public required double P99Ms { get; init; }

    /// <summary>Distinct services that called this domain.</summary>
    public required ulong ServiceCount { get; init; }

    /// <summary>Distinct (method, endpoint) pairs called on this domain - see <see cref="ExternalEndpointStats"/>.</summary>
    public required ulong EndpointCount { get; init; }

    /// <summary>Start of the latest call in the window, Unix epoch milliseconds.</summary>
    public required long LastSeenUnixMs { get; init; }
}

/// <summary>
/// Response body for <c>POST /api/external-apis/domains</c>. Hand-written on the MemoryPack
/// TS side - the <c>IReadOnlyList&lt;T&gt;</c> members block <c>[GenerateTypeScript]</c>, same
/// precedent as <see cref="MessagingDestinationsResponse"/>.
/// </summary>
[MemoryPackable]
public sealed partial record ExternalDomainsResponse
{
    /// <summary>The window actually used, post-clamp.</summary>
    public required int WindowMinutes { get; init; }

    /// <summary>Busiest first, capped at <see cref="Query.ExternalApiQueryBuilder.MaxRows"/>.</summary>
    public required IReadOnlyList<ExternalDomain> Domains { get; init; }

    /// <summary>Every service that made an outbound call in the window, sorted - the toolbar's caller picker. Unaffected by <see cref="ExternalDomainsRequest.Service"/>.</summary>
    public required IReadOnlyList<string> Services { get; init; }
}

/// <summary>Request body for <c>POST /api/external-apis/domain-detail</c> - one domain's endpoints, status codes, callers and top errors.</summary>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record ExternalDomainDetailRequest
{
    public required string Domain { get; init; }

    public int? WindowMinutes { get; init; }

    public long? EndUnixMs { get; init; }

    public string? Service { get; init; }
}

/// <summary>Which rule named an <see cref="ExternalEndpointStats.Endpoint"/> - tells the dashboard which attribute its trace drill-down should filter on. See <see cref="Query.ExternalApiQueryBuilder.EndpointExpr"/>.</summary>
public enum ExternalEndpointSource : byte
{
    /// <summary>The instrumentation's own <c>url.template</c>, verbatim.</summary>
    UrlTemplate = 0,

    /// <summary>The path of <c>url.full</c> (else <c>http.url</c>) with id-like segments replaced by <c>{id}</c>.</summary>
    UrlPath = 1,

    /// <summary><c>rpc.service/rpc.method</c>.</summary>
    Rpc = 2,

    /// <summary>The span name - no URL or RPC attributes to go on.</summary>
    SpanName = 3,
}

/// <summary>One (method, endpoint) on one domain.</summary>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record ExternalEndpointStats
{
    /// <summary><c>http.request.method</c>, else <c>http.method</c>. Empty for non-HTTP calls.</summary>
    public required string Method { get; init; }

    public required string Endpoint { get; init; }

    public required ExternalEndpointSource EndpointSource { get; init; }

    public required ulong CallCount { get; init; }

    public required ulong ErrorCount { get; init; }

    public required double PerSecond { get; init; }

    public required double P50Ms { get; init; }

    public required double P95Ms { get; init; }

    public required double P99Ms { get; init; }

    public required long LastSeenUnixMs { get; init; }
}

/// <summary>How many of one domain's calls came back with one HTTP status code.</summary>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record ExternalStatusCodeCount
{
    /// <summary><c>http.response.status_code</c>, else <c>http.status_code</c> - as the attribute carried it.</summary>
    public required string StatusCode { get; init; }

    public required ulong CallCount { get; init; }
}

/// <summary>One of our services' calls to one domain.</summary>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record ExternalCallerStats
{
    public required string ServiceName { get; init; }

    public required ulong CallCount { get; init; }

    public required ulong ErrorCount { get; init; }

    public required double PerSecond { get; init; }

    public required double P50Ms { get; init; }

    public required double P95Ms { get; init; }

    public required double P99Ms { get; init; }
}

/// <summary>One kind of failed call to one domain: same endpoint, status code and <c>error.type</c>.</summary>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record ExternalErrorGroup
{
    public required string Method { get; init; }

    public required string Endpoint { get; init; }

    public required ExternalEndpointSource EndpointSource { get; init; }

    /// <summary>Empty when the call never got a response (a timeout, a refused connection).</summary>
    public required string StatusCode { get; init; }

    /// <summary><c>error.type</c> - an exception type name, or the status code again for an HTTP error. Empty when unset.</summary>
    public required string ErrorType { get; init; }

    public required ulong CallCount { get; init; }

    public required long LastSeenUnixMs { get; init; }

    /// <summary>One failed span's status description, when any carried one.</summary>
    public required string SampleMessage { get; init; }
}

/// <summary>Response body for <c>POST /api/external-apis/domain-detail</c>. Hand-written on the MemoryPack TS side, same reason as <see cref="ExternalDomainsResponse"/>.</summary>
[MemoryPackable]
public sealed partial record ExternalDomainDetailResponse
{
    /// <summary>Echoed back from the request, so the drill-down titles itself off the response.</summary>
    public required string Domain { get; init; }

    public required int WindowMinutes { get; init; }

    /// <summary>Busiest first.</summary>
    public required IReadOnlyList<ExternalEndpointStats> Endpoints { get; init; }

    /// <summary>Numeric order. Empty when no call carried a status code.</summary>
    public required IReadOnlyList<ExternalStatusCodeCount> StatusCodes { get; init; }

    /// <summary>Busiest first.</summary>
    public required IReadOnlyList<ExternalCallerStats> Callers { get; init; }

    /// <summary>Most frequent first, at most <see cref="Query.ExternalApiQueryBuilder.MaxErrorGroups"/>.</summary>
    public required IReadOnlyList<ExternalErrorGroup> TopErrors { get; init; }
}
