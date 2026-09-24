using MemoryPack;

namespace Flare.Api.Model;

/// <summary>How <see cref="AlertThreshold.Count"/> is compared against the observed count.</summary>
public enum ThresholdComparator
{
    /// <summary>Fires when the observed count is at or above the threshold (the common "error spike" case).</summary>
    GreaterThanOrEqual,

    /// <summary>Fires when the observed count is below the threshold (e.g. "a service went quiet").</summary>
    LessThan,
}

/// <summary>Which condition an <see cref="AlertRule"/> evaluates on every poll tick.</summary>
/// <remarks>
/// <see cref="LogCount"/> is the default/original behavior (<see cref="AlertRule.Condition"/>
/// + <see cref="AlertRule.Threshold"/>'s <see cref="AlertThreshold.Count"/>) - every rule
/// created before this discriminator existed reads back as <see cref="LogCount"/> (see
/// <c>db/clickhouse/0014_alert_rules_metric_condition.sql</c>'s column default), unchanged.
/// <see cref="MetricThreshold"/> instead evaluates <see cref="AlertRule.MetricCondition"/>
/// against <see cref="AlertRule.MetricThresholdValue"/> through
/// <see cref="Query.MetricAlertConditionQueryBuilder"/>/<c>IAlertQueryService.EvaluateMetricConditionAsync</c> -
/// reusing the same metrics-query machinery <c>MetricQueryService</c> already has, not a
/// new rule engine. <see cref="ExceptionCount"/> evaluates <see cref="AlertRule.ExceptionCondition"/>
/// as a count over <see cref="AlertRule.Threshold"/>'s <see cref="AlertThreshold.Count"/> -
/// same shape as <see cref="LogCount"/>, just against exception-event occurrences
/// (<see cref="Query.ExceptionCountConditionQueryBuilder"/>/<c>IAlertQueryService.CountMatchingExceptionsAsync</c>,
/// reusing <c>ExceptionFilterSqlBuilder</c>) rather than log rows - see
/// <c>docs-internal/adr/0022-exception-count-alerting.md</c>. <see cref="AlertRule.Condition"/>
/// is ignored for <see cref="MetricThreshold"/>/<see cref="ExceptionCount"/> rules (and vice
/// versa for <see cref="AlertRule.MetricCondition"/>/<see cref="AlertRule.MetricThresholdValue"/>
/// on non-<see cref="MetricThreshold"/> rules, and for <see cref="AlertRule.ExceptionCondition"/>
/// on non-<see cref="ExceptionCount"/> rules) - same "field present, meaningful only for one
/// mode" convention <see cref="AlertRule"/>'s notification-channel fields already use.
/// <see cref="Anomaly"/> scores one of the other three kinds' series (named by
/// <see cref="AnomalyCondition.Source"/>, read from that kind's own condition field) against
/// its own seasonal baseline instead of a fixed threshold - see
/// <c>docs-internal/adr/0048-anomaly-detection-alerting.md</c>.
/// </remarks>
public enum AlertConditionKind
{
    LogCount,
    MetricThreshold,
    ExceptionCount,
    Anomaly,
}

/// <summary>The seasonal period an <see cref="AnomalyCondition"/>'s baseline windows are shifted back by.</summary>
public enum AnomalySeasonality
{
    /// <summary>Same window, same time of day, on each of the previous <see cref="AnomalyCondition.BaselinePeriods"/> days.</summary>
    Daily,

    /// <summary>Same window, same time of week, on each of the previous <see cref="AnomalyCondition.BaselinePeriods"/> weeks.</summary>
    Weekly,
}

/// <summary>Which side of the baseline an <see cref="AnomalyCondition"/> fires on.</summary>
/// <remarks><see cref="Both"/> is first so an omitted JSON <c>direction</c> (which deserializes as 0 - see <see cref="AnomalyCondition"/>'s remarks) means the intended default.</remarks>
public enum AnomalyDirection
{
    /// <summary>Either side (|z| &gt;= threshold).</summary>
    Both,

    /// <summary>Only when the current value is unusually high (z &gt;= threshold).</summary>
    Above,

    /// <summary>Only when the current value is unusually low (z &lt;= -threshold) - "traffic is half what it normally is".</summary>
    Below,
}

/// <summary>
/// Which computed field of a metric point is compared against
/// <see cref="AlertRule.MetricThresholdValue"/>. Which members are meaningful depends on
/// the condition's <see cref="MetricAlertCondition.Type"/> - <see cref="Value"/> only for
/// Gauge/Sum, the rest only for Histogram - same "the caller already knows the metric's
/// type, the API trusts it" convention <see cref="MetricQueryRequest.Type"/>'s doc comment
/// already documents; the dashboard's picker is what actually restricts the choice.
/// Plain enum, no <c>[MemoryPackable]</c>/<c>[GenerateTypeScript]</c> - same convention as
/// <see cref="ThresholdComparator"/>/<see cref="MetricPointType"/> (MemoryPack serializes a
/// plain enum natively; the attributes exist for classes/records).
/// </summary>
public enum MetricAlertAggregation
{
    /// <summary>Gauge: <c>avg(Value)</c> over the window. Sum: reset-aware <c>increase()</c> over the window, summed across every matched series (ADR-0044).</summary>
    Value,

    /// <summary>Sum: raw sample row count over the window. Histogram: total observation count (<c>sum(Count)</c>) over the window.</summary>
    Count,

    /// <summary>Histogram only: <c>sum(Sum)</c> over the window.</summary>
    Sum,

    /// <summary>Histogram only: approximate p50 over the window, via <see cref="Query.HistogramQuantileEstimator.Estimate"/>.</summary>
    P50,

    P75,

    P90,

    P95,

    P99,

    /// <summary>Histogram only: approximate max over the window, via <see cref="Query.HistogramQuantileEstimator.EstimateMax"/>.</summary>
    MaxApprox,

