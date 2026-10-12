using System.Globalization;
using Flare.Ingest.Model;
using OpenTelemetry.Proto.Collector.Profiles.V1Development;
using OpenTelemetry.Proto.Profiles.V1Development;

namespace Flare.Ingest.Otlp;

/// <summary>
/// Maps a parsed OTLP <see cref="ExportProfilesServiceRequest"/> into <see cref="ProfileSampleRecord"/>s,
/// resolving the request-wide <see cref="ProfilesDictionary"/> (strings, functions, locations,
/// stacks, links, attributes) per sample. Shared by the gRPC and HTTP receivers.
/// </summary>
/// <remarks>
/// OTLP profiles is Alpha (ADR-0141); every dictionary index is bounds-checked and resolves to the
/// zero value when out of range, since a producer bug there must not fail the whole export.
/// </remarks>
public static class OtlpProfilesMapper
{
    /// <summary>Frame name used when a location has neither a function name nor an address.</summary>
    public const string UnknownFrame = "[unknown]";

    public static IEnumerable<ProfileSampleRecord> Map(ExportProfilesServiceRequest request, DateTimeOffset ingestedAt)
    {
        var dictionary = request.Dictionary ?? new ProfilesDictionary();

        // A location's frames don't depend on the sample, and one location is shared by many stacks.
        var frameCache = new Dictionary<int, string[]>();

        foreach (var resourceProfiles in request.ResourceProfiles)
        {
            var resourceAttributes = OtlpAnyValue.Flatten(resourceProfiles.Resource?.Attributes);
            var serviceName = resourceAttributes.GetValueOrDefault("service.name");

            foreach (var scopeProfiles in resourceProfiles.ScopeProfiles)
            {
                foreach (var profile in scopeProfiles.Profiles)
                {
                    var sampleType = Str(dictionary, profile.SampleType?.TypeStrindex ?? 0);
                    var sampleUnit = Str(dictionary, profile.SampleType?.UnitStrindex ?? 0);
                    var profileId = profile.ProfileId.IsEmpty || profile.ProfileId.All(b => b == 0)
                        ? null
                        : Convert.ToHexStringLower(profile.ProfileId.Span);

                    foreach (var sample in profile.Samples)
                    {
                        var hasValues = sample.Values.Count > 0;
                        var hasTimestamps = sample.TimestampsUnixNano.Count > 0;
                        if (!hasValues && !hasTimestamps)
                        {
                            // Spec: a Sample MUST carry at least one of the two. Nothing to measure.
                            continue;
                        }

                        var (traceId, spanId) = ResolveLink(dictionary, sample.LinkIndex);

                        yield return new ProfileSampleRecord
                        {
                            Timestamp = FromUnixNano(hasTimestamps ? sample.TimestampsUnixNano.Min() : profile.TimeUnixNano),
                            ProfileId = profileId,
                            DurationNano = profile.DurationNano,
                            ServiceName = serviceName,
                            SampleType = EmptyToNull(sampleType),
                            SampleUnit = EmptyToNull(sampleUnit),
                            Stack = ResolveStack(dictionary, sample.StackIndex, frameCache),
                            Value = hasValues ? sample.Values.Sum() : sample.TimestampsUnixNano.Count,
                            TraceId = traceId,
                            SpanId = spanId,
                            ResourceAttributes = resourceAttributes,
                            SampleAttributes = ResolveAttributes(dictionary, sample.AttributeIndices),
                            IngestedAt = ingestedAt,
                        };
                    }
                }
            }
        }
    }

    /// <summary>Stack frames root-first: the wire is leaf-first, and each location expands to its inlined lines.</summary>
    private static string[] ResolveStack(ProfilesDictionary dictionary, int stackIndex, Dictionary<int, string[]> frameCache)
    {
        if (stackIndex <= 0 || stackIndex >= dictionary.StackTable.Count)
        {
            return [];
        }

        var frames = new List<string>();
        foreach (var locationIndex in dictionary.StackTable[stackIndex].LocationIndices)
        {
            if (!frameCache.TryGetValue(locationIndex, out var locationFrames))
            {
                locationFrames = ResolveLocation(dictionary, locationIndex);
                frameCache[locationIndex] = locationFrames;
            }
            frames.AddRange(locationFrames);
        }

        frames.Reverse();
        return [.. frames];
    }

