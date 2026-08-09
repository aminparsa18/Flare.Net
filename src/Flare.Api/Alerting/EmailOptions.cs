namespace Flare.Api.Alerting;

/// <summary>
/// App-wide SMTP server settings <see cref="EmailAlertNotifier"/> sends through - one
/// mail server for the whole deployment, bound from the <c>Email</c> configuration
/// section, same pattern as <see cref="AlertingOptions"/>. Unlike <see cref="AlertRule.WebhookUrl"/>
/// / <see cref="AlertRule.TelegramBotToken"/>+<see cref="AlertRule.TelegramChatId"/>, a
/// rule's Email channel carries only the recipient (<see cref="AlertRule.EmailTo"/>) -
/// the server credentials live here, not per-rule, so they aren't duplicated across
/// rules or stored in the <c>alert_rules</c> table.
/// </summary>
/// <remarks>
/// No working default for <see cref="Host"/> (unlike, say, <c>AlertingOptions.PollInterval</c>) -
/// there's no sensible default mail server. A rule whose channel is Email while this is
/// unconfigured fails per-send with a clear error (see <see cref="EmailAlertNotifier"/>),
/// not at startup - consistent with every other notifier's failure mode being a recorded
/// <c>alert_events</c> row, not a crash.
/// </remarks>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public string Host { get; set; } = "";

    public int Port { get; set; } = 587;

    public string Username { get; set; } = "";

    public string Password { get; set; } = "";

    /// <summary>The "From" address on every fired-alert email.</summary>
    public string From { get; set; } = "";

    /// <summary>Whether to negotiate STARTTLS after connecting - true for the common case (port 587).</summary>
    public bool UseStartTls { get; set; } = true;
}
