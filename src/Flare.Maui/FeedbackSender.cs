using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using OpenTelemetry;

namespace Flare.Maui;

/// <summary>
/// Posts one "report a problem" message to <c>POST /v1/feedback</c> (ADR-0193). Best effort: any failure is a false
/// return, never an exception, so the app can tell the user to try again without a try/catch of its own.
/// </summary>
internal sealed class FeedbackSender(
    FlareMauiOptions options,
    string sessionId,
    HttpClient http,
    Func<string?> userId,
    Func<string?> appVersion)
{
    /// <summary>The server cuts a longer message; stopping here keeps the request well under its size limit.</summary>
    internal const int MaxMessageChars = 4000;

    public async Task<bool> SendAsync(string message, string? email, string? name, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message)) return false;
        message = message.Trim();
        if (message.Length > MaxMessageChars) message = message[..MaxMessageChars];

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, options.Endpoint!.AbsoluteUri.TrimEnd('/') + "/v1/feedback")
            {
                Content = new StringContent(BuildBody(message, email, name), Encoding.UTF8, "application/json"),
            };
            if (!string.IsNullOrEmpty(options.IngestKey))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.IngestKey);

            using var _ = SuppressInstrumentationScope.Begin();
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return false;
        }
    }

    internal string BuildBody(string message, string? email, string? name)
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream))
        {
            json.WriteStartObject();
            json.WriteString("service", options.ServiceName);
            json.WriteString("session_id", sessionId);
            json.WriteString("message", message);
            WriteIfSet(json, "user_id", userId());
            WriteIfSet(json, "name", name);
            WriteIfSet(json, "email", email);
            WriteIfSet(json, "app_version", appVersion());
            json.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteIfSet(Utf8JsonWriter json, string property, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) json.WriteString(property, value.Trim());
    }
}
