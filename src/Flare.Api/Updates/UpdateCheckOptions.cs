namespace Flare.Api.Updates;

/// <summary>
/// The "new version available" check (ADR-0068). Bound from the <c>UpdateCheck</c>
/// configuration section - <c>UpdateCheck__Enabled=false</c> turns off the only outbound call
/// Flare.Api makes on its own, for air-gapped or egress-restricted installs.
/// </summary>
public sealed class UpdateCheckOptions
{
    public const string SectionName = "UpdateCheck";

    /// <summary>On by default; when false, Flare.Api never contacts GitHub and the dashboard shows no notice.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// How long a successful lookup is reused before GitHub is asked again. The lookup is
    /// lazy - it only happens when a dashboard asks <c>/api/version</c> after this has
    /// elapsed - so an idle instance makes no calls at all.
    /// </summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(24);

    /// <summary>The GitHub <c>owner/name</c> whose releases are checked - only worth changing for a fork that publishes its own.</summary>
    public string Repository { get; set; } = "aminparsa18/Flare.Net";
}
