using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace ExampleApp.LogGenerator;

/// <summary>
/// Continuously emits one random <see cref="SampleLogEvents"/> event every ~1-2 seconds
/// (jittered) so the Flare dashboard has a steady trickle of realistic-looking data to
/// browse. <see cref="GenerateBurst"/> lets <c>POST /generate-burst</c> fire a batch
/// immediately, for watching the live tail spike on demand - and, via
/// <see cref="ActivitySource"/>, for producing a real multi-span OTLP trace: each emitted
/// log during a burst gets its own child span, nested under ASP.NET Core's
/// auto-instrumented span for the triggering HTTP request (see <c>Program.cs</c>'s
/// <c>AddSource</c> registration) - genuine OTel SDK output, not a hand-built trace
/// export, exercising the same wire path a real production app would.
/// </summary>
/// <remarks>
/// Also the source of this example's OTLP metrics (Planning.md's v6, Pass 4): one
/// instrument of each v1-supported point type, all tied to the same burst-generation
/// path a real production app's own custom instrumentation would look like, alongside
/// the free Gauge/Sum/Histogram data <c>Flare.ServiceDefaults.ConfigureOpenTelemetry()</c>
/// already gets from <c>AddAspNetCoreInstrumentation()</c>/<c>AddRuntimeInstrumentation()</c>
/// (both now also flow to Flare once <c>Program.cs</c>'s <c>AddFlareOtlpExporter</c> call
/// exports metrics - see that method's own remarks).
/// </remarks>
internal sealed class RandomLogGeneratorWorker(ILogger<RandomLogGeneratorWorker> logger) : BackgroundService
{
    public const string ActivitySourceName = "ExampleApp.LogGenerator";
    public const string MeterName = "ExampleApp.LogGenerator";

    private static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    private static readonly Meter Meter = new(MeterName);

    /// <summary>Sum (monotonic counter): total bursts generated since process start.</summary>
    private static readonly Counter<long> BurstsGenerated = Meter.CreateCounter<long>(
        "loggenerator.bursts", unit: "{burst}", description: "Number of log bursts generated.");

    /// <summary>Histogram: wall-clock duration of each burst, in milliseconds.</summary>
    private static readonly Histogram<double> BurstDuration = Meter.CreateHistogram<double>(
        "loggenerator.burst.duration", unit: "ms", description: "Time to generate one burst of log events.");

    // ObservableGauge (Gauge): the size of the most recently requested burst - a
    // "current value" reading, not a running total, the same distinction Gauge vs. Sum
    // draws on the wire (see Flare.Ingest's MetricPointRecord remarks). Backed by a
    // plain field the callback reads, rather than computed at callback time, since
    // there's nothing to compute it *from* between bursts.
    private static int lastBurstSize;
    private static readonly ObservableGauge<int> LastBurstSizeGauge = Meter.CreateObservableGauge(
        "loggenerator.last_burst_size", () => lastBurstSize, unit: "{event}", description: "Size of the most recently generated burst.");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // Nothing calls this loop from the outside, so each tick has to be its own
            // root trace - or the Traces page stays empty until someone manually hits
            // POST /generate-burst. EmitWaterfall gives it a few nested child spans
            // instead of one flat span, so the Traces page has an actual waterfall to
            // render, not just a single row.
            EmitWaterfall();