    /// <summary>
    /// Gauge only: each matched series' most recent point in the window
    /// (<c>argMax(Value, Time)</c>), averaged across series - "right now" rather than the
    /// window's average. Identical to the latest point when the filter matches one series (ADR-0049).
    /// </summary>
    Last,

    /// <summary>
    /// Gauge only: <c>min(Value)</c> over the window. With <see cref="ThresholdComparator.GreaterThanOrEqual"/>
    /// this is "every point in the window breached" (all the time); with
    /// <see cref="ThresholdComparator.LessThan"/> it's "at least once" (ADR-0049).
    /// </summary>
    Min,

    /// <summary>
    /// Gauge only: <c>max(Value)</c> over the window - the mirror of <see cref="Min"/>: "at least
    /// once" with <see cref="ThresholdComparator.GreaterThanOrEqual"/>, "all the time" with
    /// <see cref="ThresholdComparator.LessThan"/> (ADR-0049).
    /// </summary>
    Max,
}

/// <summary>
/// A metric-query threshold condition - the <see cref="AlertConditionKind.MetricThreshold"/>
/// counterpart to <see cref="LogFilter"/>. Deliberately <see cref="MemoryPackableAttribute"/>
/// only, no <c>[GenerateTypeScript]</c>: it nests <see cref="MetricFilter"/>, itself
/// generator-ineligible for the same <c>IReadOnlyList&lt;T&gt;</c> reason
/// <see cref="LogFilter"/> is (see <c>docs-internal/adr/0016-memorypack-dashboard-typescript-adoption.md</c>) -
/// hand-written TypeScript companion (<c>$lib/memorypack/MetricAlertCondition.ts</c>),
/// reusing the <c>$lib/memorypack/MetricFilter.ts</c> that already exists for the Metrics
/// Explorer.
/// </summary>
[MemoryPackable]
public sealed partial record MetricAlertCondition
{
    public required string MetricName { get; init; }

    public required MetricPointType Type { get; init; }

    public MetricFilter Filter { get; init; } = new();

    public MetricAlertAggregation Aggregation { get; init; } = MetricAlertAggregation.Value;
}

/// <summary>
/// An exception-occurrence-count condition - the <see cref="AlertConditionKind.ExceptionCount"/>
/// counterpart to <see cref="LogFilter"/>. Deliberately <see cref="MemoryPackableAttribute"/>
/// only, no <c>[GenerateTypeScript]</c>: it nests <see cref="ExceptionFilter"/>, itself
/// generator-ineligible (nullable <c>DateTimeOffset</c> members - see
/// <c>$lib/memorypack/date-time-offset.ts</c>'s header comment) - hand-written TypeScript
/// companion (<c>$lib/memorypack/ExceptionCountCondition.ts</c>), reusing the
/// <c>$lib/memorypack/ExceptionFilter.ts</c> that already exists for the Exceptions page.
/// </summary>
/// <remarks>
/// Counts via <see cref="Query.ExceptionCountConditionQueryBuilder"/>, which reuses
/// <see cref="Query.ExceptionFilterSqlBuilder"/> (the same <c>WHERE</c> fragment
/// <see cref="Query.ExceptionGroupQueryBuilder"/>/<c>ExceptionOccurrenceQueryBuilder</c> build
/// on) plus an exact <c>exception.type</c> match, so evaluation stays consistent with how the
/// Exceptions page itself groups occurrences.
/// </remarks>
[MemoryPackable]
public sealed partial record ExceptionCountCondition
{
    /// <summary>The <c>exception.type</c> event attribute to match, e.g. <c>"System.NullReferenceException"</c>.</summary>
    public required string ExceptionType { get; init; }

    /// <summary>
    /// Exact <c>exception.message</c> match, same grouping <see cref="ExceptionGroup"/> uses.
    /// Empty (the default) matches every message for <see cref="ExceptionType"/> - counting
    /// "this exception type occurred N times" without narrowing to one specific message, the
    /// roadmap item's literal ask; a non-empty value narrows to one exact (type, message) group.
    /// </summary>
    public string ExceptionMessage { get; init; } = "";

    public ExceptionFilter Filter { get; init; } = new();
}

/// <summary>
/// The scoring parameters of an <see cref="AlertConditionKind.Anomaly"/> rule. Carries no
/// series of its own: <see cref="Source"/> names which existing condition kind produces it,
/// and the rule's matching condition field (<see cref="AlertRule.Condition"/>,
/// <see cref="AlertRule.MetricCondition"/> or <see cref="AlertRule.ExceptionCondition"/>) is
/// evaluated once for the current window and once per baseline window - see
/// <see cref="Alerting.AnomalyEvaluator"/> and <c>docs-internal/adr/0048-anomaly-detection-alerting.md</c>.
/// <see cref="MemoryPackableAttribute"/> only, with a hand-written TypeScript companion
/// (<c>$lib/memorypack/AnomalyCondition.ts</c>), so its enums stay the plain
/// <c>enums.ts</c> name/int converters <see cref="AlertConditionKind"/> already uses.
/// </summary>
/// <remarks>
/// System.Text.Json source-gen resets a member omitted from the JSON body to
/// <see langword="default"/>, not its C# initializer (see <see cref="AlertRuleRequest"/>'s
/// remarks). So every enum member's intended default is its 0 value, and the two numeric
/// members, whose 0 is never valid, are <c>required</c> - an omitted one is a 400, not a
/// silent 0.
/// </remarks>
[MemoryPackable]
public sealed partial record AnomalyCondition
{
    /// <summary><see cref="AlertConditionKind.LogCount"/>, <see cref="AlertConditionKind.MetricThreshold"/> or <see cref="AlertConditionKind.ExceptionCount"/> - never <see cref="AlertConditionKind.Anomaly"/> itself.</summary>
    public AlertConditionKind Source { get; init; } = AlertConditionKind.LogCount;

    public AnomalySeasonality Seasonality { get; init; } = AnomalySeasonality.Daily;

    /// <summary>How many past periods are sampled for the baseline, between <see cref="MinBaselinePeriods"/> and <see cref="MaxBaselinePeriods"/>.</summary>
    public required int BaselinePeriods { get; init; }

