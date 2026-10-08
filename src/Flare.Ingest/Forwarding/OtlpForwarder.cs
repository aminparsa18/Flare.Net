using System.IO.Compression;
using System.Net;
using System.Threading.Channels;
using Flare.Ingest.Auth;
using Google.Protobuf;
using Microsoft.Extensions.Options;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;

namespace Flare.Ingest.Forwarding;

/// <summary>Hands accepted OTLP exports to the configured forwarding targets (ADR-0155). Never throws and never blocks the receiver.</summary>
public interface IOtlpForwarder
{
    void Forward(HttpContext? http, ExportLogsServiceRequest request);
    void Forward(HttpContext? http, ExportTraceServiceRequest request);
    void Forward(HttpContext? http, ExportMetricsServiceRequest request);
}

/// <summary>
/// One bounded in-memory queue and one sender loop per target. Best effort by design: a full queue
/// or exhausted retries drop the request with a warning, because the primary copy is already safe in
/// Redis and a slow destination must not back-pressure ingest.
/// </summary>
public sealed class OtlpForwarder : BackgroundService, IOtlpForwarder
{
    private readonly record struct Item(ForwardingSignal Signal, byte[] Body);

    private sealed class Target(ForwardingTargetOptions options)
    {
        public ForwardingTargetOptions Options { get; } = options;
        public HashSet<string> Services { get; } = new(options.Services, StringComparer.Ordinal);
        public HashSet<Guid> Keys { get; } = [.. options.IngestKeyIds];
        public HashSet<ForwardingSignal> Signals { get; } = options.Signals.Count == 0 ? [.. Enum.GetValues<ForwardingSignal>()] : [.. options.Signals];
        public Channel<Item> Queue { get; } = Channel.CreateBounded<Item>(
            new BoundedChannelOptions(options.QueueCapacity) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });
        public long Dropped;
    }

    private readonly List<Target> _targets;
    private readonly IHttpClientFactory _httpClients;
    private readonly ILogger<OtlpForwarder> _logger;

    public OtlpForwarder(IOptions<ForwardingOptions> options, IHttpClientFactory httpClients, ILogger<OtlpForwarder> logger)
    {
        options.Value.Validate();
        _targets = options.Value.Targets.Select(t => new Target(t)).ToList();
        _httpClients = httpClients;
        _logger = logger;
    }

    public void Forward(HttpContext? http, ExportLogsServiceRequest request) =>
        Enqueue(http, ForwardingSignal.Logs, t => ForwardingRequestFilter.Filter(request, t.Services));

    public void Forward(HttpContext? http, ExportTraceServiceRequest request) =>
        Enqueue(http, ForwardingSignal.Traces, t => ForwardingRequestFilter.Filter(request, t.Services));

    public void Forward(HttpContext? http, ExportMetricsServiceRequest request) =>
        Enqueue(http, ForwardingSignal.Metrics, t => ForwardingRequestFilter.Filter(request, t.Services));

    private void Enqueue(HttpContext? http, ForwardingSignal signal, Func<Target, IMessage?> select)
    {
        if (_targets.Count == 0) return;
        var keyId = http?.Features.Get<IngestKeyUsageFeature>()?.KeyId;
        foreach (var target in _targets)
        {
            if (!target.Signals.Contains(signal)) continue;
            if (target.Keys.Count > 0 && (keyId is null || !target.Keys.Contains(keyId.Value))) continue;
            try
            {
                if (select(target) is not { } message) continue;
                if (!target.Queue.Writer.TryWrite(new Item(signal, message.ToByteArray())))
                {
                    var dropped = Interlocked.Increment(ref target.Dropped);
                    if (dropped == 1 || dropped % 1000 == 0)
                    {
                        _logger.LogWarning("Forwarding target {Target} queue is full; {Dropped} request(s) dropped so far", target.Options.Name, dropped);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not queue {Signal} for forwarding target {Target}", signal, target.Options.Name);
            }
        }
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(_targets.Select(t => Task.Run(() => RunAsync(t, stoppingToken), stoppingToken)));

    private async Task RunAsync(Target target, CancellationToken ct)
    {
        var client = _httpClients.CreateClient("OtlpForwarding");
        client.Timeout = target.Options.Timeout;
        try
        {
            await foreach (var item in target.Queue.Reader.ReadAllAsync(ct))
            {
                await SendAsync(client, target, item, ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task SendAsync(HttpClient client, Target target, Item item, CancellationToken ct)
    {
        var o = target.Options;
        var url = $"{o.Endpoint.TrimEnd('/')}/v1/{item.Signal.ToString().ToLowerInvariant()}";
        var body = o.Gzip ? Gzip(item.Body) : item.Body;

        for (var attempt = 1; attempt <= o.MaxAttempts; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new ByteArrayContent(body) };
                request.Content.Headers.ContentType = new("application/x-protobuf");
                if (o.Gzip) request.Content.Headers.ContentEncoding.Add("gzip");
                foreach (var (key, value) in o.Headers) request.Headers.TryAddWithoutValidation(key, value);

                using var response = await client.SendAsync(request, ct);
                if (response.IsSuccessStatusCode) return;

                // Other 4xx will not succeed on retry: the destination rejected the payload or the credentials.
                if (response.StatusCode is not (HttpStatusCode.TooManyRequests or >= HttpStatusCode.InternalServerError))
                {
                    _logger.LogWarning("Forwarding target {Target} rejected {Signal} with {Status}; not retrying", o.Name, item.Signal, (int)response.StatusCode);
                    return;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogDebug(ex, "Forwarding to {Target} failed (attempt {Attempt})", o.Name, attempt);
            }

            if (attempt < o.MaxAttempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)), ct);
            }
        }

        _logger.LogWarning("Forwarding target {Target} unreachable; dropped a {Signal} request after {Attempts} attempt(s)", o.Name, item.Signal, o.MaxAttempts);
    }

    private static byte[] Gzip(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gz = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            gz.Write(data);
        }
        return output.ToArray();
    }
}
