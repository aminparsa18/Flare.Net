using System.Text.Json;
using System.Text.Json.Nodes;
using Flare.Benchmarks.Fixtures;
using Flare.Benchmarks.Fixtures.Synthetic;

namespace Flare.Benchmarks.Export;

/// <summary>
/// <c>dotnet run -c Release --project src/Flare.Benchmarks -- export &lt;dir&gt;</c>: writes every
/// scenario's response body exactly as Flare.Api would send it - System.Text.Json and MemoryPack,
/// each uncompressed and under every codec - plus <c>manifest.json</c> with the byte sizes.
/// </summary>
/// <remarks>
/// This is the single source of fixture bytes for the TypeScript benchmarks, so the dashboard
/// side decodes what the server really produces (STJ's timestamp format, escaping and field set),
/// not a <c>JSON.stringify</c> of a TypeScript object.
/// </remarks>
public static class FixtureExporter
{
    public static void Export(string directory)
    {
        Directory.CreateDirectory(directory);
        var scenarios = new JsonArray();

        foreach (var scenario in Scenarios.All)
        {
            scenarios.Add(Write(directory, scenario, Compression.All));
        }

        var manifest = new JsonObject
        {
            ["codecs"] = new JsonArray(Compression.All.Select(c => (JsonNode)c).ToArray()),
            ["scenarios"] = scenarios,
            ["request"] = Write(directory, Scenarios.LogSearchRequest, [Compression.None]),
            ["otlpRequests"] = OtlpRequestSizes(),
        };

        File.WriteAllText(Path.Combine(directory, "manifest.json"), manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Exported {Scenarios.All.Count} scenarios + 1 request body to {Path.GetFullPath(directory)}");
    }

    /// <summary>Byte sizes only: the OTLP request fixtures are .NET-to-.NET, so no TypeScript benchmark reads their bytes.</summary>
    private static JsonArray OtlpRequestSizes() => new(OtlpRequestFixtures.Names.Select(name =>
    {
        var body = OtlpRequestFixtures.Encode(name);
        var sizes = new JsonObject();
        foreach (var codec in OtlpRequestFixtures.Codecs)
        {
            sizes[codec] = OtlpRequestFixtures.Compress(body, codec).Length;
        }

        return (JsonNode)new JsonObject
        {
            ["name"] = name,
            ["records"] = OtlpRequestFixtures.BatchSize,
            ["sizes"] = sizes,
        };
    }).ToArray());

    private static JsonObject Write(string root, Scenario scenario, string[] codecs)
    {
        var dir = Path.Combine(root, scenario.Name);
        Directory.CreateDirectory(dir);
        var sizes = new JsonObject();

        foreach (var (format, body) in new[] { ("json", scenario.EncodeJson()), ("memorypack", scenario.EncodeMemoryPack()) })
        {
            var perCodec = new JsonObject();
            foreach (var codec in codecs)
            {
                var bytes = Compression.Compress(body, codec);
                File.WriteAllBytes(Path.Combine(dir, $"{format}.{codec}"), bytes);
                perCodec[codec] = bytes.Length;
            }

            sizes[format] = perCodec;
        }

        return new JsonObject
        {
            ["name"] = scenario.Name,
            ["source"] = scenario.Source,
            ["description"] = scenario.Description,
            ["endpoint"] = scenario.Endpoint,
            ["shape"] = scenario.Shape,
            ["sizes"] = sizes,
        };
    }
}
