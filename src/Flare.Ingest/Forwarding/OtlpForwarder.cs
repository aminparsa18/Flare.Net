using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text.Json;
using Flare.Ingest.Auth;
using Google.Protobuf;
using Microsoft.Extensions.Options;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;
using StackExchange.Redis;

namespace Flare.Ingest.Forwarding;

/// <summary>Hands accepted OTLP exports to the forwarding targets (ADR-0155, ADR-0157). Never throws and never blocks the receiver.</summary>
public interface IOtlpForwarder
{
    void Forward(HttpContext? http, ExportLogsServiceRequest request);
    void Forward(HttpContext? http, ExportTraceServiceRequest request);
    void Forward(HttpContext? http, ExportMetricsServiceRequest request);
}

/// <summary>
/// Targets come from <c>Forwarding:Targets</c> configuration plus the ones saved from the dashboard, re-read every
/// <see cref="ForwardingOptions.RefreshInterval"/>. Each target has a Redis stream as its queue and one sender per
/// replica reading it through a consumer group, so queued requests survive a restart and replicas share the work.
/// </summary>
/// <remarks>
/// The receiver only does a fire-and-forget <c>XADD</c> (trimmed to <see cref="ForwardingTargetOptions.QueueCapacity"/>),
/// so a slow or dead destination never slows ingest. A sender acks a request once the destination accepted it or
/// answered with a non-retryable 4xx; a network error, 429 or 5xx leaves it pending and it is retried after
/// <see cref="ForwardingOptions.ReclaimIdle"/>, until it is older than <see cref="ForwardingOptions.MaxAge"/>.
/// Delivery is therefore at-least-once; the destination may see a duplicate after a timeout whose request had in fact landed.
/// The Redis key names are mirrored in Flare.Api's <c>ExportStatusKeys</c>, which reads the status written here.
/// </remarks>
public sealed class OtlpForwarder : BackgroundService, IOtlpForwarder
{
    private const string TargetsSetKey = "flare:forward:targets";
    private const string Group = "forwarders";
    private static readonly RedisValue NewMessages = ">";

    private sealed record Def(string Key, string Source, ForwardingTargetOptions Options)
    {
        public string Signature => JsonSerializer.Serialize(Options, ForwardingJson.Default.ForwardingTargetOptions);
    }

    private sealed class Target(Def def)
    {
        public Def Def { get; } = def;
        public string QueueKey { get; } = $"flare:forward:queue:{def.Key}";
        public string StatsKey { get; } = $"flare:forward:stats:{def.Key}";
        public string Signature { get; } = def.Signature;
        public HashSet<string> Services { get; } = new(def.Options.Services, StringComparer.Ordinal);
        public HashSet<Guid> Keys { get; } = [.. def.Options.IngestKeyIds];
        public HashSet<ForwardingSignal> Signals { get; } =
            def.Options.Signals.Count == 0 ? [.. Enum.GetValues<ForwardingSignal>()] : [.. def.Options.Signals];
        public CancellationTokenSource Cts { get; } = new();
        public Task Runner { get; set; } = Task.CompletedTask;
    }

    private enum Outcome { Delivered, Permanent, Retry }

    private readonly ForwardingOptions _options;
    private readonly IForwardingTargetStore _store;
    private readonly IConnectionMultiplexer _redis;
    private readonly IHttpClientFactory _httpClients;
    private readonly TimeProvider _time;
    private readonly ILogger<OtlpForwarder> _logger;
    private readonly string _consumer = $"{Environment.MachineName}-{Environment.ProcessId}";
    private volatile Target[] _targets = [];
    private Dictionary<string, Target> _running = [];