    /// <summary>How many standard deviations from the baseline mean counts as anomalous, greater than 0 and at most <see cref="MaxZScoreThreshold"/>.</summary>
    public required double ZScoreThreshold { get; init; }

    public AnomalyDirection Direction { get; init; }

    public const int MinBaselinePeriods = 3;

    public const int MaxBaselinePeriods = 12;

    public const double MaxZScoreThreshold = 10;
}

/// <summary>The breach condition an <see cref="AlertRule"/> evaluates on every poll tick.</summary>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record AlertThreshold
{
    /// <summary>Meaningful only for <see cref="AlertConditionKind.LogCount"/> rules - ignored (a harmless placeholder) for <see cref="AlertConditionKind.MetricThreshold"/> ones, which compare against <see cref="AlertRule.MetricThresholdValue"/> instead.</summary>
    public required ulong Count { get; init; }

    public ThresholdComparator Comparator { get; init; } = ThresholdComparator.GreaterThanOrEqual;

    /// <summary>
    /// Shared by <c>AlertEvaluationWorker</c> (the real evaluation loop) and the
    /// <c>/api/alerts/{id}/test</c>/<c>/api/alerts/test</c> dry-run endpoints, so "would
    /// this fire" always means the same thing whether asked live or in a test.
    /// </summary>
    public bool IsBreached(ulong observedCount) => Comparator switch
    {
        ThresholdComparator.LessThan => observedCount < Count,
        _ => observedCount >= Count,
    };

    /// <summary>
    /// <see cref="AlertConditionKind.MetricThreshold"/> counterpart to <see cref="IsBreached"/> -
    /// compares against <paramref name="observedValue"/>/<paramref name="thresholdValue"/>
    /// (a metric-query result, e.g. p99 latency) rather than <see cref="Count"/>, reusing
    /// the same <see cref="Comparator"/>.
    /// </summary>
    public bool IsBreachedValue(double observedValue, double thresholdValue) => Comparator switch
    {
        ThresholdComparator.LessThan => observedValue < thresholdValue,
        _ => observedValue >= thresholdValue,
    };
}