    /// <summary>
    /// Frames for one location, innermost inlined function first (matching the wire's lines
    /// order, so the caller-side reversal in <see cref="ResolveStack"/> yields root-first overall).
    /// </summary>
    private static string[] ResolveLocation(ProfilesDictionary dictionary, int locationIndex)
    {
        if (locationIndex < 0 || locationIndex >= dictionary.LocationTable.Count)
        {
            return [UnknownFrame];
        }

        var location = dictionary.LocationTable[locationIndex];
        var names = new List<string>();
        foreach (var line in location.Lines)
        {
            var function = line.FunctionIndex > 0 && line.FunctionIndex < dictionary.FunctionTable.Count
                ? dictionary.FunctionTable[line.FunctionIndex]
                : null;
            var name = function is null
                ? string.Empty
                : Str(dictionary, function.NameStrindex) is { Length: > 0 } n ? n : Str(dictionary, function.SystemNameStrindex);
            names.Add(name.Length > 0 ? name : UnknownFrame);
        }

        if (names.Count > 0)
        {
            return [.. names];
        }

        // Native frame the profiler couldn't symbolize: keep the module + address so distinct
        // unsymbolized frames stay distinct in a flame graph instead of all collapsing to one.
        var module = location.MappingIndex > 0 && location.MappingIndex < dictionary.MappingTable.Count
            ? Str(dictionary, dictionary.MappingTable[location.MappingIndex].FilenameStrindex)
            : string.Empty;
        if (location.Address == 0 && module.Length == 0)
        {
            return [UnknownFrame];
        }

        var address = "0x" + location.Address.ToString("x", CultureInfo.InvariantCulture);
        return [module.Length > 0 ? $"{Path.GetFileName(module)}+{address}" : address];
    }

    private static (string? TraceId, string? SpanId) ResolveLink(ProfilesDictionary dictionary, int linkIndex)
    {
        if (linkIndex <= 0 || linkIndex >= dictionary.LinkTable.Count)
        {
            return (null, null);
        }

        var link = dictionary.LinkTable[linkIndex];
        // Per spec link_table[0] is an all-zero link; a zero id is "no span", not a real one.
        var traceId = link.TraceId.IsEmpty || link.TraceId.All(b => b == 0) ? null : Convert.ToHexStringLower(link.TraceId.Span);
        var spanId = link.SpanId.IsEmpty || link.SpanId.All(b => b == 0) ? null : Convert.ToHexStringLower(link.SpanId.Span);
        return (traceId, spanId);
    }

    private static Dictionary<string, string> ResolveAttributes(ProfilesDictionary dictionary, IEnumerable<int> attributeIndices)
    {
        var result = new Dictionary<string, string>();
        foreach (var index in attributeIndices)
        {
            if (index <= 0 || index >= dictionary.AttributeTable.Count)
            {
                continue;
            }

            var attribute = dictionary.AttributeTable[index];
            var key = Str(dictionary, attribute.KeyStrindex);
            var value = OtlpAnyValue.ToFlatString(attribute.Value);
            if (key.Length > 0 && value is not null)
            {
                result[key] = value;
            }
        }
        return result;
    }

    private static string Str(ProfilesDictionary dictionary, int index) =>
        index > 0 && index < dictionary.StringTable.Count ? dictionary.StringTable[index] : string.Empty;

    private static DateTimeOffset FromUnixNano(ulong unixNano) =>
        DateTimeOffset.UnixEpoch.AddTicks((long)(unixNano / 100));

    private static string? EmptyToNull(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
