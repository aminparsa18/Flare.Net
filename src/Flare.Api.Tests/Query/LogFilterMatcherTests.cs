using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class LogFilterMatcherTests
{
    [Fact]
    public void Matches_WithNoFilters_ReturnsTrue()
    {
        Assert.True(LogFilterMatcher.Matches(MinimalLogEvent(), new LogFilter()));
    }

    [Fact]
    public void Matches_IgnoresFromAndTo()
    {
        // A live tail is inherently open-ended; From/To (which bound /api/logs/search's
        // historical range) don't apply here - see LogFilterMatcher's remarks.
        var logEvent = MinimalLogEvent() with { Timestamp = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero) };
        var filter = new LogFilter
        {
            From = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            To = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero),
        };

        Assert.True(LogFilterMatcher.Matches(logEvent, filter));
    }

    [Theory]
    [InlineData("flare-ingest", true)]
    [InlineData("payments-api", false)]
    public void Matches_Services_IsExactMatch(string serviceName, bool expected)
    {
        var logEvent = MinimalLogEvent() with { ServiceName = "flare-ingest" };
        var filter = new LogFilter { Services = [serviceName] };

        Assert.Equal(expected, LogFilterMatcher.Matches(logEvent, filter));
    }

    [Theory]
    [InlineData((byte)17, true)]
    [InlineData((byte)9, false)]
    public void Matches_SeverityNumbers_IsExactMatch(byte severityNumber, bool expected)
    {
        var logEvent = MinimalLogEvent() with { SeverityNumber = 17 };
        var filter = new LogFilter { SeverityNumbers = [severityNumber] };

        Assert.Equal(expected, LogFilterMatcher.Matches(logEvent, filter));
    }

    [Theory]
    [InlineData("0102030405060708090a0b0c0d0e0f10", true)]
    [InlineData("ffffffffffffffffffffffffffffffff", false)]
    public void Matches_TraceId_IsExactMatch(string traceId, bool expected)
    {
        var logEvent = MinimalLogEvent() with { TraceId = "0102030405060708090a0b0c0d0e0f10" };
        var filter = new LogFilter { TraceId = traceId };

        Assert.Equal(expected, LogFilterMatcher.Matches(logEvent, filter));
    }

    [Theory]
    [InlineData("boom", true)]
    [InlineData("BOOM", true)]
    [InlineData("nope", false)]
    public void Matches_Search_IsCaseInsensitiveSubstring(string search, bool expected)
    {
        var logEvent = MinimalLogEvent() with { Body = "it went boom today" };
        var filter = new LogFilter { Search = search };

        Assert.Equal(expected, LogFilterMatcher.Matches(logEvent, filter));
    }

    [Theory]
    [InlineData(AttributeBag.Log, "GET", true)]
    [InlineData(AttributeBag.Log, "POST", false)]
    [InlineData(AttributeBag.Resource, "GET", false)]
    public void Matches_AttributeFilter_UsesTheRightBag(AttributeBag bag, string value, bool expected)
    {
        var logEvent = MinimalLogEvent() with
        {
            LogAttributes = new Dictionary<string, string> { ["http.method"] = "GET" },
        };
        var filter = new LogFilter { Attributes = [new AttributeFilter { Bag = bag, Key = "http.method", Value = value }] };

        Assert.Equal(expected, LogFilterMatcher.Matches(logEvent, filter));
    }

    [Fact]
    public void Matches_AttributeFilter_ReturnsFalse_WhenKeyIsAbsent()
    {
        var logEvent = MinimalLogEvent();
        var filter = new LogFilter { Attributes = [new AttributeFilter { Key = "missing.key", Value = "anything" }] };

        Assert.False(LogFilterMatcher.Matches(logEvent, filter));
    }

    [Theory]
    [InlineData("GET", false)] // present, equals -> NotEquals fails
    [InlineData("POST", true)] // present, differs -> NotEquals passes
    public void Matches_NotEqualsOperator_WhenKeyIsPresent(string filterValue, bool expected)
    {
        var logEvent = MinimalLogEvent() with
        {
            LogAttributes = new Dictionary<string, string> { ["http.method"] = "GET" },
        };
        var filter = new LogFilter { Attributes = [new AttributeFilter { Key = "http.method", Value = filterValue, Operator = AttributeFilterOperator.NotEquals }] };

        Assert.Equal(expected, LogFilterMatcher.Matches(logEvent, filter));
    }

    [Fact]
    public void Matches_NotEqualsOperator_ReturnsTrue_WhenKeyIsAbsent()
    {
        var logEvent = MinimalLogEvent();
        var filter = new LogFilter { Attributes = [new AttributeFilter { Key = "missing.key", Value = "anything", Operator = AttributeFilterOperator.NotEquals }] };

        Assert.True(LogFilterMatcher.Matches(logEvent, filter));
    }

    [Theory]
    [InlineData("http.method", true)]
    [InlineData("missing.key", false)]
    public void Matches_ExistsOperator_ChecksKeyPresence_IgnoringValue(string key, bool expected)
    {
        var logEvent = MinimalLogEvent() with
        {
            LogAttributes = new Dictionary<string, string> { ["http.method"] = "GET" },
        };
        var filter = new LogFilter { Attributes = [new AttributeFilter { Key = key, Value = "", Operator = AttributeFilterOperator.Exists }] };

        Assert.Equal(expected, LogFilterMatcher.Matches(logEvent, filter));
    }

    [Theory]
    [InlineData("http.method", false)]
    [InlineData("missing.key", true)]
    public void Matches_AbsentOperator_ChecksKeyAbsence_IgnoringValue(string key, bool expected)
    {
        var logEvent = MinimalLogEvent() with
        {
            LogAttributes = new Dictionary<string, string> { ["http.method"] = "GET" },
        };
        var filter = new LogFilter { Attributes = [new AttributeFilter { Key = key, Value = "", Operator = AttributeFilterOperator.Absent }] };

        Assert.Equal(expected, LogFilterMatcher.Matches(logEvent, filter));
    }

    [Theory]
    [InlineData("^/api/v[0-9]+/users", true)] // matches
    [InlineData("^/internal/", false)] // doesn't match
    public void Matches_RegexOperator_WhenKeyIsPresent(string pattern, bool expected)
    {
        var logEvent = MinimalLogEvent() with
        {
            LogAttributes = new Dictionary<string, string> { ["http.route"] = "/api/v2/users/123" },
        };
        var filter = new LogFilter { Attributes = [new AttributeFilter { Key = "http.route", Value = pattern, Operator = AttributeFilterOperator.Regex }] };

        Assert.Equal(expected, LogFilterMatcher.Matches(logEvent, filter));
    }

    [Fact]
    public void Matches_RegexOperator_ReturnsFalse_WhenKeyIsAbsent()
    {
        var logEvent = MinimalLogEvent();
        var filter = new LogFilter { Attributes = [new AttributeFilter { Key = "missing.key", Value = ".*", Operator = AttributeFilterOperator.Regex }] };

        Assert.False(LogFilterMatcher.Matches(logEvent, filter));
    }

    [Fact]
    public void Matches_RegexOperator_ReturnsFalse_OnInvalidPattern()
    {
        // Fail-closed, not an exception - see LogFilterMatcher.RegexMatches's remarks on
        // why an uncaught RegexParseException here would be worse than a false negative.
        var logEvent = MinimalLogEvent() with
        {
            LogAttributes = new Dictionary<string, string> { ["http.route"] = "/api/v2/users" },
        };
        var filter = new LogFilter { Attributes = [new AttributeFilter { Key = "http.route", Value = "(unclosed", Operator = AttributeFilterOperator.Regex }] };

        Assert.False(LogFilterMatcher.Matches(logEvent, filter));
    }

    [Theory]
    [InlineData("^/api/v[0-9]+/users", false)] // matches -> NotRegex fails
    [InlineData("^/internal/", true)] // doesn't match -> NotRegex passes
    public void Matches_NotRegexOperator_WhenKeyIsPresent(string pattern, bool expected)
    {
        var logEvent = MinimalLogEvent() with
        {
            LogAttributes = new Dictionary<string, string> { ["http.route"] = "/api/v2/users/123" },
        };
        var filter = new LogFilter { Attributes = [new AttributeFilter { Key = "http.route", Value = pattern, Operator = AttributeFilterOperator.NotRegex }] };

        Assert.Equal(expected, LogFilterMatcher.Matches(logEvent, filter));
    }

    [Fact]
    public void Matches_NotRegexOperator_ReturnsTrue_WhenKeyIsAbsent()
    {
        var logEvent = MinimalLogEvent();
        var filter = new LogFilter { Attributes = [new AttributeFilter { Key = "missing.key", Value = ".*", Operator = AttributeFilterOperator.NotRegex }] };

        Assert.True(LogFilterMatcher.Matches(logEvent, filter));
    }

    [Theory]
    [InlineData(new[] { "500", "502", "503" }, true)] // present in the list
    [InlineData(new[] { "200", "201" }, false)] // present but not in the list
    public void Matches_InOperator_WhenKeyIsPresent(string[] values, bool expected)
    {
        var logEvent = MinimalLogEvent() with
        {
            LogAttributes = new Dictionary<string, string> { ["http.status_code"] = "502" },
        };
        var filter = new LogFilter { Attributes = [new AttributeFilter { Key = "http.status_code", Value = "", Operator = AttributeFilterOperator.In, Values = values }] };

        Assert.Equal(expected, LogFilterMatcher.Matches(logEvent, filter));
    }

    [Fact]
    public void Matches_InOperator_ReturnsFalse_WhenKeyIsAbsent()
    {
        var logEvent = MinimalLogEvent();
        var filter = new LogFilter { Attributes = [new AttributeFilter { Key = "missing.key", Value = "", Operator = AttributeFilterOperator.In, Values = ["500"] }] };

        Assert.False(LogFilterMatcher.Matches(logEvent, filter));
    }

    [Fact]
    public void Matches_InOperator_ReturnsFalse_WhenValuesIsNullOrEmpty()
    {
        var logEvent = MinimalLogEvent() with
        {
            LogAttributes = new Dictionary<string, string> { ["http.status_code"] = "502" },
        };
        var filter = new LogFilter { Attributes = [new AttributeFilter { Key = "http.status_code", Value = "", Operator = AttributeFilterOperator.In }] };

        Assert.False(LogFilterMatcher.Matches(logEvent, filter));
    }

    [Theory]
    [InlineData(new[] { "500", "502", "503" }, false)] // present in the list -> NotIn fails
    [InlineData(new[] { "200", "201" }, true)] // present but not in the list -> NotIn passes
    public void Matches_NotInOperator_WhenKeyIsPresent(string[] values, bool expected)
    {
        var logEvent = MinimalLogEvent() with
        {
            LogAttributes = new Dictionary<string, string> { ["http.status_code"] = "502" },
        };
        var filter = new LogFilter { Attributes = [new AttributeFilter { Key = "http.status_code", Value = "", Operator = AttributeFilterOperator.NotIn, Values = values }] };

        Assert.Equal(expected, LogFilterMatcher.Matches(logEvent, filter));
    }

    [Fact]
    public void Matches_NotInOperator_ReturnsTrue_WhenKeyIsAbsent()
    {
        var logEvent = MinimalLogEvent();
        var filter = new LogFilter { Attributes = [new AttributeFilter { Key = "missing.key", Value = "", Operator = AttributeFilterOperator.NotIn, Values = ["500"] }] };

        Assert.True(LogFilterMatcher.Matches(logEvent, filter));
    }

    [Fact]
    public void Matches_NotInOperator_ReturnsTrue_WhenValuesIsNullOrEmpty()
    {
        var logEvent = MinimalLogEvent() with
        {
            LogAttributes = new Dictionary<string, string> { ["http.status_code"] = "502" },
        };
        var filter = new LogFilter { Attributes = [new AttributeFilter { Key = "http.status_code", Value = "", Operator = AttributeFilterOperator.NotIn }] };

        Assert.True(LogFilterMatcher.Matches(logEvent, filter));
    }

    [Fact]
    public void Matches_MultipleAttributeFilters_AreAnded()
    {
        var logEvent = MinimalLogEvent() with
        {
            LogAttributes = new Dictionary<string, string> { ["http.method"] = "GET", ["http.status_code"] = "500" },
        };
        var filter = new LogFilter
        {
            Attributes =
            [
                new AttributeFilter { Key = "http.method", Value = "GET" },
                new AttributeFilter { Key = "http.status_code", Value = "404" }, // doesn't match
            ],
        };

        Assert.False(LogFilterMatcher.Matches(logEvent, filter));
    }

    private static LogEventDto MinimalLogEvent() => new()
    {
        EventId = Guid.NewGuid(),
        Timestamp = DateTimeOffset.UnixEpoch,
        ObservedTimestamp = DateTimeOffset.UnixEpoch,
        IngestedAt = DateTimeOffset.UnixEpoch,
        TraceId = string.Empty,
        SpanId = string.Empty,
        TraceFlags = 0,
        SeverityText = string.Empty,
        SeverityNumber = 9,
        ServiceName = string.Empty,
        Body = string.Empty,
        ResourceSchemaUrl = string.Empty,
        ResourceAttributes = new Dictionary<string, string>(),
        ScopeSchemaUrl = string.Empty,
        ScopeName = string.Empty,
        ScopeVersion = string.Empty,
        ScopeAttributes = new Dictionary<string, string>(),
        LogAttributes = new Dictionary<string, string>(),
        EventName = string.Empty,
        PatternId = string.Empty,
        PatternTemplate = string.Empty,
    };
}
