namespace ExampleApp.LogGenerator;

/// <summary>
/// Continuously emits one random <see cref="SampleLogEvents"/> event every ~1-2 seconds
/// (jittered) so the Flare dashboard has a steady trickle of realistic-looking data to
/// browse. <see cref="GenerateBurst"/> lets <c>POST /generate-burst</c> fire a batch
/// immediately, for watching the live tail spike on demand.
/// </summary>
internal sealed class RandomLogGeneratorWorker(ILogger<RandomLogGeneratorWorker> logger) : BackgroundService
{
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
        for (var i = 0; i < count; i++)
        {
            SampleLogEvents.EmitOne(logger);
        }
    }
}
