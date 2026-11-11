using Xunit;
using Flare.Api.OpenApi;
using Microsoft.OpenApi;

namespace Flare.Api.Tests;

public class FlareOpenApiTransformersTests
{
    private static async Task<OpenApiDocument> TransformAsync()
    {
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "t", Version = "1" },
            Paths = new OpenApiPaths
            {
                ["/api/logs/search"] = new OpenApiPathItem
                {
                    Operations = new Dictionary<HttpMethod, OpenApiOperation> { [HttpMethod.Post] = new() },
                },
                ["/api/users"] = new OpenApiPathItem
                {
                    Operations = new Dictionary<HttpMethod, OpenApiOperation> { [HttpMethod.Get] = new() },
                },
                ["/health"] = new OpenApiPathItem
                {
                    Operations = new Dictionary<HttpMethod, OpenApiOperation> { [HttpMethod.Get] = new() },
                },
            },
        };
        await FlareOpenApiTransformers.SecuritySchemes("flare_session").TransformAsync(doc, null!, default);
        await FlareOpenApiTransformers.Tags().TransformAsync(doc, null!, default);
        return doc;
    }

    [Fact]
    public async Task Declares_cookie_and_bearer_schemes()
    {
        var doc = await TransformAsync();
        var schemes = doc.Components!.SecuritySchemes!;
        Assert.Equal(ParameterLocation.Cookie, schemes[FlareOpenApiTransformers.CookieScheme].In);
        Assert.Equal("flare_session", schemes[FlareOpenApiTransformers.CookieScheme].Name);
        Assert.Equal("bearer", schemes[FlareOpenApiTransformers.BearerScheme].Scheme);
        Assert.Equal(2, doc.Security!.Count);
    }

    [Fact]
    public async Task Tags_operations_by_api_segment_and_builds_hierarchy()
    {
        var doc = await TransformAsync();
        Assert.Equal("logs", doc.Paths["/api/logs/search"].Operations![HttpMethod.Post].Tags!.Single().Name);
        Assert.Null(doc.Paths["/health"].Operations![HttpMethod.Get].Tags);
        Assert.Contains(doc.Tags!, t => t.Name == "Telemetry");
        Assert.Contains(doc.Tags!, t => t.Name == "Administration");
        Assert.Equal("Telemetry", doc.Tags!.Single(t => t.Name == "logs").Parent!.Name);
    }

    [Theory]
    [InlineData(OpenApiSpecVersion.OpenApi3_1, false)]
    [InlineData(OpenApiSpecVersion.OpenApi3_2, true)]
    public async Task Serializes_tag_hierarchy_only_for_v32(OpenApiSpecVersion version, bool expectParent)
    {
        var json = await (await TransformAsync()).SerializeAsJsonAsync(version);
        Assert.Contains("PersonalAccessToken", json);
        Assert.Equal(expectParent, json.Contains("\"parent\""));
    }

    [Theory]
    [InlineData("/api/alerts/{id}", "alerts")]
    [InlineData("/api", null)]
    [InlineData("/v2/search", null)]
    public void SegmentOf_extracts_second_segment(string path, string? expected) =>
        Assert.Equal(expected, FlareOpenApiTransformers.SegmentOf(path));
}