/// <summary>A saved threshold/query-based alert rule.</summary>
/// <remarks>
/// <see cref="Condition"/> reuses <see cref="LogFilter"/> verbatim - the same filter DSL
/// <c>/api/logs/search</c>/<c>/api/logs/aggregate</c> already compile via
/// <see cref="Query.LogFilterSqlBuilder"/>. Its <see cref="LogFilter.From"/>/
/// <see cref="LogFilter.To"/> are ignored for alert evaluation:
/// <c>AlertEvaluationWorker</c> supplies its own rolling window derived from
/// <see cref="WindowSeconds"/> at evaluation time.
/// </remarks>
[MemoryPackable]
public sealed partial record AlertRule
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public string Description { get; init; } = "";

    public bool Enabled { get; init; } = true;

    public required LogFilter Condition { get; init; }

    public required AlertThreshold Threshold { get; init; }

    /// <summary>Rolling window, in seconds, the threshold is evaluated over on every poll tick.</summary>
    public required int WindowSeconds { get; init; }

    /// <summary>Minimum seconds between two notifications for this rule, even if it keeps breaching.</summary>
    public int CooldownSeconds { get; init; } = 300;

    /// <summary>
    /// Where the fired-alert JSON payload is POSTed. Covers both a generic webhook
    /// consumer and a Slack incoming-webhook URL - see <c>WebhookAlertNotifier</c> for
    /// the shared payload shape. Mutually exclusive with <see cref="TelegramBotToken"/>/
    /// <see cref="TelegramChatId"/> - a rule notifies exactly one channel; see
    /// <c>AlertEndpoints</c>'s channel validation.
    /// </summary>
    public string WebhookUrl { get; init; } = "";

    /// <summary>
    /// The Telegram bot (from @BotFather) a fired alert is sent through, when this rule's
    /// channel is Telegram instead of webhook/Slack. Set together with
    /// <see cref="TelegramChatId"/>, never alongside <see cref="WebhookUrl"/> - see
    /// <c>TelegramAlertNotifier</c>.
    /// </summary>
    public string TelegramBotToken { get; init; } = "";

    /// <summary>The target chat/channel/group ID <see cref="TelegramBotToken"/> sends the fired-alert message to.</summary>
    public string TelegramChatId { get; init; } = "";

    /// <summary>
    /// Recipient address(es) (comma/semicolon-separated for more than one) a fired alert
    /// is emailed to, when this rule's channel is Email instead of webhook/Slack or
    /// Telegram. The SMTP server itself is app-wide config (<c>Alerting.EmailOptions</c>),
    /// not per-rule - see <c>EmailAlertNotifier</c>.
    /// </summary>
    public string EmailTo { get; init; } = "";

    /// <summary>
    /// A PagerDuty Events API v2 integration/routing key (from a PagerDuty service's
    /// "Events API v2" integration), when this rule's channel is PagerDuty instead of
    /// webhook/Slack, Telegram, or Email - see <c>PagerDutyAlertNotifier</c>. Unlike
    /// Email, there's no app-wide server config: the routing key alone is enough to POST
    /// to PagerDuty's fixed Events API endpoint.
    /// </summary>
    public string PagerDutyRoutingKey { get; init; } = "";

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    /// <summary>Which condition this rule evaluates. Appended after every pre-existing field (not inserted earlier) so the hand-written MemoryPack TypeScript companion (<c>$lib/memorypack/AlertRule.ts</c>) versions the same way its own <c>deserializeCore</c> already does for older payloads.</summary>
    public AlertConditionKind ConditionKind { get; init; } = AlertConditionKind.LogCount;

    /// <summary>Set (non-null) only for <see cref="AlertConditionKind.MetricThreshold"/> rules; null/ignored for <see cref="AlertConditionKind.LogCount"/> ones, which use <see cref="Condition"/> instead.</summary>
    public MetricAlertCondition? MetricCondition { get; init; }

    /// <summary>The metric-condition threshold value, compared via <see cref="AlertThreshold.IsBreachedValue"/>. Set (non-null) only for <see cref="AlertConditionKind.MetricThreshold"/> rules; null/ignored for <see cref="AlertConditionKind.LogCount"/> ones, which use <see cref="Threshold"/>'s <see cref="AlertThreshold.Count"/> instead.</summary>
    public double? MetricThresholdValue { get; init; }

    /// <summary>
    /// Zero or more saved <see cref="NotificationChannel"/> IDs this rule fans out to on
    /// breach - the reusable-channel counterpart to <see cref="WebhookUrl"/>/
    /// <see cref="TelegramBotToken"/>+<see cref="TelegramChatId"/>/<see cref="EmailTo"/>/
    /// <see cref="PagerDutyRoutingKey"/>. Mutually exclusive with those legacy inline
    /// fields (see <see cref="AlertRuleRequest.ValidateChannel"/>) - empty for every rule
    /// created before this field existed, which keeps notifying through its legacy inline
    /// channel unchanged; non-empty only for rules created/edited through the channel
    /// picker. Appended after every pre-existing field, same versioning reasoning as
    /// <see cref="ConditionKind"/>. Resolved to actual <see cref="NotificationChannel"/>
    /// rows by <c>NotificationChannelResolver</c>, never read directly by a notifier.
    /// </summary>
    public IReadOnlyList<Guid> ChannelIds { get; init; } = [];

    /// <summary>
    /// Set (non-null) only for <see cref="AlertConditionKind.ExceptionCount"/> rules;
    /// null/ignored otherwise. Compared as a count via <see cref="Threshold"/>'s
    /// <see cref="AlertThreshold.Count"/>/<see cref="AlertThreshold.IsBreached"/>, same as
    /// <see cref="Condition"/> - unlike <see cref="MetricCondition"/>, this doesn't need its
    /// own threshold-value sibling. Appended after every pre-existing field (after
    /// <see cref="ChannelIds"/>, the most recently appended field before this one), same
    /// versioning reasoning as <see cref="ConditionKind"/>.
    /// </summary>
    public ExceptionCountCondition? ExceptionCondition { get; init; }

    /// <summary>
    /// Opt-in absent-data ("no data") alerting: when greater than 0, the rule also fires if
    /// its condition matched <b>nothing at all</b> over the last this-many seconds - a dead
    /// exporter or a service that stopped emitting, which no threshold comparison can catch
    /// on its own (a metric over an empty window evaluates to <see cref="double.NaN"/>, which
    /// never breaches either direction). 0 (the default, and every rule created before this
    /// field existed) disables it. Its own window rather than reusing <see cref="WindowSeconds"/>,
    /// so e.g. a 5-minute p99 threshold can pair with a 30-minute "stopped reporting" check.
    /// Only meaningful for <see cref="AlertConditionKind.LogCount"/>/<see cref="AlertConditionKind.MetricThreshold"/>
    /// rules - rejected for <see cref="AlertConditionKind.ExceptionCount"/> by
    /// <see cref="AlertRuleRequest.ValidateCondition"/>, since zero exceptions is the healthy
    /// state there, not a silent exporter. An opt-in field rather than a fourth
    /// <see cref="AlertConditionKind"/> - see <c>docs-internal/adr/0045-absent-data-alerting.md</c>.
    /// Appended after every pre-existing field, same versioning reasoning as <see cref="ConditionKind"/>.
    /// </summary>
    public int NoDataWindowSeconds { get; init; }

    /// <summary>
    /// How often <c>AlertEvaluationWorker</c> re-evaluates this rule, in seconds. 0 (the
    /// default, and every rule created before this field existed) means every poll tick
    /// (<c>Alerting:PollInterval</c>, 30s by default) - the original behavior. A larger value
    /// (e.g. 300/900) lets a slow or expensive rule run less often; the worker skips it on
    /// ticks where it isn't yet due, tracked per rule in Redis (not ClickHouse - see
    /// <c>docs-internal/adr/0046-per-rule-alert-evaluation-interval.md</c>). Never longer than
    /// <see cref="WindowSeconds"/> (see <see cref="AlertRuleRequest.ValidateCondition"/>), so
    /// consecutive evaluations' windows always overlap rather than leaving unobserved gaps.
    /// Appended after every pre-existing field, same versioning reasoning as <see cref="ConditionKind"/>.
    /// </summary>
    public int EvaluationIntervalSeconds { get; init; }

    /// <summary>
    /// Set (non-null) only for <see cref="AlertConditionKind.Anomaly"/> rules; null/ignored
    /// otherwise. The series it scores still comes from <see cref="Condition"/>/<see cref="MetricCondition"/>/<see cref="ExceptionCondition"/>,
    /// per <see cref="AnomalyCondition.Source"/>. Appended after <see cref="EvaluationIntervalSeconds"/>,
    /// same versioning reasoning as <see cref="ConditionKind"/>.
    /// </summary>
    public AnomalyCondition? AnomalyCondition { get; init; }

    /// <summary>
    /// Opt-in minimum sample size for <see cref="AlertConditionKind.MetricThreshold"/> rules:
    /// when greater than 0, the threshold is only compared if the condition matched at least
    /// this many raw data points (across every matched series) over <see cref="WindowSeconds"/>.
    /// Fewer points is "insufficient data" - the rule doesn't fire, rather than e.g. firing on
    /// an average of one or two sparse samples. 0 (the default, and every rule created before
    /// this field existed) disables it. Complements <see cref="NoDataWindowSeconds"/>, which is
    /// what fires on zero points; a rule may use both. Rejected for every other condition kind
    /// by <see cref="AlertRuleRequest.ValidateCondition"/> - a count rule's observed count <i>is</i>
    /// its sample size. See <c>docs-internal/adr/0050-alert-minimum-data-points.md</c>. Appended
    /// after <see cref="AnomalyCondition"/>, same versioning reasoning as <see cref="ConditionKind"/>.
    /// </summary>
    public int MinDataPoints { get; init; }
}

