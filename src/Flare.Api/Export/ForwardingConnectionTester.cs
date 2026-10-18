using System.Net;
using Flare.Api.Model;

namespace Flare.Api.Export;

/// <summary>
/// Probes an OTLP/HTTP forwarding destination the way Flare.Ingest will deliver to it: an empty protobuf
/// <c>ExportLogsServiceRequest</c> (a valid export of nothing) POSTed to <c>{endpoint}/v1/logs</c> with the target's
/// headers. Nothing reaches the destination's data, but a wrong host, TLS problem, or rejected credential shows up.
/// </summary>
public static class ForwardingConnectionTester
{
    public const string HttpClientName = "forwarding-test";

    public static string ProbeUrl(string endpoint) => $"{endpoint.Trim().TrimEnd('/')}/v1/logs";

    public static async Task<ForwardingTestResult> TestAsync(
        HttpClient http, string endpoint, IReadOnlyDictionary<string, string> headers, CancellationToken cancellationToken)
    {
        var url = ProbeUrl(endpoint);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new ByteArrayContent([]) };
            request.Content.Headers.ContentType = new("application/x-protobuf");
            foreach (var (name, value) in headers)
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }

            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var status = (int)response.StatusCode;
            return new ForwardingTestResult
            {
                Success = response.IsSuccessStatusCode,
                StatusCode = status,
                Url = url,
                Error = response.IsSuccessStatusCode ? "" : Describe(response.StatusCode, status),
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return new ForwardingTestResult
            {
                Success = false,
                StatusCode = 0,
                Url = url,
                Error = ex is HttpRequestException ? ex.Message : "The destination did not answer in time.",
            };
        }
    }

    private static string Describe(HttpStatusCode code, int status) => code switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => $"HTTP {status}: the destination rejected the credentials.",
        HttpStatusCode.NotFound => $"HTTP {status}: no OTLP/HTTP receiver at this URL. Check the endpoint path.",
        HttpStatusCode.TooManyRequests => $"HTTP {status}: the destination is rate limiting.",
        >= HttpStatusCode.MultipleChoices and < HttpStatusCode.BadRequest => $"HTTP {status}: the destination redirected the request; use its final URL.",
        _ => $"HTTP {status}",
    };
}
