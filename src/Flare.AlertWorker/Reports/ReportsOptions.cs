namespace Flare.AlertWorker.Reports;

/// <summary>
/// Settings for scheduled dashboard reports (ADR-0142), bound from the <c>Reports</c> configuration
/// section. Off by default: rendering needs a Chromium, which this process does not ship.
/// </summary>
public sealed class ReportsOptions
{
    public const string SectionName = "Reports";

    /// <summary>Whether this worker runs dashboard schedules at all.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The dashboard's public base URL, as the headless browser reaches it. Falls back to
    /// <c>Alerting:PublicUrl</c> when blank.
    /// </summary>
    public string DashboardUrl { get; set; } = "";

    /// <summary>
    /// The Flare.Api base URL as the headless browser reaches it - the same value the dashboard's own
    /// <c>PUBLIC_API_URL</c> holds. The render credential is a cookie scoped to this host, so it must be
    /// the host the dashboard calls.
    /// </summary>
    public string ApiUrl { get; set; } = "";

    /// <summary>Playwright server to connect to (<c>ws://host:3000/</c>), instead of launching a local Chromium.</summary>
    public string BrowserWsEndpoint { get; set; } = "";

    /// <summary>Chromium executable to launch when <see cref="BrowserWsEndpoint"/> is blank; blank uses Playwright's own download.</summary>
    public string ChromiumPath { get; set; } = "";

    /// <summary>Extra command-line switches for a locally launched Chromium, e.g. <c>--host-resolver-rules=MAP localhost host.docker.internal</c>.</summary>
    public string[] ChromiumArgs { get; set; } = [];

    /// <summary>How often due schedules are looked for. A schedule fires within this much of its cron time.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Hard cap on one render, from navigation to the finished file.</summary>
    public TimeSpan RenderTimeout { get; set; } = TimeSpan.FromSeconds(120);

    /// <summary>Extra wait after the page reports ready and the network goes quiet, so charts finish drawing.</summary>
    public TimeSpan SettleDelay { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>How long the render credential lives. It only has to outlast one render.</summary>
    public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromMinutes(10);

    public int ViewportWidth { get; set; } = 1600;

    /// <summary>Tallest page rendered; longer dashboards are cut here.</summary>
    public int MaxPageHeight { get; set; } = 16000;

    /// <summary>Largest attachment sent; a bigger render is recorded as a failure instead of bouncing at the mail server.</summary>
    public int MaxAttachmentBytes { get; set; } = 20 * 1024 * 1024;
}