/// <summary>Create/update request body for <c>/api/alerts</c>.</summary>
/// <remarks>
/// Every optional member below is nullable rather than typed with a non-default C#
/// initializer, unlike <see cref="AlertRule"/>'s equivalents - see
/// <see cref="Model.LogSearchRequest.Filter"/>'s doc comment for the general caveat, and
/// why it hits harder here: this type also has `required` members, which pushes System.Text.Json's
/// source generator onto its parameterized-constructor-style converter (confirmed live -
/// a JSON body omitting a `required` member throws instead of silently defaulting, proving
/// this converter treats every settable member, not just the `required` ones, as a
/// constructor parameter and passes <see langword="default"/> for anything the JSON body
/// omits). For <see cref="bool"/>/<see cref="int"/> members that collision is
/// unrecoverable in place: a caller must be able to send <c>"enabled":false</c>
/// explicitly, so a non-nullable <see cref="bool"/> can never tell "omitted" apart from
/// "explicitly false" once <see langword="default"/> and "explicitly false" are the same
/// value. Hence <see cref="bool"/>?/<see cref="int"/>? here, coalesced to the real default
/// in <see cref="Query.AlertQueryService"/>'s <c>CreateAsync</c>/<c>UpdateAsync</c> - same
/// pattern <see cref="Query.LogSearchQueryBuilder.Build"/> uses for <see cref="Model.LogSearchRequest.Filter"/>.
/// The <see cref="string"/> members have no such collision (there's no meaningful
/// difference between "omitted" and "explicitly empty" for a description or webhook URL)
/// but are made nullable too, so the whole type uses one consistent shape instead of
/// mixing coalescing strategies.
/// </remarks>
[MemoryPackable]
public sealed partial record AlertRuleRequest
{
    public required string Name { get; init; }

    public string? Description { get; init; }

    public bool? Enabled { get; init; }

    /// <summary>See <see cref="Model.LogSearchRequest.Filter"/>'s doc comment - the same JSON-deserialization default caveat applies here.</summary>
    public LogFilter Condition { get; init; } = new();

    public required AlertThreshold Threshold { get; init; }

    public required int WindowSeconds { get; init; }

    public int? CooldownSeconds { get; init; }

    /// <summary>See <see cref="AlertRule.WebhookUrl"/>'s doc comment - mutually exclusive with <see cref="TelegramBotToken"/>/<see cref="TelegramChatId"/>.</summary>
    public string? WebhookUrl { get; init; }

    /// <summary>See <see cref="AlertRule.TelegramBotToken"/>'s doc comment.</summary>
    public string? TelegramBotToken { get; init; }

    /// <summary>See <see cref="AlertRule.TelegramChatId"/>'s doc comment.</summary>
    public string? TelegramChatId { get; init; }

    /// <summary>See <see cref="AlertRule.EmailTo"/>'s doc comment.</summary>
    public string? EmailTo { get; init; }

    /// <summary>See <see cref="AlertRule.PagerDutyRoutingKey"/>'s doc comment.</summary>
    public string? PagerDutyRoutingKey { get; init; }

    /// <summary>See <see cref="AlertRule.ConditionKind"/>'s doc comment. Defaults to <see cref="AlertConditionKind.LogCount"/> when omitted, same as the saved-rule default.</summary>
    public AlertConditionKind? ConditionKind { get; init; }

    /// <summary>See <see cref="AlertRule.MetricCondition"/>'s doc comment.</summary>
    public MetricAlertCondition? MetricCondition { get; init; }

    /// <summary>See <see cref="AlertRule.MetricThresholdValue"/>'s doc comment.</summary>
    public double? MetricThresholdValue { get; init; }

    /// <summary>See <see cref="AlertRule.ChannelIds"/>'s doc comment.</summary>
    public IReadOnlyList<Guid>? ChannelIds { get; init; }

    /// <summary>See <see cref="AlertRule.ExceptionCondition"/>'s doc comment. Appended after <see cref="ChannelIds"/>, same versioning reasoning as that field.</summary>
    public ExceptionCountCondition? ExceptionCondition { get; init; }

    /// <summary>See <see cref="AlertRule.NoDataWindowSeconds"/>'s doc comment. Omitted/null means 0 (disabled). Appended after <see cref="ExceptionCondition"/>, same versioning reasoning.</summary>
    public int? NoDataWindowSeconds { get; init; }

    /// <summary>See <see cref="AlertRule.EvaluationIntervalSeconds"/>'s doc comment. Omitted/null means 0 (every poll tick). Appended after <see cref="NoDataWindowSeconds"/>, same versioning reasoning.</summary>
    public int? EvaluationIntervalSeconds { get; init; }

    /// <summary>See <see cref="AlertRule.AnomalyCondition"/>'s doc comment. Appended after <see cref="EvaluationIntervalSeconds"/>, same versioning reasoning.</summary>
    public AnomalyCondition? AnomalyCondition { get; init; }

    /// <summary>See <see cref="AlertRule.MinDataPoints"/>'s doc comment. Omitted/null means 0 (disabled). Appended after <see cref="AnomalyCondition"/>, same versioning reasoning.</summary>
    public int? MinDataPoints { get; init; }

