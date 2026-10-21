using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Flare.OpenTelemetry;
using Xunit;

namespace Flare.OpenTelemetry.Tests;

public class FlareOpenTelemetryExtensionsTests
{
    [Fact]
    public void ResolveEndpoint_grpc_uses_the_endpoint_as_given()
    {
        var options = new FlareOptions { Endpoint = new Uri("http://flare:4317") };

        Assert.Equal(new Uri("http://flare:4317"), FlareOpenTelemetryExtensions.ResolveEndpoint(options, "v1/traces"));
    }

    [Theory]
    [InlineData("http://flare:4318", "http://flare:4318/v1/traces")]
    [InlineData("http://flare:4318/", "http://flare:4318/v1/traces")]
    [InlineData("https://example.com/flare", "https://example.com/flare/v1/traces")]
    public void ResolveEndpoint_http_appends_the_signal_path(string endpoint, string expected)
    {
        var options = new FlareOptions { Endpoint = new Uri(endpoint), Protocol = OtlpExportProtocol.HttpProtobuf };

        Assert.Equal(new Uri(expected), FlareOpenTelemetryExtensions.ResolveEndpoint(options, "v1/traces"));
    }

    [Fact]
    public void ResolveEndpoint_defaults_to_the_conventional_grpc_port()
    {
        Assert.Equal(new Uri("http://localhost:4317"), FlareOpenTelemetryExtensions.ResolveEndpoint(new FlareOptions(), "v1/logs"));
    }

    [Fact]
    public void CreateSampler_is_null_at_full_sampling()
    {
        Assert.Null(FlareOpenTelemetryExtensions.CreateSampler(1.0));
    }

    [Fact]
    public void CreateSampler_below_one_is_parent_based()
    {
        Assert.IsType<ParentBasedSampler>(FlareOpenTelemetryExtensions.CreateSampler(0.25));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    [InlineData(double.NaN)]
    public void Validate_rejects_a_ratio_outside_zero_to_one(double ratio)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => FlareOpenTelemetryExtensions.Validate(new FlareOptions { TraceSampleRatio = ratio }));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void Validate_accepts_the_inclusive_bounds(double ratio)
    {
        FlareOpenTelemetryExtensions.Validate(new FlareOptions { TraceSampleRatio = ratio });
    }

    [Fact]
    public void AddFlareTelemetry_registers_all_three_signal_providers()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.AddFlareTelemetry("http://localhost:4317", "key");

        using var provider = builder.Services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<TracerProvider>());
        Assert.NotNull(provider.GetService<MeterProvider>());
        Assert.NotNull(provider.GetService<LoggerProvider>());
    }

    // Regression: the three signals once shared one named options instance, so logs and traces
    // were exported to the metrics path. Each signal must resolve its own endpoint.
    [Fact]
    public void AddFlareTelemetry_gives_each_signal_its_own_endpoint_and_key()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddFlareTelemetry(o =>
        {
            o.Endpoint = new Uri("http://flare:4318");
            o.Protocol = OtlpExportProtocol.HttpProtobuf;
            o.ApiKey = "k1";
        });

        using var provider = builder.Services.BuildServiceProvider();
        var monitor = provider.GetRequiredService<IOptionsMonitor<OtlpExporterOptions>>();

        Assert.Equal(new Uri("http://flare:4318/v1/logs"), monitor.Get("flare-logs").Endpoint);
        Assert.Equal(new Uri("http://flare:4318/v1/traces"), monitor.Get("flare-traces").Endpoint);
        Assert.Equal(new Uri("http://flare:4318/v1/metrics"), monitor.Get("flare-metrics").Endpoint);
        Assert.Equal("Authorization=Bearer k1", monitor.Get("flare-traces").Headers);
    }

    [Fact]
    public void AddFlareTelemetry_binds_the_Flare_configuration_section()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Flare:Endpoint"] = "http://flare.internal:4317",
            ["Flare:TraceSampleRatio"] = "0.5",
            ["Flare:ServiceName"] = "orders",
        });

        var options = new FlareOptions();
        builder.Configuration.GetSection(FlareOptions.SectionName).Bind(options);

        Assert.Equal(new Uri("http://flare.internal:4317"), options.Endpoint);
        Assert.Equal(0.5, options.TraceSampleRatio);
        Assert.Equal("orders", options.ServiceName);
        builder.AddFlareTelemetry();
    }

    [Fact]
    public void AddFlareTelemetry_throws_for_an_invalid_ratio()
    {
        var builder = Host.CreateApplicationBuilder();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => builder.AddFlareTelemetry(o => o.TraceSampleRatio = 2));
    }
}
