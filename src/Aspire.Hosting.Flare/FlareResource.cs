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
public sealed class FlareResource(string name) : Resource(name), IResourceWithoutLifetime;
