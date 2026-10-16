using Flare.Api.Prometheus;

namespace Flare.Api.Loki;

/// <summary>What a LogQL label name resolves to in Flare's log schema.</summary>
internal enum LokiLabelKind
{
    /// <summary><c>ServiceName</c>: label <c>service_name</c>.</summary>
    Service,

    /// <summary>The OTel severity bucketed into trace/debug/info/warn/error/fatal/unknown: labels <c>detected_level</c> and (before a <c>| json</c> stage) <c>level</c>.</summary>
    Level,

    /// <summary>A field named <c>level</c> extracted by a <c>| json</c> or <c>| logfmt</c> stage when the line has one, the bucketed severity otherwise: <c>level</c> after a parser stage.</summary>
    LevelOrParsed,

    SeverityText,

    Scope,

    TraceId,

    SpanId,

    /// <summary>A key of <c>ResourceAttributes</c> (<see cref="LokiLabel.Key"/> is the dotted OTel key).</summary>
    ResourceAttribute,

    /// <summary>A key of <c>LogAttributes</c>.</summary>
    LogAttribute,

    /// <summary>A field extracted from the body by a <c>| json</c> or <c>| logfmt</c> stage; <see cref="LokiLabel.Key"/> is the label name, which each parser stage maps to its own path or key.</summary>
    ParsedField,

    /// <summary>The parser-error labels <c>__error__</c> and <c>__error_details__</c>: empty unless a <c>| json</c> stage (<see cref="LokiLabel.Key"/> is <c>json</c>) met a body that is not JSON.</summary>
    ParseError,
}

/// <summary>The parser stages a pipeline has applied so far.</summary>
[Flags]
internal enum LokiParserKinds
{
    None = 0,
    Json = 1,
    Logfmt = 2,
}

/// <summary>A resolved label.</summary>
internal sealed record LokiLabel(LokiLabelKind Kind, string Key);

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
    /// <param name="parsers">The parser stages applied so far; once there is one, unknown names are body fields.</param>
    public LokiLabel Resolve(string name, LokiParserKinds parsers = LokiParserKinds.None)
    {
        var afterParser = parsers != LokiParserKinds.None;
        switch (name)
        {
            case "service_name":
                return new LokiLabel(LokiLabelKind.Service, "");
            case "detected_level":
                return new LokiLabel(LokiLabelKind.Level, "");
            case "level":
                return new LokiLabel(afterParser ? LokiLabelKind.LevelOrParsed : LokiLabelKind.Level, "level");
            case "severity_text":
                return new LokiLabel(LokiLabelKind.SeverityText, "");
            case "scope_name":
                return new LokiLabel(LokiLabelKind.Scope, "");
            case "trace_id":
                return new LokiLabel(LokiLabelKind.TraceId, "");
            case "span_id":
                return new LokiLabel(LokiLabelKind.SpanId, "");
            case "__error__" or "__error_details__":
                return new LokiLabel(LokiLabelKind.ParseError, parsers.HasFlag(LokiParserKinds.Json) ? "json" : "");
        }

        if (_resource.TryGetValue(name, out var resourceKey))
        {
            return new LokiLabel(LokiLabelKind.ResourceAttribute, resourceKey);
        }

        if (_log.TryGetValue(name, out var logKey))
        {
            return new LokiLabel(LokiLabelKind.LogAttribute, logKey);
        }

        return afterParser
            ? new LokiLabel(LokiLabelKind.ParsedField, name)
            : new LokiLabel(LokiLabelKind.LogAttribute, name);
    }

    /// <summary>True when <paramref name="name"/> is a resource or log attribute key seen in the window.</summary>
    public bool IsKnownAttribute(string name) => _resource.ContainsKey(name) || _log.ContainsKey(name);

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
