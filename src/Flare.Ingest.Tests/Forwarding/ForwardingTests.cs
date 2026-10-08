using Flare.Ingest.Forwarding;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;
using OpenTelemetry.Proto.Resource.V1;
using Xunit;

namespace Flare.Ingest.Tests.Forwarding;

public class ForwardingTests
{
    private static ResourceLogs For(string? service)
    {
        var rl = new ResourceLogs { Resource = new Resource() };
        if (service is not null)
        {
            rl.Resource.Attributes.Add(new KeyValue { Key = "service.name", Value = new AnyValue { StringValue = service } });
        }
        return rl;
    }

    private static ExportLogsServiceRequest Request(params string?[] services)
    {
        var r = new ExportLogsServiceRequest();
        r.ResourceLogs.AddRange(services.Select(For));
        return r;
    }

    [Fact]
    public void Empty_service_set_returns_request_untouched()
    {
        var request = Request("a", "b");
        Assert.Same(request, ForwardingRequestFilter.Filter(request, new HashSet<string>()));
    }

    [Fact]
    public void Keeps_only_listed_services()
    {
        var result = ForwardingRequestFilter.Filter(Request("a", "b", null), new HashSet<string> { "b" });
        Assert.NotNull(result);
        Assert.Single(result.ResourceLogs);
    }

    [Fact]
    public void No_match_returns_null()
    {
        Assert.Null(ForwardingRequestFilter.Filter(Request("a"), new HashSet<string> { "z" }));
    }

    [Theory]
    [InlineData("", "http://x:4318")]
    [InlineData("t", "not-a-url")]
    [InlineData("t", "ftp://x")]
    public void Validate_rejects_bad_targets(string name, string endpoint)
    {
        var options = new ForwardingOptions { Targets = [new() { Name = name, Endpoint = endpoint }] };
        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void Validate_rejects_duplicate_names_and_accepts_valid()
    {
        var ok = new ForwardingTargetOptions { Name = "a", Endpoint = "http://x:4318" };
        new ForwardingOptions { Targets = [ok] }.Validate();
        var dup = new ForwardingOptions { Targets = [ok, new() { Name = "A", Endpoint = "http://y:4318" }] };
        Assert.Throws<InvalidOperationException>(dup.Validate);
    }
}
