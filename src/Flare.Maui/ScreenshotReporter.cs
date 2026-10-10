using System.Diagnostics;
using System.Net.Http.Headers;
using OpenTelemetry;

namespace Flare.Maui;

/// <summary>
/// Uploads an error screenshot to <c>POST /v1/screenshots</c> after an exception span was reported
/// (ADR-0174). The capture itself is platform code, passed in as <paramref name="capture"/>. Best effort and
/// rate limited: a failure never reaches the app, and an exception loop cannot upload more than
/// <see cref="MaxPerSession"/> images or more than one per <see cref="MinInterval"/>.
/// </summary>
internal sealed class ScreenshotReporter(
    FlareMauiOptions options,
    string sessionId,
    Func<CancellationToken, Task<byte[]?>> capture,
    HttpClient http,
    TimeProvider? time = null)
{
    internal const int MaxPerSession = 5;
    internal static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(10);
    internal const string ContentType = "image/jpeg";

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly object _gate = new();
    private int _sent;
    private DateTimeOffset _last = DateTimeOffset.MinValue;

    /// <summary>Capture and upload; when <paramref name="wait"/> is set, block up to that long (a fatal crash is about to end the process).</summary>
    public void Report(string traceId, string spanId, TimeSpan? wait)
    {
        if (!TryReserve()) return;
        var work = Task.Run(() => CaptureAndUploadAsync(traceId, spanId));
        if (wait is { } w)
        {
            try { work.Wait(w); } catch { /* best effort */ }
        }
    }

    internal async Task CaptureAndUploadAsync(string traceId, string spanId)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var image = await capture(timeout.Token).ConfigureAwait(false);
            if (image is not { Length: > 0 } || image.Length > options.ScreenshotMaxBytes) return;

            var query = $"?service={Uri.EscapeDataString(options.ServiceName!)}&session_id={sessionId}&trace_id={traceId}&span_id={spanId}";
            using var request = new HttpRequestMessage(HttpMethod.Post, options.Endpoint!.AbsoluteUri.TrimEnd('/') + "/v1/screenshots" + query)
            {
                Content = new ByteArrayContent(image) { Headers = { ContentType = new MediaTypeHeaderValue(ContentType) } },
            };
            if (!string.IsNullOrEmpty(options.IngestKey))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.IngestKey);

            using var _ = SuppressInstrumentationScope.Begin();
            using var response = await http.SendAsync(request, timeout.Token).ConfigureAwait(false);
        }
        catch
        {
            // Best effort: a screenshot is a bonus on top of the exception span, which is already exported.
        }
    }

    private bool TryReserve()
    {
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            if (_sent >= MaxPerSession || now - _last < MinInterval) return false;
            _sent++;
            _last = now;
            return true;
        }
    }
}
