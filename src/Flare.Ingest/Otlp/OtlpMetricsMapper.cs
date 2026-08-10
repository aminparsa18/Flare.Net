using System.Globalization;
using Flare.Ingest.Model;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Metrics.V1;

namespace Flare.Ingest.Otlp;

/// <summary>Result of mapping one OTLP export: the mapped points, plus any metric names whose point type v1 doesn't support.</summary>
/// <param name="Points">Every Gauge/Sum/Histogram data point in the export, mapped to Flare's internal model.</param>
/// <param name="UnsupportedMetricNames">
/// Distinct names of metrics in this export that used an ExponentialHistogram or Summary
/// point (or no point type at all) - their data points were dropped, not stored. Callers
/// log this rather than the mapper itself doing so, since <see cref="OtlpMetricsMapper"/>
/// stays a pure static mapper with no logger dependency, same style as
/// <see cref="OtlpTraceMapper"/>.
/// </param>
public sealed record MetricMapResult(IReadOnlyList<MetricPointRecord> Points, IReadOnlyList<string> UnsupportedMetricNames);

/// <summary>
/// Maps parsed OTLP <see cref="ExportMetricsServiceRequest"/> messages into Flare's
/// internal <see cref="MetricPointRecord"/> model. Shared by both the gRPC and HTTP
/// receivers, same shape as <see cref="OtlpTraceMapper"/>.
/// </summary>
/// <remarks>
/// v1 scope: only <see cref="Metric.DataOneofCase.Gauge"/>/<see cref="Metric.DataOneofCase.Sum"/>/
/// <see cref="Metric.DataOneofCase.Histogram"/> are mapped - see
/// <see cref="MetricPointRecord"/>'s remarks for why ExponentialHistogram/Summary are
/// deliberately out of scope. Deliberately duplicates <see cref="OtlpTraceMapper"/>'s
/// attribute-flattening helpers rather than extracting a shared utility - same
/// "duplicate now, extract only on a third instance" call already made twice over
/// (<see cref="OtlpLogMapper"/>, <see cref="OtlpTraceMapper"/>).
/// </remarks>
public static class OtlpMetricsMapper
{
    public static MetricMapResult Map(ExportMetricsServiceRequest request)
    {
        var points = new List<MetricPointRecord>();
        var unsupported = new HashSet<string>(StringComparer.Ordinal);

        foreach (var resourceMetrics in request.ResourceMetrics)
        {
            var resourceAttributes = Flatten(resourceMetrics.Resource?.Attributes);
            var serviceName = resourceAttributes.GetValueOrDefault("service.name");
            var resourceSchemaUrl = EmptyToNull(resourceMetrics.SchemaUrl);

            foreach (var scopeMetrics in resourceMetrics.ScopeMetrics)
            {
                var scopeAttributes = Flatten(scopeMetrics.Scope?.Attributes);
                var scopeSchemaUrl = EmptyToNull(scopeMetrics.SchemaUrl);
                var scopeName = EmptyToNull(scopeMetrics.Scope?.Name);
                var scopeVersion = EmptyToNull(scopeMetrics.Scope?.Version);

                foreach (var metric in scopeMetrics.Metrics)
                {
                    var name = metric.Name;
                    var description = EmptyToNull(metric.Description);
                    var unit = EmptyToNull(metric.Unit);

                    switch (metric.DataCase)
                    {
                        case Metric.DataOneofCase.Gauge:
                            foreach (var dp in metric.Gauge.DataPoints)
                            {
                                points.Add(new GaugePointRecord
                                {
                                    MetricName = name,
                                    Description = description,
                                    Unit = unit,
                                    ServiceName = serviceName,
                                    ResourceSchemaUrl = resourceSchemaUrl,
                                    ResourceAttributes = resourceAttributes,
                                    ScopeSchemaUrl = scopeSchemaUrl,
                                    ScopeName = scopeName,
                                    ScopeVersion = scopeVersion,
                                    ScopeAttributes = scopeAttributes,
                                    DataPointAttributes = Flatten(dp.Attributes),
                                    StartTime = dp.StartTimeUnixNano == 0 ? null : FromUnixNano(dp.StartTimeUnixNano),
                                    Time = FromUnixNano(dp.TimeUnixNano),
                                    Value = NumberValue(dp),
                                });
                            }
                            break;

                        case Metric.DataOneofCase.Sum:
                            foreach (var dp in metric.Sum.DataPoints)
                            {
                                points.Add(new SumPointRecord
                                {
                                    MetricName = name,
                                    Description = description,
                                    Unit = unit,
                                    ServiceName = serviceName,
                                    ResourceSchemaUrl = resourceSchemaUrl,
                                    ResourceAttributes = resourceAttributes,
                                    ScopeSchemaUrl = scopeSchemaUrl,
                                    ScopeName = scopeName,
                                    ScopeVersion = scopeVersion,
                                    ScopeAttributes = scopeAttributes,
                                    DataPointAttributes = Flatten(dp.Attributes),
                                    StartTime = dp.StartTimeUnixNano == 0 ? null : FromUnixNano(dp.StartTimeUnixNano),
                                    Time = FromUnixNano(dp.TimeUnixNano),
                                    Value = NumberValue(dp),
                                    AggregationTemporality = (int)metric.Sum.AggregationTemporality,
                                    IsMonotonic = metric.Sum.IsMonotonic,
                                });
                            }
                            break;

                        case Metric.DataOneofCase.Histogram:
                            foreach (var dp in metric.Histogram.DataPoints)
                            {
                                points.Add(new HistogramPointRecord
                                {
                                    MetricName = name,
                                    Description = description,
                                    Unit = unit,
                                    ServiceName = serviceName,
                                    ResourceSchemaUrl = resourceSchemaUrl,
                                    ResourceAttributes = resourceAttributes,
                                    ScopeSchemaUrl = scopeSchemaUrl,
                                    ScopeName = scopeName,
                                    ScopeVersion = scopeVersion,
                                    ScopeAttributes = scopeAttributes,
                                    DataPointAttributes = Flatten(dp.Attributes),
                                    StartTime = dp.StartTimeUnixNano == 0 ? null : FromUnixNano(dp.StartTimeUnixNano),
                                    Time = FromUnixNano(dp.TimeUnixNano),
                                    AggregationTemporality = (int)metric.Histogram.AggregationTemporality,
                                    Count = dp.Count,
                                    Sum = dp.HasSum ? dp.Sum : null,
                                    BucketCounts = [.. dp.BucketCounts],
                                    ExplicitBounds = [.. dp.ExplicitBounds],
                                });
                            }
                            break;

                        default:
                            // ExponentialHistogram, Summary, or no data set at all (Metric.DataOneofCase.None) -
                            // see MetricPointRecord's remarks for why these are out of v1 scope.
                            unsupported.Add(name);
                            break;
                    }
                }
            }
        }

        return new MetricMapResult(points, [.. unsupported]);
    }