    /// <summary>
    /// Exactly one notification mode: either the legacy inline channel
    /// (<see cref="WebhookUrl"/> - covers both a generic webhook consumer and Slack -
    /// both <see cref="TelegramBotToken"/> and <see cref="TelegramChatId"/> together,
    /// <see cref="EmailTo"/>, or <see cref="PagerDutyRoutingKey"/>) or one-or-more
    /// <see cref="ChannelIds"/> - never both, never neither, never one Telegram field
    /// without the other. Called by <c>AlertEndpoints</c>'s create/update handlers (not
    /// the dry-run test endpoints, which never notify). Returns an error message, or null
    /// when this request is valid.
    /// </summary>
    public string? ValidateChannel()
    {
        var hasWebhook = !string.IsNullOrWhiteSpace(WebhookUrl);
        var hasBotToken = !string.IsNullOrWhiteSpace(TelegramBotToken);
        var hasChatId = !string.IsNullOrWhiteSpace(TelegramChatId);
        var hasTelegram = hasBotToken && hasChatId;
        var hasEmail = !string.IsNullOrWhiteSpace(EmailTo);
        var hasPagerDuty = !string.IsNullOrWhiteSpace(PagerDutyRoutingKey);
        var hasChannelIds = ChannelIds is { Count: > 0 };

        if (hasBotToken != hasChatId)
        {
            return "telegramBotToken and telegramChatId must be set together.";
        }

        var legacyChannelCount = (hasWebhook ? 1 : 0) + (hasTelegram ? 1 : 0) + (hasEmail ? 1 : 0) + (hasPagerDuty ? 1 : 0);
        if (legacyChannelCount > 0 && hasChannelIds)
        {
            return "channelIds and a legacy inline channel (webhookUrl, telegramBotToken/telegramChatId, emailTo, pagerDutyRoutingKey) are mutually exclusive - use one or the other.";
        }

        if (hasChannelIds)
        {
            return null;
        }

        return legacyChannelCount switch
        {
            0 => "One of channelIds, webhookUrl, telegramBotToken/telegramChatId, emailTo, or pagerDutyRoutingKey is required.",
            1 => null,
            _ => "webhookUrl, telegramBotToken/telegramChatId, emailTo, and pagerDutyRoutingKey are mutually exclusive - a rule's legacy inline channel is exactly one of them.",
        };
    }

    /// <summary>
    /// <see cref="AlertConditionKind.MetricThreshold"/> requires <see cref="MetricCondition"/>
    /// and <see cref="MetricThresholdValue"/> both set; <see cref="AlertConditionKind.ExceptionCount"/>
    /// requires <see cref="ExceptionCondition"/> set; <see cref="AlertConditionKind.LogCount"/>
    /// (the default) needs neither, since <see cref="Condition"/>/<see cref="Threshold"/> are
    /// already independently required/validated. Called alongside <see cref="ValidateChannel"/>
    /// from <c>AlertEndpoints</c>'s create/update/send-test-draft handlers. Not called from the
    /// dry-run test endpoints' <c>EvaluateAsync</c>, which needs to branch on
    /// <see cref="ConditionKind"/> either way to know which condition to evaluate, and reports
    /// a missing <see cref="MetricCondition"/>/<see cref="ExceptionCondition"/> as "wouldn't
    /// fire" rather than a 400 there - same "channel validation is create/update-only"
    /// precedent <see cref="ValidateChannel"/>'s own doc comment already sets.
    /// </summary>
    /// <remarks>
    /// Also validates <see cref="NoDataWindowSeconds"/>: 0/null (disabled), or at least
    /// <see cref="MinNoDataWindowSeconds"/> - a shorter window would trip on ordinary
    /// ingest/flush latency rather than a genuinely silent source - and never on an
    /// <see cref="AlertConditionKind.ExceptionCount"/> rule (see <see cref="AlertRule.NoDataWindowSeconds"/>).
    /// And <see cref="EvaluationIntervalSeconds"/>: 0/null (every poll tick), or between
    /// <see cref="MinEvaluationIntervalSeconds"/> and <see cref="MaxEvaluationIntervalSeconds"/>,
    /// and never longer than <see cref="WindowSeconds"/> - a 1m window evaluated every 15m
    /// would silently never look at 14 of every 15 minutes.
    /// <see cref="AlertConditionKind.Anomaly"/> is validated by <see cref="ValidateAnomaly"/>;
    /// its <see cref="AnomalyCondition.Source"/> then stands in for <c>kind</c> in the no-data
    /// check, since that's the series actually being queried.
    /// </remarks>
    public string? ValidateCondition()
    {
        var kind = ConditionKind ?? AlertConditionKind.LogCount;
        var conditionError = kind switch
        {
            AlertConditionKind.MetricThreshold when MetricCondition is null || MetricThresholdValue is null =>
                "metricCondition and metricThresholdValue are required when conditionKind is MetricThreshold.",
            AlertConditionKind.ExceptionCount when ExceptionCondition is null =>
                "exceptionCondition is required when conditionKind is ExceptionCount.",
            AlertConditionKind.Anomaly => ValidateAnomaly(),
            _ => null,
        };

        var seriesKind = kind == AlertConditionKind.Anomaly && AnomalyCondition is { } anomaly ? anomaly.Source : kind;
        var noDataError = (NoDataWindowSeconds ?? 0) switch
        {
            0 => null,
            _ when seriesKind == AlertConditionKind.ExceptionCount =>
                "noDataWindowSeconds is not supported when conditionKind is ExceptionCount - zero exceptions is the healthy state, not missing data.",
            < MinNoDataWindowSeconds =>
                $"noDataWindowSeconds must be 0 (disabled) or at least {MinNoDataWindowSeconds}.",
            _ => null,
        };

        var intervalError = (EvaluationIntervalSeconds ?? 0) switch
        {
            0 => null,
            < MinEvaluationIntervalSeconds or > MaxEvaluationIntervalSeconds =>
                $"evaluationIntervalSeconds must be 0 (every poll tick) or between {MinEvaluationIntervalSeconds} and {MaxEvaluationIntervalSeconds}.",
            var interval when interval > WindowSeconds =>
                "evaluationIntervalSeconds must not exceed windowSeconds - otherwise part of every interval is never evaluated.",
            _ => null,
        };

        var minDataPointsError = (MinDataPoints ?? 0) switch
        {
            0 => null,
            < 0 or > MaxMinDataPoints =>
                $"minDataPoints must be 0 (disabled) or between 1 and {MaxMinDataPoints}.",
            _ when kind != AlertConditionKind.MetricThreshold =>
                "minDataPoints is only supported when conditionKind is MetricThreshold - a count rule's observed count is already its sample size.",
            _ => null,
        };

        return conditionError ?? noDataError ?? intervalError ?? minDataPointsError;
    }

