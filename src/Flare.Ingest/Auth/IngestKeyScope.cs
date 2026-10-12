using OpenTelemetry.Proto.Resource.V1;

namespace Flare.Ingest.Auth;

/// <summary>Enforces an ingest key's service allowlist (ADR-0150) on a parsed export, before
/// anything is written. A restricted key sending a resource with a missing or unlisted
/// <c>service.name</c> has the whole request refused, so a leaked key can't write as another service.</summary>
public static class IngestKeyScope
{
    /// <summary>True when the request carries a key with an allowlist and any resource falls outside it.
    /// False with no key (auth off, the static key, a test calling a receiver directly) or no allowlist.</summary>
    public static bool RejectsServices(HttpContext? context, IEnumerable<Resource?> resources)
    {
        var allowed = context?.Features.Get<IngestKeyUsageFeature>()?.AllowedServices;
        if (allowed is not { Count: > 0 })
        {
            return false;
        }

        return resources.Any(r => !(OtlpServiceName(r) is { } name && allowed.Contains(name)));
    }

    /// <summary>The single-service form of <see cref="RejectsServices"/>, for non-OTLP endpoints that carry a service name directly.</summary>
    public static bool RejectsService(HttpContext? context, string service)
    {
        var allowed = context?.Features.Get<IngestKeyUsageFeature>()?.AllowedServices;
        return allowed is { Count: > 0 } && !allowed.Contains(service);
    }

    private static string? OtlpServiceName(Resource? resource) =>
        Otlp.OtlpAnyValue.Flatten(resource?.Attributes).GetValueOrDefault("service.name");
}
