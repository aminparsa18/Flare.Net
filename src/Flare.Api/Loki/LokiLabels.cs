using Flare.Api.Prometheus;

namespace Flare.Api.Loki;

/// <summary>What a LogQL label name resolves to in Flare's log schema.</summary>
internal enum LokiLabelKind
{
    /// <summary><c>ServiceName</c>: label <c>service_name</c>.</summary>
    Service,

    /// <summary>The OTel severity bucketed into trace/debug/info/warn/error/fatal/unknown: labels <c>detected_level</c> and (before a <c>| json</c> stage) <c>level</c>.</summary>
    Level,

    /// <summary>A body JSON field named <c>level</c> when there is one, the bucketed severity otherwise: <c>level</c> after a <c>| json</c> stage.</summary>
    LevelOrJson,

    SeverityText,

    Scope,

    TraceId,

    SpanId,

    /// <summary>A key of <c>ResourceAttributes</c> (<see cref="LokiLabel.Key"/> is the dotted OTel key).</summary>
    ResourceAttribute,

    /// <summary>A key of <c>LogAttributes</c>.</summary>
    LogAttribute,

    /// <summary>A field extracted from the body by <c>| json</c>; <see cref="LokiLabel.Key"/> is the dot-separated path, or the label name when the path is guessed from a flattened name.</summary>
    JsonField,

    /// <summary>The parser-error label <c>__error__</c>: empty unless a <c>| json</c> stage (<see cref="LokiLabel.Key"/> is <c>json</c>) met a body that is not JSON.</summary>
    ParseError,
}

/// <summary>A resolved label. <see cref="Guessed"/> marks a <see cref="LokiLabelKind.JsonField"/> whose path is inferred from an underscore-flattened name.</summary>
internal sealed record LokiLabel(LokiLabelKind Kind, string Key, bool Guessed = false);

/// <summary>
/// Maps LogQL label names onto Flare's schema (ADR-0236). Loki labels are flat identifiers; Flare keeps
/// OTel attributes under dotted keys in three maps. The mapping follows Loki's own OTLP ingestion: dots
/// become underscores (<c>k8s.pod.name</c> is <c>k8s_pod_name</c>), the service is <c>service_name</c>,
/// the severity is <c>detected_level</c>, and the trace and span ids are <c>trace_id</c> / <c>span_id</c>.
/// A name that is not one of those is looked up among the attribute keys Flare has seen in the window
/// (<see cref="LokiQueryService"/> samples them), resource attributes first.
/// </summary>
internal sealed class LokiLabelResolver
{
    /// <summary>
    /// The resource attributes Loki indexes by default for OTLP logs. These (and <c>service_name</c>) are
    /// a stream's labels in a response; every other attribute is structured metadata on the entry, so a
    /// stream is not split by high-cardinality values.
    /// </summary>
    public static readonly IReadOnlySet<string> IndexResourceKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "service.namespace", "service.instance.id", "deployment.environment", "deployment.environment.name",
        "cloud.region", "cloud.availability_zone", "k8s.cluster.name", "k8s.namespace.name", "k8s.pod.name",
        "k8s.container.name", "container.name", "k8s.replicaset.name", "k8s.deployment.name",
        "k8s.statefulset.name", "k8s.daemonset.name", "k8s.cronjob.name", "k8s.job.name",
    };

    private readonly Dictionary<string, string> _resource = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _log = new(StringComparer.Ordinal);

    /// <param name="resourceKeys">Resource attribute keys seen in the window, most common first.</param>
    /// <param name="logKeys">Log attribute keys seen in the window, most common first.</param>
    public LokiLabelResolver(IEnumerable<string> resourceKeys, IEnumerable<string> logKeys)
    {
        foreach (var key in resourceKeys)
        {
            _resource.TryAdd(PromNames.Sanitize(key), key);
        }

        foreach (var key in logKeys)
        {
            _log.TryAdd(PromNames.Sanitize(key), key);
        }
    }

    public static LokiLabelResolver Empty { get; } = new([], []);

    /// <summary>The label names a stream carries when a query does not narrow them: <c>service_name</c> and the indexed resource attributes present in the window.</summary>
    public IReadOnlyList<string> StreamLabelNames() =>
        ["service_name", .. _resource.Where(kv => IndexResourceKeys.Contains(kv.Value)).Select(kv => kv.Key).Order(StringComparer.Ordinal)];

    /// <summary>Every label name a selector can use: the fixed ones plus the resource attributes seen in the window.</summary>
    public IReadOnlyList<string> SelectorLabelNames() =>
        ["detected_level", "service_name", .. _resource.Keys.Where(k => k != "service_name").Order(StringComparer.Ordinal)];

    /// <param name="name">The LogQL label name.</param>
    /// <param name="afterJson">True after a <c>| json</c> stage, where unknown names are body fields.</param>
    /// <param name="jsonExpressions">Explicit <c>| json label="path"</c> mappings seen so far.</param>
    public LokiLabel Resolve(string name, bool afterJson = false, IReadOnlyDictionary<string, string>? jsonExpressions = null)
    {
        if (jsonExpressions is not null && jsonExpressions.TryGetValue(name, out var path))
        {
            return new LokiLabel(LokiLabelKind.JsonField, path);
        }

        switch (name)
        {
            case "service_name":
                return new LokiLabel(LokiLabelKind.Service, "");
            case "detected_level":
                return new LokiLabel(LokiLabelKind.Level, "");
            case "level":
                return new LokiLabel(afterJson ? LokiLabelKind.LevelOrJson : LokiLabelKind.Level, "level");
            case "severity_text":
                return new LokiLabel(LokiLabelKind.SeverityText, "");
            case "scope_name":
                return new LokiLabel(LokiLabelKind.Scope, "");
            case "trace_id":
                return new LokiLabel(LokiLabelKind.TraceId, "");
            case "span_id":
                return new LokiLabel(LokiLabelKind.SpanId, "");
            case "__error__":
                return new LokiLabel(LokiLabelKind.ParseError, afterJson ? "json" : "");
        }

        if (_resource.TryGetValue(name, out var resourceKey))
        {
            return new LokiLabel(LokiLabelKind.ResourceAttribute, resourceKey);
        }

        if (_log.TryGetValue(name, out var logKey))
        {
            return new LokiLabel(LokiLabelKind.LogAttribute, logKey);
        }

        return afterJson
            ? new LokiLabel(LokiLabelKind.JsonField, name, Guessed: true)
            : new LokiLabel(LokiLabelKind.LogAttribute, name);
    }

    /// <summary>The attribute label name an OTel key is exposed under.</summary>
    public static string LabelName(string attributeKey) => PromNames.Sanitize(attributeKey);

    /// <summary>The bucketed level for an OTel <c>SeverityNumber</c>, matching <see cref="LevelSql"/>.</summary>
    public static string LevelName(byte severityNumber) => severityNumber switch
    {
        0 => "unknown",
        <= 4 => "trace",
        <= 8 => "debug",
        <= 12 => "info",
        <= 16 => "warn",
        <= 20 => "error",
        _ => "fatal",
    };

    /// <summary>ClickHouse form of <see cref="LevelName"/>.</summary>
    public const string LevelSql =
        "multiIf(SeverityNumber = 0, 'unknown', SeverityNumber <= 4, 'trace', SeverityNumber <= 8, 'debug', " +
        "SeverityNumber <= 12, 'info', SeverityNumber <= 16, 'warn', SeverityNumber <= 20, 'error', 'fatal')";
}