            try
            {
                await Task.Delay(Random.Shared.Next(1000, 2000), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Immediately emits <paramref name="count"/> events, ignoring the normal timer.</summary>
    public void GenerateBurst(int count)
    {
        using var burstActivity = ActivitySource.StartActivity("generate-burst");
        burstActivity?.SetTag("burst.count", count);

        var stopwatch = Stopwatch.StartNew();

        for (var i = 0; i < count; i++)
        {
            EmitWaterfall(burstIndex: i);
        }

        stopwatch.Stop();
        BurstsGenerated.Add(1);
        BurstDuration.Record(stopwatch.Elapsed.TotalMilliseconds);
        lastBurstSize = count;
    }

    /// <summary>
    /// Emits one log event wrapped in a small, believable multi-span trace - "handle-request"
    /// runs "auth-check", then forks into two concurrent branches ("inventory.query", with its
    /// own nested "sql.query", racing "payment.request"), then joins into "render-response" -
    /// instead of one flat span, so the Traces page's waterfall (and its critical-path
    /// highlighting) has an actual bottleneck to show off, not just a flat sequential chain
    /// where every span is trivially "critical". Spans are backdated with
    /// <see cref="Activity.SetEndTime"/> rather than real <c>Task.Delay</c>s: this is dummy
    /// data, so there's no reason to actually block the trickle loop (or, worse, a 500-item
    /// burst) for the fake latency it's showing off - "concurrent" here just means both
    /// branches' timestamps start at the same instant, not that they run on separate threads.
    /// Called with no ambient <see cref="Activity.Current"/> (the trickle loop) it becomes its
    /// own root trace; called from inside <see cref="GenerateBurst"/> it nests under that call's
    /// "generate-burst" span the same way <c>StartActivity</c> always has here, since
    /// <see cref="ActivityContext"/> <c>default</c> means "use <see cref="Activity.Current"/> as
    /// the parent, if any".
    /// </summary>
    private void EmitWaterfall(int? burstIndex = null)
    {
        var start = DateTimeOffset.UtcNow;
        using var root = ActivitySource.StartActivity("handle-request", ActivityKind.Internal, default(ActivityContext), startTime: start);
        if (burstIndex is int index)
        {
            root?.SetTag("burst.index", index);
        }

        var forkStart = ChildSpan("auth-check", start, Random.Shared.Next(1, 8));

        // Two branches start at the same instant. inventory.query usually (not always -
        // real overlapping I/O doesn't always resolve the same way either) draws the
        // longer duration, making it - and its nested sql.query - the critical path;
        // payment.request still ran, but finished in its shadow and never affected when
        // render-response could start.
        var inventoryEnd = SpanWithNestedChild(
            "inventory.query", forkStart, Random.Shared.Next(180, 420),
            "sql.query", childRatio: 0.7, work: () => SampleLogEvents.EmitOne(logger),
            peerService: "inventory-service", childPeerService: "postgres");
        var paymentEnd = ChildSpan(
            "payment.request", forkStart, Random.Shared.Next(40, 150),
            kind: ActivityKind.Client, peerService: "payment-service");
        var joinTime = inventoryEnd > paymentEnd ? inventoryEnd : paymentEnd;

        var end = ChildSpan("render-response", joinTime, Random.Shared.Next(1, 6));

        root?.SetEndTime(end.UtcDateTime);
    }

    /// <summary>
    /// Starts a child span at <paramref name="start"/>, runs <paramref name="work"/> (if any),
    /// backdates it to end after <paramref name="durationMs"/>, and returns that end time for
    /// the next span to chain from. <paramref name="peerService"/>, when given, is the standard
    /// OTel <c>peer.service</c> span attribute - the spec-correct way to say "this span called
    /// out to service X" from inside a process that never actually instruments a separate
    /// "inventory-service"/"payment-service" (this example app is one process). The dashboard's
    /// Service Map tab reads it to draw a distinct node for the call's destination instead of
    /// collapsing everything into this app's own single <c>ServiceName</c>.
    /// </summary>
    private DateTimeOffset ChildSpan(string name, DateTimeOffset start, int durationMs, Action? work = null, ActivityKind kind = ActivityKind.Internal, string? peerService = null)
    {
        using var span = ActivitySource.StartActivity(name, kind, default(ActivityContext), startTime: start);
        if (peerService is not null)
        {
            span?.SetTag("peer.service", peerService);
        }

        work?.Invoke();
        var end = start.AddMilliseconds(durationMs);
        span?.SetEndTime(end.UtcDateTime);
        return end;
    }

    /// <summary>
    /// Like <see cref="ChildSpan"/>, but wraps a nested child spanning <paramref name="childRatio"/>
    /// of its own duration (e.g. inventory.query's underlying sql.query) - the rest is the
    /// parent's own overhead (connection acquisition, deserialization) around it.
    /// </summary>
    private DateTimeOffset SpanWithNestedChild(
        string name, DateTimeOffset start, int durationMs, string childName, double childRatio,
        Action? work = null, string? peerService = null, string? childPeerService = null)
    {
        using var span = ActivitySource.StartActivity(name, ActivityKind.Client, default(ActivityContext), startTime: start);
        if (peerService is not null)
        {
            span?.SetTag("peer.service", peerService);
        }

        ChildSpan(childName, start, (int)(durationMs * childRatio), work, ActivityKind.Client, childPeerService);
        var end = start.AddMilliseconds(durationMs);
        span?.SetEndTime(end.UtcDateTime);
        return end;
    }

    public override void Dispose()
    {
        Meter.Dispose();
        base.Dispose();
    }
}
