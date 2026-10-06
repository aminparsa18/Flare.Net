using Flare.Ingest.Otlp;
using Google.Protobuf;
using OpenTelemetry.Proto.Collector.Profiles.V1Development;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Profiles.V1Development;
using OpenTelemetry.Proto.Resource.V1;
using Xunit;
using ValueType = OpenTelemetry.Proto.Profiles.V1Development.ValueType;

namespace Flare.Ingest.Tests;

public class OtlpProfilesMapperTests
{
    private static readonly DateTimeOffset TestIngestedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Dictionary: strings [ "", cpu, nanoseconds, main, foo, bar, libc.so, service.name, region ],
    /// functions [zero, main, foo, bar], locations [zero, main, foo, bar], stacks [zero, bar->foo->main (leaf first)].
    /// </summary>
    private static ExportProfilesServiceRequest Request(Action<Profile>? configureProfile = null, Action<ProfilesDictionary>? configureDictionary = null)
    {
        var dictionary = new ProfilesDictionary();
        dictionary.StringTable.AddRange(["", "cpu", "nanoseconds", "main", "foo", "bar", "libc.so", "region"]);
        dictionary.FunctionTable.Add(new Function());
        for (var i = 3; i <= 5; i++)
        {
            dictionary.FunctionTable.Add(new Function { NameStrindex = i });
        }
        dictionary.LocationTable.Add(new Location());
        for (var i = 1; i <= 3; i++)
        {
            var location = new Location();
            location.Lines.Add(new Line { FunctionIndex = i });
            dictionary.LocationTable.Add(location);
        }
        dictionary.StackTable.Add(new Stack());
        var stack = new Stack();
        stack.LocationIndices.AddRange([3, 2, 1]); // bar (leaf), foo, main (root)
        dictionary.StackTable.Add(stack);
        dictionary.LinkTable.Add(new Link { TraceId = ByteString.CopyFrom(new byte[16]), SpanId = ByteString.CopyFrom(new byte[8]) });
        dictionary.MappingTable.Add(new Mapping());
        dictionary.AttributeTable.Add(new KeyValueAndUnit());
        configureDictionary?.Invoke(dictionary);

        var profile = new Profile
        {
            SampleType = new ValueType { TypeStrindex = 1, UnitStrindex = 2 },
            TimeUnixNano = 1_700_000_000_000_000_000UL,
            DurationNano = 5_000_000_000UL,
        };
        profile.Samples.Add(new Sample { StackIndex = 1, Values = { 10, 5 } });
        configureProfile?.Invoke(profile);

        var scope = new ScopeProfiles();
        scope.Profiles.Add(profile);
        var resource = new ResourceProfiles
        {
            Resource = new Resource { Attributes = { new KeyValue { Key = "service.name", Value = new AnyValue { StringValue = "checkout" } } } },
        };
        resource.ScopeProfiles.Add(scope);

        var request = new ExportProfilesServiceRequest { Dictionary = dictionary };
        request.ResourceProfiles.Add(resource);
        return request;
    }

    [Fact]
    public void Map_ReturnsEmpty_WhenNoResourceProfiles()
    {
        Assert.Empty(OtlpProfilesMapper.Map(new ExportProfilesServiceRequest(), TestIngestedAt));
    }

    [Fact]
    public void Map_ResolvesStackRootFirst_FromLeafFirstWire()
    {
        var record = Assert.Single(OtlpProfilesMapper.Map(Request(), TestIngestedAt));

        Assert.Equal(["main", "foo", "bar"], record.Stack);
    }

