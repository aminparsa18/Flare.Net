using Flare.Ingest.Auth;
using Microsoft.AspNetCore.Http;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Resource.V1;
using Xunit;

namespace Flare.Ingest.Tests.Auth;

public class IngestKeyScopeTests
{
    private static Resource ServiceResource(string? name)
    {
        var resource = new Resource();
        if (name is not null)
        {
            resource.Attributes.Add(new KeyValue { Key = "service.name", Value = new AnyValue { StringValue = name } });
        }
        return resource;
    }

    private static DefaultHttpContext ContextWith(params string[] allowed)
    {
        var context = new DefaultHttpContext();
        context.Features.Set(new IngestKeyUsageFeature(Guid.NewGuid()) { AllowedServices = allowed.ToHashSet() });
        return context;
    }

    [Fact]
    public void RejectsServices_IsFalse_WithoutAKeyOrAllowlist()
    {
        Assert.False(IngestKeyScope.RejectsServices(null, [ServiceResource("anything")]));
        Assert.False(IngestKeyScope.RejectsServices(new DefaultHttpContext(), [ServiceResource("anything")]));
        Assert.False(IngestKeyScope.RejectsServices(ContextWith(), [ServiceResource(null)]));
    }

    [Fact]
    public void RejectsServices_AcceptsOnlyListedServices()
    {
        var context = ContextWith("web", "checkout");

        Assert.False(IngestKeyScope.RejectsServices(context, [ServiceResource("web"), ServiceResource("checkout")]));
        Assert.True(IngestKeyScope.RejectsServices(context, [ServiceResource("web"), ServiceResource("billing")]));
        Assert.True(IngestKeyScope.RejectsServices(context, [ServiceResource("Web")]));
    }

    [Fact]
    public void RejectsServices_TreatsAMissingServiceNameAsOutsideTheList()
    {
        var context = ContextWith("web");

        Assert.True(IngestKeyScope.RejectsServices(context, [ServiceResource(null)]));
        Assert.True(IngestKeyScope.RejectsServices(context, [null]));
    }
}
