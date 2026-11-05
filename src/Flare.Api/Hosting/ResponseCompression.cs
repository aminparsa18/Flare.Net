using System.IO.Compression;
using Flare.Api.Json;
using Microsoft.AspNetCore.ResponseCompression;

namespace Flare.Api.Hosting;

/// <summary>
/// <c>ResponseCompression:*</c> (ADR-0277): gzip for Flare.Api's responses through ASP.NET Core's
/// built-in <c>UseResponseCompression</c>.
/// </summary>
public sealed class ResponseCompressionSettings
{
    public const string SectionName = "ResponseCompression";

    /// <summary>Master switch. Set <c>false</c> when a reverse proxy compresses instead.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// gzip level. <see cref="CompressionLevel.Fastest"/> (gzip 1 on .NET 10) was the fastest end to
    /// end at 1 Gbps and within 3% (MemoryPack) / 14% (JSON) of the best codec at 100 Mbps;
    /// <see cref="CompressionLevel.Optimal"/> (gzip 6) only pays for its extra CPU on links of about
    /// 20 Mbps and slower.
    /// </summary>
    public CompressionLevel Level { get; set; } = CompressionLevel.Fastest;
}

public static class FlareResponseCompression
{
    /// <summary>
    /// The framework defaults (JSON, text, XML, ...) plus Flare's own: MemoryPack (the dashboard's
    /// format, ADR-0015) and the CSV/NDJSON exports. <c>text/event-stream</c> is deliberately absent:
    /// the MCP endpoint's SSE stream must reach the client event by event.
    /// </summary>
    public static readonly IReadOnlyList<string> MimeTypes =
    [
        .. ResponseCompressionDefaults.MimeTypes,
        ApiSerialization.MemoryPackContentType,
        "text/csv",
        "application/x-ndjson",
    ];

    /// <summary>
    /// Registers gzip only, so every client that sends <c>Accept-Encoding: gzip</c> gets the level the
    /// benchmark chose (browsers list <c>br</c> too, and the framework default would pick Brotli).
    /// <c>EnableForHttps</c> is on; ADR-0277 records why BREACH does not apply to Flare.Api.
    /// </summary>
    public static IServiceCollection AddFlareResponseCompression(this IServiceCollection services, ResponseCompressionSettings settings)
    {
        if (!settings.Enabled)
        {
            return services;
        }

        services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
            options.MimeTypes = MimeTypes;
            options.Providers.Add<GzipCompressionProvider>();
        });
        services.Configure<GzipCompressionProviderOptions>(options => options.Level = settings.Level);
        return services;
    }

    public static IApplicationBuilder UseFlareResponseCompression(this IApplicationBuilder app, ResponseCompressionSettings settings) =>
        settings.Enabled ? app.UseResponseCompression() : app;
}
