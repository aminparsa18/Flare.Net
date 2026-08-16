using System.Runtime.InteropServices;

namespace Flare.Cli.Internal;

/// <summary>
/// Registers cleanup to run when this process is asked to stop - via Ctrl+C (SIGINT),
/// SIGTERM, or an external <see cref="CancellationToken"/> - instead of relying on any
/// one mechanism alone. <c>context.Cancel = true</c> on the Posix handlers suppresses
/// .NET's own default "terminate immediately" action so the registered cleanup actually
/// gets to run before the process exits; without it, a plain Ctrl+C left `flare logs -f`
/// orphaning its child `docker compose` process (see <see cref="ComposeRunner"/>'s own
/// remarks) rather than shutting down cleanly.
/// </summary>
internal static class InterruptSignal
{
    public static IDisposable OnInterrupt(Action cleanup, CancellationToken externalToken = default)
    {
        var registrations = new List<IDisposable>(3);

        registrations.Add(PosixSignalRegistration.Create(PosixSignal.SIGINT, context =>
        {
            context.Cancel = true;
            cleanup();
        }));
        registrations.Add(PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
        {
            context.Cancel = true;
            cleanup();
        }));

        if (externalToken.CanBeCanceled)
        {
            registrations.Add(externalToken.Register(cleanup));
        }

        return new DisposableGroup(registrations);
    }

    private sealed class DisposableGroup(List<IDisposable> items) : IDisposable
    {
        public void Dispose()
        {
            foreach (var item in items)
            {
                item.Dispose();
            }
        }
    }
}
