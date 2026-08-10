using System.Diagnostics;

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
internal sealed class RandomLogGeneratorWorker(ILogger<RandomLogGeneratorWorker> logger) : BackgroundService
{
    public const string ActivitySourceName = "ExampleApp.LogGenerator";

    private static readonly ActivitySource ActivitySource = new(ActivitySourceName);

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

        for (var i = 0; i < count; i++)
        {
            using var emitActivity = ActivitySource.StartActivity("emit-log-event");
            emitActivity?.SetTag("burst.index", i);
            SampleLogEvents.EmitOne(logger);
        }
    }
}
