namespace ExampleApp.Shop;

/// <summary>Simulated work time - log-normal, so there's a long right tail like real latency, not a flat band.</summary>
public static class Latency
{
    /// <summary>A duration whose median is <paramref name="medianMs"/>.</summary>
    public static TimeSpan Sample(double medianMs, double sigma = 0.35)
    {
        // Box-Muller for a standard normal, then exp.
        var u1 = 1.0 - Random.Shared.NextDouble();
        var u2 = Random.Shared.NextDouble();
        var normal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        return TimeSpan.FromMilliseconds(medianMs * Math.Exp(sigma * normal));
    }

    public static Task DelayAsync(double medianMs, CancellationToken cancellationToken, double sigma = 0.35) =>
        Task.Delay(Sample(medianMs, sigma), cancellationToken);
}
