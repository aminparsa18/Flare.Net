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
            SampleLogEvents.EmitOne(logger);

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
            using var emitActivity = ActivitySource.StartActivity("emit-log-event");
            emitActivity?.SetTag("burst.index", i);
            SampleLogEvents.EmitOne(logger);
        }

        stopwatch.Stop();
        BurstsGenerated.Add(1);
        BurstDuration.Record(stopwatch.Elapsed.TotalMilliseconds);
        lastBurstSize = count;
    }

    public override void Dispose()
    {
        Meter.Dispose();
        base.Dispose();
    }
}
