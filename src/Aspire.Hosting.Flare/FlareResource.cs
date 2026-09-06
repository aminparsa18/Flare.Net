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
    private string? _ingestResourceName;
    private string? _apiResourceName;
    private string? _clickHouseResourceName;
    private string? _redisResourceName;
    private string? _imageTag;
    private bool _persistentStorageConfigured;

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

    /// <summary>
    /// Records the ingest sub-resource's Aspire resource name, so the <c>With*</c> chain methods
    /// on <see cref="Aspire.Hosting.FlareResourceBuilderExtensions"/> (<c>WithIngestGrpcPort</c>,
    /// <c>WithIngestHttpPort</c>, <c>WithIngestImage</c>, <c>WithApiKey</c>) can reach back into it
    /// after <see cref="Aspire.Hosting.FlareResourceBuilderExtensions.AddFlare"/> has already
    /// returned - same reasoning as <see cref="SetDashboardResourceName"/>.
    /// </summary>
    internal void SetIngestResourceName(string ingestResourceName)
    {
        _ingestResourceName = ingestResourceName;
    }

    /// <summary>The ingest sub-resource's Aspire resource name (e.g. <c>"flare-ingest"</c>).</summary>
    internal string IngestResourceName => _ingestResourceName
        ?? throw new InvalidOperationException(
            $"{nameof(IngestResourceName)} isn't available until {nameof(Aspire.Hosting.FlareResourceBuilderExtensions.AddFlare)} has finished configuring this resource.");

    /// <summary>
    /// Records the api sub-resource's Aspire resource name, so the <c>With*</c> chain methods
    /// (<c>WithApiPort</c>, <c>WithApiImage</c>) can reach back into it after
    /// <see cref="Aspire.Hosting.FlareResourceBuilderExtensions.AddFlare"/> has already returned -
    /// same reasoning as <see cref="SetDashboardResourceName"/>.
    /// </summary>
    internal void SetApiResourceName(string apiResourceName)
    {
        _apiResourceName = apiResourceName;
    }

    /// <summary>The api sub-resource's Aspire resource name (e.g. <c>"flare-api"</c>).</summary>
    internal string ApiResourceName => _apiResourceName
        ?? throw new InvalidOperationException(
            $"{nameof(ApiResourceName)} isn't available until {nameof(Aspire.Hosting.FlareResourceBuilderExtensions.AddFlare)} has finished configuring this resource.");

    /// <summary>
    /// Records the ClickHouse sub-resource's Aspire resource name, so
    /// <see cref="Aspire.Hosting.FlareResourceBuilderExtensions.WithPersistentStorage"/> can reach
    /// back into it after <see cref="Aspire.Hosting.FlareResourceBuilderExtensions.AddFlare"/> has
    /// already returned - same reasoning as <see cref="SetDashboardResourceName"/>.
    /// </summary>
    internal void SetClickHouseResourceName(string clickHouseResourceName)
    {
        _clickHouseResourceName = clickHouseResourceName;
    }

    /// <summary>The ClickHouse sub-resource's Aspire resource name (e.g. <c>"flare-clickhouse"</c>).</summary>
    internal string ClickHouseResourceName => _clickHouseResourceName
        ?? throw new InvalidOperationException(
            $"{nameof(ClickHouseResourceName)} isn't available until {nameof(Aspire.Hosting.FlareResourceBuilderExtensions.AddFlare)} has finished configuring this resource.");

    /// <summary>
    /// Records the Redis sub-resource's Aspire resource name, so
    /// <see cref="Aspire.Hosting.FlareResourceBuilderExtensions.WithPersistentStorage"/> can reach
    /// back into it after <see cref="Aspire.Hosting.FlareResourceBuilderExtensions.AddFlare"/> has
    /// already returned - same reasoning as <see cref="SetDashboardResourceName"/>.
    /// </summary>
    internal void SetRedisResourceName(string redisResourceName)
    {
        _redisResourceName = redisResourceName;
    }

    /// <summary>The Redis sub-resource's Aspire resource name (e.g. <c>"flare-redis"</c>).</summary>
    internal string RedisResourceName => _redisResourceName
        ?? throw new InvalidOperationException(
            $"{nameof(RedisResourceName)} isn't available until {nameof(Aspire.Hosting.FlareResourceBuilderExtensions.AddFlare)} has finished configuring this resource.");

    /// <summary>
    /// Marks that <see cref="Aspire.Hosting.FlareResourceBuilderExtensions.WithPersistentStorage"/>
    /// has bound real Kubernetes persistent volumes to this Flare instance's storage - read back by
    /// the deferred ephemeral-storage warning
    /// (<see cref="Aspire.Hosting.FlareResourceBuilderExtensions.AddFlare"/>'s
    /// <c>BeforePublishEvent</c> subscription) to silence it. A plain bool flag rather than
    /// inspecting the actual Kubernetes annotations <c>WithPersistentVolume</c> attaches, because
    /// those annotation types are internal to <c>Aspire.Hosting.Kubernetes</c> - this package has no
    /// public API to introspect them, so it tracks its own record of "was the supported path used"
    /// instead. Consequently: hand-wiring <c>AddPersistentVolume</c>/<c>WithPersistentVolume</c>
    /// directly onto the sub-resources (bypassing <c>WithPersistentStorage</c>) still triggers the
    /// warning, same as before this method existed.
    /// </summary>
    internal void MarkPersistentStorageConfigured()
    {
        _persistentStorageConfigured = true;
    }

    /// <summary>Whether <see cref="Aspire.Hosting.FlareResourceBuilderExtensions.WithPersistentStorage"/> has been called for this Flare instance.</summary>
    internal bool PersistentStorageConfigured => _persistentStorageConfigured;

    /// <summary>
    /// Records the <c>imageTag</c> passed to <see cref="Aspire.Hosting.FlareResourceBuilderExtensions.AddFlare"/>,
    /// so <c>WithIngestImage</c>/<c>WithApiImage</c>/<c>WithDashboardImage</c> can keep reusing it
    /// when a consumer overrides just the image name/registry, not the tag - the same "override the
    /// name, keep the shared tag" split <c>AddFlare</c>'s own <c>ingestImage</c>/<c>apiImage</c>/
    /// <c>dashboardImage</c> parameters used before this type became chain methods.
    /// </summary>
    internal void SetImageTag(string imageTag)
    {
        _imageTag = imageTag;
    }

    /// <summary>The shared image tag passed to <see cref="Aspire.Hosting.FlareResourceBuilderExtensions.AddFlare"/>.</summary>
    internal string ImageTag => _imageTag
        ?? throw new InvalidOperationException(
            $"{nameof(ImageTag)} isn't available until {nameof(Aspire.Hosting.FlareResourceBuilderExtensions.AddFlare)} has finished configuring this resource.");

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