    /// <summary>
    /// <see cref="AlertConditionKind.Anomaly"/> arm of <see cref="ValidateCondition"/>:
    /// <see cref="AnomalyCondition"/> set, a non-anomaly <see cref="AnomalyCondition.Source"/>
    /// whose own condition field is set (a <see cref="AlertConditionKind.MetricThreshold"/>
    /// source needs <see cref="MetricCondition"/> but not <see cref="MetricThresholdValue"/> -
    /// there's no fixed threshold), in-range scoring parameters, and a window shorter than
    /// the seasonal period so the current window never overlaps the first baseline window.
    /// </summary>
    private string? ValidateAnomaly()
    {
        if (AnomalyCondition is not { } anomaly)
        {
            return "anomalyCondition is required when conditionKind is Anomaly.";
        }

        return anomaly.Source switch
        {
            AlertConditionKind.Anomaly =>
                "anomalyCondition.source must be LogCount, MetricThreshold or ExceptionCount.",
            AlertConditionKind.MetricThreshold when MetricCondition is null =>
                "metricCondition is required when anomalyCondition.source is MetricThreshold.",
            AlertConditionKind.ExceptionCount when ExceptionCondition is null =>
                "exceptionCondition is required when anomalyCondition.source is ExceptionCount.",
            _ when anomaly.BaselinePeriods is < Model.AnomalyCondition.MinBaselinePeriods or > Model.AnomalyCondition.MaxBaselinePeriods =>
                $"anomalyCondition.baselinePeriods must be between {Model.AnomalyCondition.MinBaselinePeriods} and {Model.AnomalyCondition.MaxBaselinePeriods}.",
            _ when !(anomaly.ZScoreThreshold > 0 && anomaly.ZScoreThreshold <= Model.AnomalyCondition.MaxZScoreThreshold) =>
                $"anomalyCondition.zScoreThreshold must be greater than 0 and at most {Model.AnomalyCondition.MaxZScoreThreshold}.",
            _ when TimeSpan.FromSeconds(WindowSeconds) >= Alerting.AnomalyScoring.PeriodOf(anomaly.Seasonality) =>
                "windowSeconds must be shorter than the anomaly seasonality period (1 day for Daily, 7 days for Weekly).",
            _ => null,
        };
    }

    /// <summary>The shortest non-zero <see cref="NoDataWindowSeconds"/> <see cref="ValidateCondition"/> accepts.</summary>
    public const int MinNoDataWindowSeconds = 60;

    /// <summary>The shortest non-zero <see cref="EvaluationIntervalSeconds"/> <see cref="ValidateCondition"/> accepts.</summary>
    public const int MinEvaluationIntervalSeconds = 60;

    /// <summary>The longest <see cref="EvaluationIntervalSeconds"/> <see cref="ValidateCondition"/> accepts (24h).</summary>
    public const int MaxEvaluationIntervalSeconds = 86_400;

    /// <summary>The largest <see cref="MinDataPoints"/> <see cref="ValidateCondition"/> accepts.</summary>
    public const int MaxMinDataPoints = 100_000;
}

/// <summary>Response body for <c>GET /api/alerts</c>.</summary>
[MemoryPackable]
public sealed partial record AlertRuleListResponse
{
    public required IReadOnlyList<AlertRule> Rules { get; init; }
}

/// <summary>One row of an <see cref="AlertRule"/>'s fired-notification history.</summary>
[MemoryPackable]
public sealed partial record AlertHistoryEntry
{
    public required Guid EventId { get; init; }

    public required Guid RuleId { get; init; }

    /// <summary>Snapshot of the rule's name at fire time - survives a later rename/delete.</summary>
    public required string RuleName { get; init; }

    public required DateTimeOffset FiredAt { get; init; }

    public required ulong ObservedCount { get; init; }

    public required ulong ThresholdCount { get; init; }

    public required int WindowSeconds { get; init; }

    /// <summary>"Sent" | "Failed".</summary>
    public required string NotificationStatus { get; init; }

    /// <summary>Webhook response HTTP status; 0 if the POST never completed.</summary>
    public int NotificationStatusCode { get; init; }

    public string NotificationError { get; init; } = "";

    /// <summary>Snapshot of the firing rule's <see cref="AlertRule.ConditionKind"/> at fire time - appended after every pre-existing field, same versioning reasoning as <see cref="AlertRule.ConditionKind"/>.</summary>
    public AlertConditionKind ConditionKind { get; init; } = AlertConditionKind.LogCount;

    /// <summary>Set only for a <see cref="AlertConditionKind.MetricThreshold"/> event; null for a <see cref="AlertConditionKind.LogCount"/>/<see cref="AlertConditionKind.ExceptionCount"/> one, both of which use <see cref="ObservedCount"/> instead.</summary>
    public double? ObservedValue { get; init; }

    /// <summary>Set only for a <see cref="AlertConditionKind.MetricThreshold"/> event; null for a <see cref="AlertConditionKind.LogCount"/>/<see cref="AlertConditionKind.ExceptionCount"/> one, both of which use <see cref="ThresholdCount"/> instead.</summary>
    public double? ThresholdValue { get; init; }

    /// <summary>
    /// Per-channel outcome of this fire - one entry per channel the firing rule fanned
    /// out to (see <c>NotificationChannelResolver</c>). <see cref="NotificationStatus"/>/
    /// <see cref="NotificationStatusCode"/>/<see cref="NotificationError"/> stay as the
    /// backward-compatible summary across all of them ("Sent" only if every channel
    /// succeeded, "Failed" if any did, <see cref="NotificationError"/> joining per-channel
    /// failures) - existing readers of those three fields see unchanged behavior for a
    /// single-channel fire; this list is the additive per-channel detail. Appended after
    /// every pre-existing field, same versioning reasoning as <see cref="AlertRule.ConditionKind"/>.
    /// </summary>
    public IReadOnlyList<AlertChannelResult> ChannelResults { get; init; } = [];