    public OtlpForwarder(
        IOptions<ForwardingOptions> options,
        IForwardingTargetStore store,
        IConnectionMultiplexer redis,
        IHttpClientFactory httpClients,
        TimeProvider time,
        ILogger<OtlpForwarder> logger)
    {
        _options = options.Value;
        _options.Validate();
        _store = store;
        _redis = redis;
        _httpClients = httpClients;
        _time = time;
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
        var targets = _targets;
        if (targets.Length == 0) return;

        var keyId = http?.Features.Get<IngestKeyUsageFeature>()?.KeyId;
        IDatabase? db = null;
        foreach (var target in targets)
        {
            if (!target.Signals.Contains(signal)) continue;
            if (target.Keys.Count > 0 && (keyId is null || !target.Keys.Contains(keyId.Value))) continue;
            try
            {
                if (select(target) is not { } message) continue;
                db ??= _redis.GetDatabase();
                db.StreamAdd(
                    target.QueueKey,
                    [new NameValueEntry("s", (int)signal), new NameValueEntry("b", message.ToByteArray())],
                    maxLength: target.Def.Options.QueueCapacity,
                    useApproximateMaxLength: true,
                    flags: CommandFlags.FireAndForget);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not queue {Signal} for forwarding target {Target}", signal, target.Def.Options.Name);
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await ReconcileAsync(stoppingToken);
                await Task.Delay(_options.RefreshInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        finally
        {
            foreach (var target in _running.Values) target.Cts.Cancel();
            await Task.WhenAll(_running.Values.Select(t => t.Runner)).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    /// <summary>Starts, restarts (when its settings changed) and stops senders so they match configuration plus the saved targets.</summary>
    private async Task ReconcileAsync(CancellationToken ct)
    {
        var desired = _options.Targets.Select(t => new Def($"config:{t.Name}", "config", t)).ToList();
        try
        {
            desired.AddRange((await _store.GetEnabledAsync(ct))
                .Select(m => new Def(m.Id.ToString("N"), "managed", m.Options)));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Keep the managed targets already running rather than dropping them on a ClickHouse blip.
            _logger.LogWarning(ex, "Could not read the saved forwarding targets; keeping the current set.");
            desired.AddRange(_running.Values.Where(t => t.Def.Source == "managed").Select(t => t.Def));
        }

        var next = new Dictionary<string, Target>();
        foreach (var def in desired)
        {
            if (_running.TryGetValue(def.Key, out var existing) && existing.Signature == def.Signature)
            {
                next[def.Key] = existing;
                continue;
            }

            if (existing is not null) await StopAsync(existing, removeQueue: false);
            var target = new Target(def);
            await RegisterAsync(target);
            target.Runner = Task.Run(() => RunAsync(target, target.Cts.Token), CancellationToken.None);
            next[def.Key] = target;
        }

        foreach (var gone in _running.Values.Where(t => !next.ContainsKey(t.Def.Key)))
        {
            await StopAsync(gone, removeQueue: true);
        }

        _running = next;
        _targets = [.. next.Values];
    }

    private async Task RegisterAsync(Target target)
    {
        var db = _redis.GetDatabase();
        await db.SetAddAsync(TargetsSetKey, target.Def.Key);
        await db.HashSetAsync(target.StatsKey,
        [
            new HashEntry("name", target.Def.Options.Name),
            new HashEntry("source", target.Def.Source),
        ]);
    }

    private async Task StopAsync(Target target, bool removeQueue)
    {
        target.Cts.Cancel();
        await target.Runner.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        if (!removeQueue) return;

        var db = _redis.GetDatabase();
        await db.SetRemoveAsync(TargetsSetKey, target.Def.Key);
        await db.KeyDeleteAsync([target.QueueKey, target.StatsKey]);
    }

    private async Task RunAsync(Target target, CancellationToken ct)
    {
        var o = target.Def.Options;
        var db = _redis.GetDatabase();
        var http = _httpClients.CreateClient("OtlpForwarding");
        http.Timeout = o.Timeout;

        try
        {
            try
            {
                await db.StreamCreateConsumerGroupAsync(target.QueueKey, Group, "0-0", createStream: true);
            }
            catch (RedisServerException ex) when (ex.Message.StartsWith("BUSYGROUP", StringComparison.Ordinal))
            {
                // Group exists from a previous run.
            }

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var entries = await ReadAsync(db, target);
                    if (entries.Length == 0)
                    {
                        await Task.Delay(250, ct);
                        continue;
                    }

                    var backoff = false;
                    foreach (var entry in entries)
                    {
                        if (Expired(entry))
                        {
                            await db.StreamAcknowledgeAsync(target.QueueKey, Group, entry.Id);
                            await db.StreamDeleteAsync(target.QueueKey, [entry.Id]);
                            await RecordFailureAsync(db, target, "expired before it could be delivered");
                            continue;
                        }

                        var (outcome, error) = await DeliverAsync(http, target, entry, ct);
                        if (outcome == Outcome.Retry)
                        {
                            await RecordFailureAsync(db, target, error!);
                            backoff = true;
                            continue; // stays pending; reclaimed after ReclaimIdle
                        }

                        await db.StreamAcknowledgeAsync(target.QueueKey, Group, entry.Id);
                        await db.StreamDeleteAsync(target.QueueKey, [entry.Id]);
                        if (outcome == Outcome.Delivered) await RecordSuccessAsync(db, target);
                        else await RecordFailureAsync(db, target, error!);
                    }

                    if (backoff) await Task.Delay(TimeSpan.FromSeconds(5), ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Forwarding sender for {Target} hit an error; retrying shortly", o.Name);
                    await Task.Delay(TimeSpan.FromSeconds(2), ct);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Stopped: shutdown or the target changed.
        }
    }

    /// <summary>Requests idle past <see cref="ForwardingOptions.ReclaimIdle"/> (a failed delivery, or a replica that died) first, then new ones.</summary>
    private async Task<StreamEntry[]> ReadAsync(IDatabase db, Target target)
    {
        var pending = await db.StreamPendingMessagesAsync(target.QueueKey, Group, 10, RedisValue.Null,
            minIdleTimeInMs: (long)_options.ReclaimIdle.TotalMilliseconds);
        var entries = new List<StreamEntry>();
        if (pending.Length > 0)
        {
            entries.AddRange(await db.StreamClaimAsync(target.QueueKey, Group, _consumer,
                (long)_options.ReclaimIdle.TotalMilliseconds, [.. pending.Select(p => p.MessageId)]));
        }

        if (entries.Count < 10)
        {
            entries.AddRange(await db.StreamReadGroupAsync(target.QueueKey, Group, _consumer, NewMessages, 10 - entries.Count));
        }

        return [.. entries.Where(e => !e.IsNull)];
    }

    private bool Expired(StreamEntry entry) =>
        long.TryParse(entry.Id.ToString().Split('-')[0], CultureInfo.InvariantCulture, out var ms)
        && _time.GetUtcNow() - DateTimeOffset.FromUnixTimeMilliseconds(ms) > _options.MaxAge;

    private async Task<(Outcome, string?)> DeliverAsync(HttpClient http, Target target, StreamEntry entry, CancellationToken ct)
    {
        var o = target.Def.Options;
        var signalValue = entry["s"];
        var raw = entry["b"];
        if (signalValue.IsNullOrEmpty || raw.IsNullOrEmpty)
        {
            return (Outcome.Permanent, "malformed queue entry");
        }

        var url = $"{o.Endpoint.TrimEnd('/')}/v1/{((ForwardingSignal)(int)signalValue).ToString().ToLowerInvariant()}";
        var bytes = (byte[])raw!;
        var body = o.Gzip ? Gzip(bytes) : bytes;
        string? lastError = null;

        for (var attempt = 1; attempt <= o.MaxAttempts; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new ByteArrayContent(body) };
                request.Content.Headers.ContentType = new("application/x-protobuf");
                if (o.Gzip) request.Content.Headers.ContentEncoding.Add("gzip");
                foreach (var (key, value) in o.Headers) request.Headers.TryAddWithoutValidation(key, value);

                using var response = await http.SendAsync(request, ct);
                if (response.IsSuccessStatusCode) return (Outcome.Delivered, null);

                lastError = $"HTTP {(int)response.StatusCode}";
                // Other 4xx will not succeed on retry: the destination rejected the payload or the credentials.
                if (response.StatusCode is not (HttpStatusCode.TooManyRequests or >= HttpStatusCode.InternalServerError))
                {
                    return (Outcome.Permanent, lastError + " (not retried)");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                lastError = ex.Message;
            }

            if (attempt < o.MaxAttempts) await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)), ct);
        }

        _logger.LogWarning("Forwarding target {Target} is not accepting {Url}: {Error}", o.Name, url, lastError);
        return (Outcome.Retry, lastError);
    }

    private async Task RecordSuccessAsync(IDatabase db, Target target)
    {
        await db.HashIncrementAsync(target.StatsKey, "sent", flags: CommandFlags.FireAndForget);
        await db.HashSetAsync(target.StatsKey, "lastSuccessAt", _time.GetUtcNow().ToUnixTimeMilliseconds(), flags: CommandFlags.FireAndForget);
    }

    private async Task RecordFailureAsync(IDatabase db, Target target, string error)
    {
        await db.HashIncrementAsync(target.StatsKey, "failed", flags: CommandFlags.FireAndForget);
        await db.HashSetAsync(target.StatsKey,
        [
            new HashEntry("lastError", error.Length > 300 ? error[..300] : error),
            new HashEntry("lastErrorAt", _time.GetUtcNow().ToUnixTimeMilliseconds()),
        ], CommandFlags.FireAndForget);
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
