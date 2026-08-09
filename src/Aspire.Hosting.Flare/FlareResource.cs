// For ease of discovery, resource types live in the Aspire.Hosting.ApplicationModel
// namespace (same convention Aspire's own "Create custom hosting integrations" doc uses).
namespace Aspire.Hosting.ApplicationModel;

/// <summary>
/// A composite resource representing the whole Flare stack - ClickHouse, Redis, the OTLP
/// ingest receiver, the query API, and the dashboard - as a single named group in the Aspire
/// dashboard. Added via <see cref="Aspire.Hosting.FlareResourceBuilderExtensions.AddFlare"/>.
/// </summary>
/// <remarks>
/// This resource has no process of its own - it implements <see cref="IResourceWithoutLifetime"/>
/// ("resources that are just holders of data or references to other resources") because it's
/// purely a grouping node: the five real backing resources attach to it via
/// <c>WithParentRelationship</c> so they nest under one entry in the dashboard instead of
/// appearing as five unrelated top-level resources.
/// </remarks>
public sealed class FlareResource(string name) : Resource(name), IResourceWithoutLifetime, IResourceWithConnectionString
{
    // Populated by AddFlare once the private "ingest" container sub-resource exists. Can't be
    // self-constructed lazily the way e.g. Aspire.Hosting.Seq's SeqResource.PrimaryEndpoint is,
    // because FlareResource doesn't own these endpoints itself - "ingest" does - so the
    // EndpointReference has to be handed in from AddFlare rather than built from `this`.
    private EndpointReference? _otlpGrpcEndpoint;
    private EndpointReference? _otlpHttpEndpoint;
    private string? _dashboardResourceName;

    /// <summary>
    /// Wires this resource's endpoint references to Flare.Ingest's actual OTLP endpoints.
    /// Called once by <see cref="Aspire.Hosting.FlareResourceBuilderExtensions.AddFlare"/>
    /// after the ingest container is created.
    /// </summary>
    internal void SetIngestEndpoints(EndpointReference grpc, EndpointReference http)
    {
        _otlpGrpcEndpoint = grpc;
        _otlpHttpEndpoint = http;
    }

    /// <summary>
    /// Records the dashboard sub-resource's Aspire resource name, so
    /// <see cref="Aspire.Hosting.FlareResourceBuilderExtensions.WaitForFlare{TDestination}"/> can
    /// look it up by name later without the caller needing to hold onto their own reference to it.
    /// Called once by <see cref="Aspire.Hosting.FlareResourceBuilderExtensions.AddFlare"/> after
    /// the dashboard container is created.
    /// </summary>
    internal void SetDashboardResourceName(string dashboardResourceName)
    {
        _dashboardResourceName = dashboardResourceName;
    }

    /// <summary>The dashboard sub-resource's Aspire resource name (e.g. <c>"flare-dashboard"</c>).</summary>
    internal string DashboardResourceName => _dashboardResourceName
        ?? throw new InvalidOperationException(
            $"{nameof(DashboardResourceName)} isn't available until {nameof(Aspire.Hosting.FlareResourceBuilderExtensions.AddFlare)} has finished configuring this resource.");

    /// <summary>Endpoint reference for Flare.Ingest's OTLP gRPC endpoint (4317 by default).</summary>
    public EndpointReference OtlpGrpcEndpoint => _otlpGrpcEndpoint
        ?? throw new InvalidOperationException(
            $"{nameof(OtlpGrpcEndpoint)} isn't available until {nameof(Aspire.Hosting.FlareResourceBuilderExtensions.AddFlare)} has finished configuring this resource.");

    /// <summary>Endpoint reference for Flare.Ingest's OTLP HTTP endpoint (4318 by default).</summary>
    public EndpointReference OtlpHttpEndpoint => _otlpHttpEndpoint
        ?? throw new InvalidOperationException(
            $"{nameof(OtlpHttpEndpoint)} isn't available until {nameof(Aspire.Hosting.FlareResourceBuilderExtensions.AddFlare)} has finished configuring this resource.");

    /// <summary>
    /// The connection string consumers get via <c>.WithReference(flare)</c> - Flare.Ingest's
    /// OTLP gRPC URL (e.g. <c>http://localhost:4317</c>), resolved per execution context the
    /// same way any other Aspire endpoint reference is. gRPC because it's the default protocol
    /// OpenTelemetry .NET's OTLP exporter uses; use <see cref="OtlpHttpEndpoint"/> directly (or
    /// <see cref="Aspire.Hosting.FlareResourceBuilderExtensions.WithOtlpEndpoint{TDestination}"/> with
    /// <c>useHttp: true</c>) for the HTTP endpoint instead.
    /// </summary>
    public ReferenceExpression ConnectionStringExpression =>
        ReferenceExpression.Create($"{OtlpGrpcEndpoint.Property(EndpointProperty.Url)}");

    /// <summary>
    /// Host/Port/Uri breakdown of <see cref="OtlpGrpcEndpoint"/>, surfaced the same way
    /// Aspire.Hosting.Seq's <c>SeqResource</c> does for its own connection string - lets
    /// tooling (and the future <c>Flare.Aspire</c> client package) read structured pieces
    /// instead of parsing the connection string.
    /// </summary>
    IEnumerable<KeyValuePair<string, ReferenceExpression>> IResourceWithConnectionString.GetConnectionProperties()
    {
        yield return new("Host", ReferenceExpression.Create($"{OtlpGrpcEndpoint.Property(EndpointProperty.Host)}"));
        yield return new("Port", ReferenceExpression.Create($"{OtlpGrpcEndpoint.Property(EndpointProperty.Port)}"));
        yield return new("Uri", ConnectionStringExpression);
    }
}