    private static double NumberValue(NumberDataPoint dp) => dp.ValueCase switch
    {
        NumberDataPoint.ValueOneofCase.AsDouble => dp.AsDouble,
        NumberDataPoint.ValueOneofCase.AsInt => dp.AsInt,
        _ => 0d,
    };

    private static DateTimeOffset FromUnixNano(ulong unixNano) =>
        DateTimeOffset.UnixEpoch.AddTicks((long)(unixNano / 100));

    /// <summary>
    /// Proto3 string fields default to <c>""</c> when unset on the wire - there's no way
    /// to distinguish "unset" from "explicitly empty" at the protobuf level. Flare treats
    /// both as "absent" for every nullable string field on <see cref="MetricPointRecord"/>.
    /// </summary>
    private static string? EmptyToNull(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private static Dictionary<string, string> Flatten(IEnumerable<KeyValue>? attributes)
    {
        var result = new Dictionary<string, string>();
        if (attributes is null)
        {
            return result;
        }

        foreach (var kv in attributes)
        {
            var value = AnyValueToString(kv.Value);
            if (value is not null)
            {
                result[kv.Key] = value;
            }
        }

        return result;
    }

    private static string? AnyValueToString(AnyValue? value) => value?.ValueCase switch
    {
        AnyValue.ValueOneofCase.StringValue => value.StringValue,
        AnyValue.ValueOneofCase.BoolValue => value.BoolValue ? "true" : "false",
        AnyValue.ValueOneofCase.IntValue => value.IntValue.ToString(CultureInfo.InvariantCulture),
        AnyValue.ValueOneofCase.DoubleValue => value.DoubleValue.ToString(CultureInfo.InvariantCulture),
        AnyValue.ValueOneofCase.BytesValue => Convert.ToBase64String(value.BytesValue.Span),
        AnyValue.ValueOneofCase.ArrayValue => "[" + string.Join(",", value.ArrayValue.Values.Select(AnyValueToString)) + "]",
        AnyValue.ValueOneofCase.KvlistValue => "{" + string.Join(",", value.KvlistValue.Values.Select(kv => $"{kv.Key}={AnyValueToString(kv.Value)}")) + "}",
        _ => null,
    };
}
