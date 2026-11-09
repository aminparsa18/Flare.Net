namespace Flare.Api.Demo;

/// <summary>
/// Public read-only demo mode (ADR-0278), bound from the <c>Demo</c> configuration section
/// (<c>Demo__Enabled=true</c>). Configuration only, never a stored setting, so nothing reachable
/// over HTTP can switch it off.
/// </summary>
public sealed class DemoOptions
{
    public const string SectionName = "Demo";

    /// <summary>Visitors without a session browse as a shared read-only Viewer, auth is always on,
    /// and that Viewer cannot write anything. Off by default.</summary>
    public bool Enabled { get; set; }

    /// <summary>The local Viewer account visitors act as. Created at startup with a random password
    /// nobody knows, so it can't be signed into.</summary>
    public string Username { get; set; } = "demo";

    /// <summary>Requests one client address may make per minute while browsing as the demo user. A
    /// dashboard page fans out into many parallel queries, so this is a flood guard, not a quota.
    /// Behind a reverse proxy, set <c>ASPNETCORE_FORWARDEDHEADERS_ENABLED=true</c> so the address is
    /// the visitor's rather than the proxy's.</summary>
    public int RequestsPerMinute { get; set; } = 600;
}