    /// <summary>
    /// True when this event fired because the rule's condition matched no data at all over
    /// <see cref="AlertRule.NoDataWindowSeconds"/> (absent-data alerting), rather than a
    /// threshold breach - <see cref="ObservedCount"/> is 0 and <see cref="ObservedValue"/>
    /// null in that case, and <see cref="WindowSeconds"/> is the no-data window, not the
    /// threshold window. Appended after every pre-existing field, same versioning reasoning
    /// as <see cref="AlertRule.ConditionKind"/>.
    /// </summary>
    public bool NoData { get; init; }

    /// <summary>
    /// Set only for an <see cref="AlertConditionKind.Anomaly"/> fire: the mean of the baseline
    /// windows the current value was scored against. <see cref="ObservedValue"/> holds that
    /// current value for every anomaly source, counts included. Appended after every
    /// pre-existing field, same versioning reasoning as <see cref="AlertRule.ConditionKind"/>.
    /// </summary>
    public double? BaselineMean { get; init; }

    /// <summary>Set only for an <see cref="AlertConditionKind.Anomaly"/> fire: how many (floored) standard deviations the current value was from <see cref="BaselineMean"/>. Appended after <see cref="BaselineMean"/>.</summary>
    public double? ZScore { get; init; }
}

/// <summary>Response body for <c>GET /api/alerts/{id}/history</c>.</summary>
[MemoryPackable]
public sealed partial record AlertHistoryResponse
{
    public required IReadOnlyList<AlertHistoryEntry> Events { get; init; }
}

/// <summary>
/// Response body for the dry-run test endpoints (<c>POST /api/alerts/{id}/test</c> and
/// <c>POST /api/alerts/test</c>) - evaluates the condition/threshold against current
/// data without touching cooldown state or sending a notification.
/// </summary>
[MemoryPackable]
public sealed partial record AlertTestResult
{
    /// <summary>Meaningful only for a <see cref="AlertConditionKind.LogCount"/>/<see cref="AlertConditionKind.ExceptionCount"/> rule/draft - 0 for a <see cref="AlertConditionKind.MetricThreshold"/> one, which reports its result via <see cref="ObservedValue"/> instead.</summary>
    public required ulong ObservedCount { get; init; }

    public required bool WouldFire { get; init; }

    public required DateTimeOffset EvaluatedAt { get; init; }

    public required int WindowSeconds { get; init; }

    /// <summary>Snapshot of the evaluated rule/draft's <see cref="AlertRule.ConditionKind"/> - appended after every pre-existing field, same versioning reasoning as <see cref="AlertRule.ConditionKind"/>.</summary>
    public AlertConditionKind ConditionKind { get; init; } = AlertConditionKind.LogCount;

    /// <summary>Set only when <see cref="ConditionKind"/> is <see cref="AlertConditionKind.MetricThreshold"/>; null otherwise.</summary>
    public double? ObservedValue { get; init; }

    /// <summary>
    /// True when the rule/draft has <see cref="AlertRule.NoDataWindowSeconds"/> enabled and
    /// its condition matched nothing over that window - <see cref="WouldFire"/> is then true
    /// regardless of the threshold, same precedence <c>AlertEvaluationWorker</c> applies.
    /// Appended after every pre-existing field, same versioning reasoning as <see cref="AlertRule.ConditionKind"/>.
    /// </summary>
    public bool NoData { get; init; }

    /// <summary>Set only for an <see cref="AlertConditionKind.Anomaly"/> rule/draft with enough history to score; null otherwise. Appended after <see cref="NoData"/>, same versioning reasoning.</summary>
    public double? BaselineMean { get; init; }

    /// <summary>Set only alongside <see cref="BaselineMean"/>.</summary>
    public double? ZScore { get; init; }

    /// <summary>
    /// <see cref="AlertConditionKind.Anomaly"/> only: how many baseline windows had data. Below
    /// <see cref="Alerting.AnomalyScoring.MinBaselineSamples"/> means "not enough history yet"
    /// (<see cref="BaselineMean"/>/<see cref="ZScore"/> null, <see cref="WouldFire"/> false).
    /// 0 for every other kind.
    /// </summary>
    public int BaselineSampleCount { get; init; }

    /// <summary>
    /// True when the rule/draft has <see cref="AlertRule.MinDataPoints"/> enabled and its metric
    /// condition matched fewer points than that over the window - <see cref="WouldFire"/> is
    /// then false regardless of the threshold, same as <c>AlertEvaluationWorker</c>. Appended
    /// after <see cref="BaselineSampleCount"/>, same versioning reasoning.
    /// </summary>
    public bool InsufficientData { get; init; }

    /// <summary>The window's raw point count, set only when <see cref="AlertRule.MinDataPoints"/> is enabled (the count is only queried then); null otherwise.</summary>
    public ulong? DataPointCount { get; init; }
}

/// <summary>
/// Response body for the "send test alert" endpoints (<c>POST /api/alerts/{id}/send-test</c>
/// and <c>POST /api/alerts/send-test</c>) - actually sends a notification through the
/// rule/draft's configured channel (unlike <see cref="AlertTestResult"/>'s dry-run, which
/// never notifies), so a channel's config (URL, bot token, SMTP address, PagerDuty
/// routing key) can be verified before relying on it in a real incident. Carries no
/// <see cref="DateTimeOffset"/>/nested member, unlike <see cref="AlertRule"/>, so this can
/// carry <c>[GenerateTypeScript]</c> directly rather than needing a hand-written
/// <c>$lib/memorypack/</c> companion.
/// </summary>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record AlertNotificationTestResult
{
    public required bool Success { get; init; }

    /// <summary>The channel's own status code where one exists (HTTP status for webhook/Slack/Telegram/PagerDuty); 0 for Email, which has none.</summary>
    public required int StatusCode { get; init; }

    public string Error { get; init; } = "";
}
