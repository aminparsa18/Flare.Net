using System.Text.Json;
using System.Text.Json.Serialization;

namespace Flare.Cli.Internal;

/// <summary>
/// Small metadata file at <c>~/.flare/state.json</c> - tracks what `flare update` needs
/// to print a meaningful before/after diff (per-service image digests) instead of just
/// re-running `pull` silently. Not a source of truth for anything Docker Compose itself
/// already tracks (that lives in the compose project state, not here).
/// </summary>
internal sealed class StateMetadata
{
    [JsonPropertyName("imageTag")]
    public string ImageTag { get; set; } = "edge";

    [JsonPropertyName("lastPulled")]
    public Dictionary<string, string> LastPulled { get; set; } = new();

    [JsonPropertyName("lastPulledAt")]
    public DateTimeOffset? LastPulledAt { get; set; }

    [JsonPropertyName("cliVersion")]
    public string CliVersion { get; set; } = "unknown";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    public static StateMetadata Load(string path)
    {
        if (!File.Exists(path))
        {
            return new StateMetadata();
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<StateMetadata>(json, SerializerOptions) ?? new StateMetadata();
    }

    public static void Save(string path, StateMetadata state)
    {
        var json = JsonSerializer.Serialize(state, SerializerOptions);
        File.WriteAllText(path, json);
    }
}
