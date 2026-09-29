using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace ExampleApp.Seeder;

/// <summary>
/// The Flare.Api objects a scenario owns - dashboards, saved views, pipeline rules. They're
/// found again by name (in every locale the seeder knows), so re-seeding in another locale
/// replaces them instead of piling up copies.
/// </summary>
public sealed class FlareApiClient(HttpClient http, Uri baseUri, string? token)
{
    public enum Kind
    {
        Dashboards,
        Views,
        PipelineRules,
    }

    private static (string Path, string ListProperty) Route(Kind kind) => kind switch
    {
        Kind.Dashboards => ("api/dashboards", "dashboards"),
        Kind.Views => ("api/views", "views"),
        Kind.PipelineRules => ("api/pipeline-rules", "rules"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>Deletes every object of <paramref name="kind"/> whose name is in <paramref name="names"/>.</summary>
    public async Task<int> DeleteByNameAsync(Kind kind, IEnumerable<string> names, CancellationToken ct)
    {
        var (path, listProperty) = Route(kind);
        var wanted = names.ToHashSet(StringComparer.Ordinal);
        var list = await SendAsync(HttpMethod.Get, path, null, ct);
        var deleted = 0;
        foreach (var item in list?[listProperty]?.AsArray() ?? [])
        {
            if (item?["name"]?.GetValue<string>() is { } name && wanted.Contains(name))
            {
                await SendAsync(HttpMethod.Delete, $"{path}/{item["id"]}", null, ct);
                deleted++;
            }
        }

        return deleted;
    }

    public async Task<string> CreateAsync(Kind kind, JsonObject body, CancellationToken ct)
    {
        var created = await SendAsync(HttpMethod.Post, Route(kind).Path, body, ct);
        return created?["id"]?.ToString() ?? "?";
    }

    private async Task<JsonNode?> SendAsync(HttpMethod method, string path, JsonObject? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, new Uri(baseUri, path));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var response = await http.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            var hint = response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden
                ? " - Flare has auth on: pass a personal access token with --token (a Member or Admin's)."
                : "";
            throw new InvalidOperationException($"{method} {request.RequestUri} failed with {(int)response.StatusCode}{hint} {text}");
        }

        return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
    }
}
