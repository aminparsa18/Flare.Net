using System.IO.Compression;
using System.Text;
using Flare.Api.Hosting;
using Flare.Api.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flare.Api.Tests.Hosting;

/// <summary>
/// Runs the real <see cref="ResponseCompressionMiddleware"/> with Flare's registration against an
/// in-memory response - no server.
/// </summary>
public sealed class ResponseCompressionTests
{
    private const string BrowserAcceptEncoding = "gzip, deflate, br, zstd";
    private static readonly byte[] Body = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("{\"service\":\"checkout\",\"level\":\"Error\"},", 200)));

    private static async Task<(HttpContext Http, byte[] Output)> RunAsync(
        string contentType, string? acceptEncoding = BrowserAcceptEncoding, bool https = false, ResponseCompressionSettings? settings = null)
    {
        var services = new ServiceCollection().AddLogging().AddFlareResponseCompression(settings ?? new ResponseCompressionSettings()).BuildServiceProvider();
        var middleware = new ResponseCompressionMiddleware(async ctx =>
        {
            ctx.Response.ContentType = contentType;
            await ctx.Response.Body.WriteAsync(Body);
        }, services.GetRequiredService<IResponseCompressionProvider>());

        var http = new DefaultHttpContext { RequestServices = services };
        http.Request.Scheme = https ? "https" : "http";
        if (acceptEncoding is not null)
        {
            http.Request.Headers.AcceptEncoding = acceptEncoding;
        }

        var output = new MemoryStream();
        http.Response.Body = output;
        await middleware.Invoke(http);
        return (http, output.ToArray());
    }

    private static byte[] Gunzip(byte[] data)
    {
        using var gzip = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
        using var result = new MemoryStream();
        gzip.CopyTo(result);
        return result.ToArray();
    }

    [Theory]
    [InlineData(ApiSerialization.MemoryPackContentType)]
    [InlineData("application/json; charset=utf-8")]
    [InlineData("text/csv; charset=utf-8")]
    [InlineData("application/x-ndjson; charset=utf-8")]
    public async Task BrowserRequest_IsGzipped_EvenThoughItOffersBrotli(string contentType)
    {
        var (http, output) = await RunAsync(contentType);

        Assert.Equal("gzip", http.Response.Headers.ContentEncoding.ToString());
        Assert.Contains("Accept-Encoding", http.Response.Headers.Vary.ToString());
        Assert.True(output.Length < Body.Length / 4);
        Assert.Equal(Body, Gunzip(output));
    }

    [Fact]
    public async Task HttpsRequest_IsCompressedToo()
    {
        var (http, _) = await RunAsync("application/json", https: true);
        Assert.Equal("gzip", http.Response.Headers.ContentEncoding.ToString());
    }

    [Theory]
    [InlineData("text/event-stream")] // MCP's SSE stream must not sit in a compressor's buffer
    [InlineData("application/octet-stream")]
    public async Task OtherContentTypes_AreLeftAlone(string contentType)
    {
        var (http, output) = await RunAsync(contentType);
        Assert.False(http.Response.Headers.ContainsKey("Content-Encoding"));
        Assert.Equal(Body, output);
    }

    [Theory]
    [InlineData(null)] // HttpClient (CLI, MCP loopback) sends no Accept-Encoding by default
    [InlineData("br")]
    public async Task ClientWithoutGzip_GetsTheBodyUncompressed(string? acceptEncoding)
    {
        var (http, output) = await RunAsync("application/json", acceptEncoding);
        Assert.False(http.Response.Headers.ContainsKey("Content-Encoding"));
        Assert.Equal(Body, output);
    }

    [Fact]
    public void Level_DefaultsToFastest_AndIsConfigurable()
    {
        static CompressionLevel LevelFor(ResponseCompressionSettings settings) =>
            new ServiceCollection().AddFlareResponseCompression(settings).BuildServiceProvider()
                .GetRequiredService<IOptions<GzipCompressionProviderOptions>>().Value.Level;

        Assert.Equal(CompressionLevel.Fastest, LevelFor(new ResponseCompressionSettings()));
        Assert.Equal(CompressionLevel.Optimal, LevelFor(new ResponseCompressionSettings { Level = CompressionLevel.Optimal }));
    }

    [Fact]
    public void Disabled_RegistersNothing()
    {
        var services = new ServiceCollection().AddFlareResponseCompression(new ResponseCompressionSettings { Enabled = false }).BuildServiceProvider();
        Assert.Null(services.GetService<IResponseCompressionProvider>());
    }
}
