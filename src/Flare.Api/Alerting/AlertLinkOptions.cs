namespace Flare.Api.Alerting;

/// <summary>
/// The public, browser-reachable base URL of the dashboard - used by
/// <see cref="AlertMessageFormatter"/> to build a deep link from a fired-alert
/// notification straight back to the rule that fired it. Bound from the same
/// <c>Alerting</c> configuration section as <see cref="Flare.AlertWorker.Alerting.AlertingOptions"/>
/// (a separate class over that section, not a shared one - PollInterval/MaxRulesPerTick
/// are Flare.AlertWorker-only tuning knobs with no reason to live in Flare.Api, while this
/// URL is needed by both Flare.AlertWorker's real fired-alert sends and Flare.Api's own
/// <c>/api/alerts/*/send-test</c> endpoints).
/// </summary>
/// <remarks>
/// Left blank by default - unlike <c>AlertingOptions.PollInterval</c>, there's no working
/// default to guess an operator's externally-reachable dashboard URL from. When blank,
/// <see cref="AlertMessageFormatter.BuildRuleUrl"/> returns null and every notifier omits
/// the link entirely rather than emitting a broken one.
/// </remarks>
public sealed class AlertLinkOptions
{
    public const string SectionName = "Alerting";

    /// <summary>
    /// e.g. <c>https://flare.example.com</c> - no trailing slash required (trimmed before
    /// use). Same URL an operator would pass to <c>WithPublicDashboardUrl</c> in Aspire
    /// hosting, or that the dashboard's own <c>ORIGIN</c> is set to for docker-compose/CLI.
    /// </summary>
    public string PublicUrl { get; set; } = "";
}
