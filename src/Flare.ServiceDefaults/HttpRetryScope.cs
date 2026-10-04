using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace Flare.ServiceDefaults;

/// <summary>
/// Retry tuning for the standard resilience handler <c>AddServiceDefaults()</c> puts on every
/// <see cref="HttpClient"/>: a 429/503 <c>Retry-After</c> is honoured, and a caller can opt a
/// send out of retries with <see cref="SingleShot"/> (the alert "send test" does, so the user
/// sees the real first error instead of a delayed one).
/// </summary>
/// <remarks>
/// <c>ConfigureHttpClientDefaults</c> registers the handler under one shared options name
/// (<c>-standard</c>), so this can't be tuned per client - hence a flow-scoped flag rather
/// than per-client options.
/// </remarks>
public static class HttpRetryScope
{
    private static readonly AsyncLocal<bool> SingleShotFlag = new();

    /// <summary>Sends started on this async flow until the returned scope is disposed get no retries.</summary>
    public static IDisposable SingleShot(bool enabled = true) => new Scope(enabled);

    public static void Tune(HttpStandardResilienceOptions options)
    {
        options.Retry.ShouldRetryAfterHeader = true;
        options.Retry.ShouldHandle = args => ValueTask.FromResult(!SingleShotFlag.Value && HttpClientResiliencePredicates.IsTransient(args.Outcome));
    }

    private sealed class Scope : IDisposable
    {
        private readonly bool _previous;

        public Scope(bool enabled)
        {
            _previous = SingleShotFlag.Value;
            SingleShotFlag.Value = enabled || _previous;
        }

        public void Dispose() => SingleShotFlag.Value = _previous;
    }
}
