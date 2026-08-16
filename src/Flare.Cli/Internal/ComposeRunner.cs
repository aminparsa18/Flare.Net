using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Flare.Cli.Internal;

internal sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

/// <summary>
/// Thin wrapper around shelling out to the `docker` CLI (specifically `docker compose`
/// against ~/.flare/docker-compose.yml). Plain <see cref="Process"/>, not
/// Docker.DotNet/Testcontainers - neither is referenced anywhere in Flare.Net today, and
/// neither is the right shape for this: Testcontainers is a test-fixture library
/// (ephemeral, test-run-scoped), and a raw Engine-API client would mean reimplementing
/// Compose's own dependency ordering, healthcheck-driven depends_on, and .env
/// interpolation - all of which the `docker compose` CLI (bundled with Docker Desktop,
/// already required to be present) already does correctly. <see cref="ProcessStartInfo.ArgumentList"/>
/// is used throughout, never a concatenated string, to sidestep shell-quoting bugs
/// entirely.
/// </summary>
internal static class ComposeRunner
{
    /// <summary>
    /// Runs `docker compose &lt;args&gt;` against ~/.flare/docker-compose.yml, capturing
    /// output instead of inheriting the console. Use for anything whose output this tool
    /// needs to parse or whose output shouldn't interleave with the caller's own
    /// Spectre.Console rendering (e.g. a status table, a spinner).
    /// </summary>
    public static Task<ProcessResult> RunCapturedAsync(IEnumerable<string> composeArgs, CancellationToken ct = default)
        => RunAsync(BuildComposeStartInfo(composeArgs, redirect: true), ct);

    /// <summary>
    /// Runs `docker compose &lt;args&gt;` against ~/.flare/docker-compose.yml, streaming
    /// output straight to the console instead of capturing it. Use for long-running or
    /// deliberately verbose commands (`logs -f`, `up -d`'s own pull/build progress).
    /// </summary>
    public static Task<int> RunStreamedAsync(IEnumerable<string> composeArgs, CancellationToken ct = default)
        => RunStreamedInternalAsync(BuildComposeStartInfo(composeArgs, redirect: false), ct);

    /// <summary>
    /// Runs a bare `docker &lt;args&gt;` command (not `docker compose`) - for preflight
    /// checks like `docker info` that aren't scoped to a compose project.
    /// </summary>
    public static Task<ProcessResult> RunDockerCapturedAsync(IEnumerable<string> args, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo("docker")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        return RunAsync(psi, ct);
    }

    private static ProcessStartInfo BuildComposeStartInfo(IEnumerable<string> composeArgs, bool redirect)
    {
        var psi = new ProcessStartInfo("docker")
        {
            UseShellExecute = false,
            RedirectStandardOutput = redirect,
            RedirectStandardError = redirect,
            WorkingDirectory = FlareHome.Directory,
        };

        psi.ArgumentList.Add("compose");
        // Explicit --project-name/--project-directory/--env-file rather than relying on
        // Compose's own directory-derived defaults: this tool can be invoked from any
        // cwd, so nothing about project identity or .env resolution should be implicit.
        psi.ArgumentList.Add("--project-name");
        psi.ArgumentList.Add("flare");
        psi.ArgumentList.Add("--project-directory");
        psi.ArgumentList.Add(FlareHome.Directory);
        psi.ArgumentList.Add("--env-file");
        psi.ArgumentList.Add(FlareHome.EnvFilePath);
        psi.ArgumentList.Add("-f");
        psi.ArgumentList.Add(FlareHome.ComposeFilePath);

        foreach (var arg in composeArgs)
        {
            psi.ArgumentList.Add(arg);
        }

        return psi;
    }

    private static async Task<ProcessResult> RunAsync(ProcessStartInfo psi, CancellationToken ct)
    {
        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var cleanup = RegisterChildProcessCleanup(process, ct);
        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);

        return new ProcessResult(process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    private static async Task<int> RunStreamedInternalAsync(ProcessStartInfo psi, CancellationToken ct)
    {
        using var process = new Process { StartInfo = psi };
        process.Start();

        // This is the path `flare logs -f` (and `start`/`update`'s own pull/up progress)
        // takes - long-running and meant to be Ctrl+C-able. Without explicit cleanup, the
        // .NET runtime's default SIGINT/SIGTERM handling terminates THIS process but does
        // NOT touch child processes it spawned: `docker`/the `docker-compose` plugin
        // process get reparented to init and keep running (and, for `logs -f`, keep
        // streaming) forever in the background. Confirmed reproducible against a live
        // stack: Ctrl+C-ing `flare logs -f` left two orphaned docker processes running
        // until manually killed. RegisterChildProcessCleanup below is what fixes it.
        using var cleanup = RegisterChildProcessCleanup(process, ct);
        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        return process.ExitCode;
    }

    /// <summary>
    /// Ensures the child `docker`/`docker compose` process (and anything it in turn
    /// spawned) is killed whenever this tool is asked to stop - via Ctrl+C (SIGINT),
    /// SIGTERM, or the caller's own <see cref="CancellationToken"/> - instead of being
    /// silently orphaned. <c>context.Cancel = true</c> on the Posix handlers suppresses
    /// .NET's own default "terminate immediately" action so this cleanup actually gets to
    /// run first; once the child is killed, the awaited <c>WaitForExitAsync</c> above
    /// unblocks normally and this process exits through its own normal return path.
    /// </summary>
    private static IDisposable RegisterChildProcessCleanup(Process process, CancellationToken ct)
    {
        var registrations = new List<IDisposable>(3);

        void KillProcessTree()
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
                // Process already exited between the check above and the kill call -
                // not an error worth surfacing.
            }
        }

        registrations.Add(PosixSignalRegistration.Create(PosixSignal.SIGINT, context =>
        {
            context.Cancel = true;
            KillProcessTree();
        }));
        registrations.Add(PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
        {
            context.Cancel = true;
            KillProcessTree();
        }));

        if (ct.CanBeCanceled)
        {
            registrations.Add(ct.Register(KillProcessTree));
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
