namespace Flare.Api.Demo;

/// <summary>
/// Endpoint metadata: this endpoint only reads, even though its HTTP method isn't GET (a POST search
/// with a structured filter body), so the demo user may call it (ADR-0278). Every other non-GET
/// endpoint is refused to the demo user by <see cref="DemoMiddleware"/>, so forgetting this tag on a
/// new query endpoint breaks that endpoint in the demo instead of opening a write.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AllowInDemoAttribute : Attribute;

public static class AllowInDemoExtensions
{
    public static TBuilder AllowInDemo<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new AllowInDemoAttribute());
}