    [Fact]
    public void Map_ResolvesSampleTypeUnitServiceAndStamps()
    {
        var record = Assert.Single(OtlpProfilesMapper.Map(Request(), TestIngestedAt));

        Assert.Equal("cpu", record.SampleType);
        Assert.Equal("nanoseconds", record.SampleUnit);
        Assert.Equal("checkout", record.ServiceName);
        Assert.Equal(5_000_000_000UL, record.DurationNano);
        Assert.Equal(TestIngestedAt, record.IngestedAt);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000), record.Timestamp);
    }

    [Fact]
    public void Map_SumsValues()
    {
        var record = Assert.Single(OtlpProfilesMapper.Map(Request(), TestIngestedAt));

        Assert.Equal(15, record.Value);
    }

    [Fact]
    public void Map_TimestampsOnlySample_CountsEachTimestampAsOne_AndUsesTheEarliest()
    {
        var request = Request(p =>
        {
            p.Samples.Clear();
            var sample = new Sample { StackIndex = 1 };
            sample.TimestampsUnixNano.AddRange([1_700_000_003_000_000_000UL, 1_700_000_001_000_000_000UL, 1_700_000_002_000_000_000UL]);
            p.Samples.Add(sample);
        });

        var record = Assert.Single(OtlpProfilesMapper.Map(request, TestIngestedAt));

        Assert.Equal(3, record.Value);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_700_000_001), record.Timestamp);
    }

    [Fact]
    public void Map_SkipsSampleWithNeitherValuesNorTimestamps()
    {
        var request = Request(p => p.Samples.Add(new Sample { StackIndex = 1 }));

        Assert.Single(OtlpProfilesMapper.Map(request, TestIngestedAt));
    }

    [Fact]
    public void Map_ExpandsInlinedLines_InnermostFirstOnTheWire_SoCallerIsNearerTheRoot()
    {
        // One location with lines [bar (inlined), foo (caller)] under a main location.
        var request = Request(configureDictionary: d =>
        {
            var inlined = new Location();
            inlined.Lines.Add(new Line { FunctionIndex = 3 }); // bar
            inlined.Lines.Add(new Line { FunctionIndex = 2 }); // foo
            d.LocationTable.Add(inlined);
            var stack = new Stack();
            stack.LocationIndices.AddRange([d.LocationTable.Count - 1, 1]);
            d.StackTable.Add(stack);
        }, configureProfile: p =>
        {
            p.Samples.Clear();
            p.Samples.Add(new Sample { StackIndex = 2, Values = { 1 } });
        });

        var record = Assert.Single(OtlpProfilesMapper.Map(request, TestIngestedAt));

        Assert.Equal(["main", "foo", "bar"], record.Stack);
    }

    [Fact]
    public void Map_UnsymbolizedNativeFrame_KeepsModuleAndAddress()
    {
        var request = Request(configureDictionary: d =>
        {
            d.MappingTable.Add(new Mapping { FilenameStrindex = 6 });
            d.LocationTable.Add(new Location { MappingIndex = d.MappingTable.Count - 1, Address = 0xabc });
            var stack = new Stack();
            stack.LocationIndices.Add(d.LocationTable.Count - 1);
            d.StackTable.Add(stack);
        }, configureProfile: p =>
        {
            p.Samples.Clear();
            p.Samples.Add(new Sample { StackIndex = 2, Values = { 1 } });
        });

        var record = Assert.Single(OtlpProfilesMapper.Map(request, TestIngestedAt));

        Assert.Equal(["libc.so+0xabc"], record.Stack);
    }

    [Fact]
    public void Map_OutOfRangeIndices_ResolveToZeroValuesInsteadOfThrowing()
    {
        var request = Request(p =>
        {
            p.SampleType = new ValueType { TypeStrindex = 99, UnitStrindex = 99 };
            p.Samples.Clear();
            p.Samples.Add(new Sample { StackIndex = 99, LinkIndex = 99, Values = { 7 }, AttributeIndices = { 99 } });
        });

        var record = Assert.Single(OtlpProfilesMapper.Map(request, TestIngestedAt));

        Assert.Empty(record.Stack);
        Assert.Null(record.SampleType);
        Assert.Null(record.TraceId);
        Assert.Empty(record.SampleAttributes);
    }

    [Fact]
    public void Map_ResolvesLinkToLowerHexTraceAndSpanIds()
    {
        var traceId = Convert.FromHexString("0102030405060708090a0b0c0d0e0f10");
        var spanId = Convert.FromHexString("a1a2a3a4a5a6a7a8");
        var request = Request(
            p =>
            {
                p.Samples.Clear();
                p.Samples.Add(new Sample { StackIndex = 1, LinkIndex = 1, Values = { 1 } });
            },
            d => d.LinkTable.Add(new Link { TraceId = ByteString.CopyFrom(traceId), SpanId = ByteString.CopyFrom(spanId) }));

        var record = Assert.Single(OtlpProfilesMapper.Map(request, TestIngestedAt));

        Assert.Equal("0102030405060708090a0b0c0d0e0f10", record.TraceId);
        Assert.Equal("a1a2a3a4a5a6a7a8", record.SpanId);
    }

    [Fact]
    public void Map_ZeroLink_MapsToNull()
    {
        var record = Assert.Single(OtlpProfilesMapper.Map(Request(), TestIngestedAt));

        Assert.Null(record.TraceId);
        Assert.Null(record.SpanId);
    }

    [Fact]
    public void Map_ResolvesSampleAttributes_FromTheAttributeTable()
    {
        var request = Request(
            p =>
            {
                p.Samples.Clear();
                p.Samples.Add(new Sample { StackIndex = 1, Values = { 1 }, AttributeIndices = { 1 } });
            },
            d => d.AttributeTable.Add(new KeyValueAndUnit { KeyStrindex = 7, Value = new AnyValue { StringValue = "eu" } }));

        var record = Assert.Single(OtlpProfilesMapper.Map(request, TestIngestedAt));

        Assert.Equal("eu", record.SampleAttributes["region"]);
    }

    [Fact]
    public void Map_ProfileId_IsLowerHex_AndAllZeroMapsToNull()
    {
        var withId = Assert.Single(OtlpProfilesMapper.Map(Request(p => p.ProfileId = ByteString.CopyFrom(Convert.FromHexString("00ff"))), TestIngestedAt));
        var zero = Assert.Single(OtlpProfilesMapper.Map(Request(p => p.ProfileId = ByteString.CopyFrom(new byte[16])), TestIngestedAt));

        Assert.Equal("00ff", withId.ProfileId);
        Assert.Null(zero.ProfileId);
    }

    [Fact]
    public void RoundTripsThroughProtobufWireFormat()
    {
        var parsed = ExportProfilesServiceRequest.Parser.ParseFrom(Request().ToByteArray());

        var record = Assert.Single(OtlpProfilesMapper.Map(parsed, TestIngestedAt));

        Assert.Equal(["main", "foo", "bar"], record.Stack);
    }
}
