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
    [InlineData("MyApp.Orders.OrderService", true)]
    [InlineData("MyApp.Orders", false)]
    [InlineData("MyApp.Orders.*", true)]
    [InlineData("MyApp.*", true)]
    [InlineData("MyApp.Cart.*", false)]
    [InlineData("myapp.orders.*", false)]
    [InlineData("*", true)]
    public void Matches_ScopeNames_IsExactOrStarSuffixedPrefixMatch(string scopeName, bool expected)
    {
        var logEvent = MinimalLogEvent() with { ScopeName = "MyApp.Orders.OrderService" };
        var filter = new LogFilter { ScopeNames = [scopeName] };

        Assert.Equal(expected, LogFilterMatcher.Matches(logEvent, filter));
    }

    [Fact]
    public void Matches_ScopeNames_AnyEntryMatching_IsEnough()
    {
        var logEvent = MinimalLogEvent() with { ScopeName = "Microsoft.EntityFrameworkCore.Database.Command" };
        var filter = new LogFilter { ScopeNames = ["MyApp.Orders.OrderService", "Microsoft.EntityFrameworkCore.*"] };

        Assert.True(LogFilterMatcher.Matches(logEvent, filter));
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

    [Theory]
    [InlineData("42", true)]
    [InlineData("43", false)]
    public void Matches_BodyJsonFilter_ResolvesNestedPath(string value, bool expected)
    {
        var logEvent = MinimalLogEvent() with { Body = """{"user":{"id":"42"}}""" };
        var filter = new LogFilter { BodyJsonFilters = [new BodyJsonFilter { Path = "user.id", Value = value }] };

        Assert.Equal(expected, LogFilterMatcher.Matches(logEvent, filter));
    }

    [Theory]
    [InlineData("""{"user":{"roles":["admin","dev"]}}""", "admin", true)]
    [InlineData("""{"user":{"roles":["admin","dev"]}}""", "ops", false)]
    [InlineData("""{"user":{"roles":[1,2.5,true]}}""", "2.5", true)]
    [InlineData("""{"user":{"roles":[1,2.5,true]}}""", "true", true)]
    [InlineData("""{"user":{"roles":[null]}}""", "", true)]
    [InlineData("""{"user":{"roles":"admin"}}""", "admin", false)]
    [InlineData("""{"user":{}}""", "admin", false)]
    [InlineData("not json", "admin", false)]
    public void Matches_BodyJsonHasOperator_MatchesArrayElement(string body, string value, bool expected)
    {
        var logEvent = MinimalLogEvent() with { Body = body };
        var filter = new LogFilter { BodyJsonFilters = [new BodyJsonFilter { Path = "user.roles", Value = value, Operator = BodyJsonFilterOperator.Has }] };

        Assert.Equal(expected, LogFilterMatcher.Matches(logEvent, filter));
    }

    [Theory]
    [InlineData("""{"tags":["a","b"]}""", "a", false)]
    [InlineData("""{"tags":["a","b"]}""", "c", true)]
    [InlineData("""{"tags":"a"}""", "a", true)]
    [InlineData("""{}""", "a", true)]
    [InlineData("not json", "a", true)]
    public void Matches_BodyJsonNotHasOperator_NegatesHas(string body, string value, bool expected)
    {
        var logEvent = MinimalLogEvent() with { Body = body };
        var filter = new LogFilter { BodyJsonFilters = [new BodyJsonFilter { Path = "tags", Value = value, Operator = BodyJsonFilterOperator.NotHas }] };

        Assert.Equal(expected, LogFilterMatcher.Matches(logEvent, filter));
    }

    [Fact]
    public void Matches_BodyJsonFilter_ReturnsFalse_WhenBodyIsNotJson()
    {
        var logEvent = MinimalLogEvent() with { Body = "plain text log message" };
        var filter = new LogFilter { BodyJsonFilters = [new BodyJsonFilter { Path = "user.id", Value = "42" }] };

        Assert.False(LogFilterMatcher.Matches(logEvent, filter));
    }

    [Fact]
    public void Matches_BodyJsonFilter_ReturnsFalse_WhenPathIsAbsent()
    {
        var logEvent = MinimalLogEvent() with { Body = """{"user":{"id":"42"}}""" };
        var filter = new LogFilter { BodyJsonFilters = [new BodyJsonFilter { Path = "user.missing", Value = "anything" }] };

        Assert.False(LogFilterMatcher.Matches(logEvent, filter));
    }

    [Theory]
    [InlineData("42", false)] // present, equals -> NotEquals fails
    [InlineData("43", true)] // present, differs -> NotEquals passes
    public void Matches_BodyJsonNotEqualsOperator_WhenPathIsPresent(string filterValue, bool expected)
    {
        var logEvent = MinimalLogEvent() with { Body = """{"user":{"id":"42"}}""" };
        var filter = new LogFilter { BodyJsonFilters = [new BodyJsonFilter { Path = "user.id", Value = filterValue, Operator = BodyJsonFilterOperator.NotEquals }] };

        Assert.Equal(expected, LogFilterMatcher.Matches(logEvent, filter));
    }

    [Fact]
    public void Matches_BodyJsonNotEqualsOperator_ReturnsTrue_WhenPathIsAbsent()
    {
        var logEvent = MinimalLogEvent() with { Body = """{"user":{"id":"42"}}""" };
        var filter = new LogFilter { BodyJsonFilters = [new BodyJsonFilter { Path = "user.missing", Value = "anything", Operator = BodyJsonFilterOperator.NotEquals }] };

        Assert.True(LogFilterMatcher.Matches(logEvent, filter));
    }

    [Theory]
    [InlineData("user.id", true)]
    [InlineData("user.missing", false)]
    public void Matches_BodyJsonExistsOperator_ChecksPathPresence_IgnoringValue(string path, bool expected)
    {
        var logEvent = MinimalLogEvent() with { Body = """{"user":{"id":"42"}}""" };
        var filter = new LogFilter { BodyJsonFilters = [new BodyJsonFilter { Path = path, Value = "", Operator = BodyJsonFilterOperator.Exists }] };

        Assert.Equal(expected, LogFilterMatcher.Matches(logEvent, filter));
    }

    [Fact]
    public void Matches_BodyJsonExistsOperator_ReturnsTrue_ForNullLeaf()
    {
        // A present-but-null value still counts as "has" - mirrors ClickHouse's JSONHas
        // (confirmed against a live instance; see BodyJsonFilter's own remarks).
        var logEvent = MinimalLogEvent() with { Body = """{"user":null}""" };
        var filter = new LogFilter { BodyJsonFilters = [new BodyJsonFilter { Path = "user", Value = "", Operator = BodyJsonFilterOperator.Exists }] };

        Assert.True(LogFilterMatcher.Matches(logEvent, filter));
    }

    [Theory]
    [InlineData("user.id", false)]
    [InlineData("user.missing", true)]
    public void Matches_BodyJsonAbsentOperator_ChecksPathAbsence_IgnoringValue(string path, bool expected)
    {
        var logEvent = MinimalLogEvent() with { Body = """{"user":{"id":"42"}}""" };
        var filter = new LogFilter { BodyJsonFilters = [new BodyJsonFilter { Path = path, Value = "", Operator = BodyJsonFilterOperator.Absent }] };

        Assert.Equal(expected, LogFilterMatcher.Matches(logEvent, filter));
    }

    [Theory]
    [InlineData("^[0-9]+$", true)] // matches
    [InlineData("^[a-z]+$", false)] // doesn't match
    public void Matches_BodyJsonRegexOperator_WhenPathIsPresent(string pattern, bool expected)
    {
        var logEvent = MinimalLogEvent() with { Body = """{"user":{"id":"42"}}""" };
        var filter = new LogFilter { BodyJsonFilters = [new BodyJsonFilter { Path = "user.id", Value = pattern, Operator = BodyJsonFilterOperator.Regex }] };

        Assert.Equal(expected, LogFilterMatcher.Matches(logEvent, filter));
    }

    [Fact]
    public void Matches_BodyJsonRegexOperator_ReturnsFalse_OnInvalidPattern()
    {
        var logEvent = MinimalLogEvent() with { Body = """{"user":{"id":"42"}}""" };
        var filter = new LogFilter { BodyJsonFilters = [new BodyJsonFilter { Path = "user.id", Value = "(unclosed", Operator = BodyJsonFilterOperator.Regex }] };

        Assert.False(LogFilterMatcher.Matches(logEvent, filter));
    }

    [Theory]
    [InlineData(new[] { "failed", "errored" }, true)]
    [InlineData(new[] { "ok" }, false)]
    public void Matches_BodyJsonInOperator_WhenPathIsPresent(string[] values, bool expected)
    {
        var logEvent = MinimalLogEvent() with { Body = """{"status":"failed"}""" };
        var filter = new LogFilter { BodyJsonFilters = [new BodyJsonFilter { Path = "status", Value = "", Operator = BodyJsonFilterOperator.In, Values = values }] };

        Assert.Equal(expected, LogFilterMatcher.Matches(logEvent, filter));
    }

    [Theory]
    [InlineData(new[] { "failed", "errored" }, false)]
    [InlineData(new[] { "ok" }, true)]
    public void Matches_BodyJsonNotInOperator_WhenPathIsPresent(string[] values, bool expected)
    {
        var logEvent = MinimalLogEvent() with { Body = """{"status":"failed"}""" };
        var filter = new LogFilter { BodyJsonFilters = [new BodyJsonFilter { Path = "status", Value = "", Operator = BodyJsonFilterOperator.NotIn, Values = values }] };

        Assert.Equal(expected, LogFilterMatcher.Matches(logEvent, filter));
    }

    [Theory]
    [InlineData("""{"count":42}""", "42")] // number leaf stringifies
    [InlineData("""{"ok":true}""", "true")] // bool leaf stringifies
    public void Matches_BodyJsonFilter_StringifiesNonStringLeaves(string body, string expectedValue)
    {
        var logEvent = MinimalLogEvent() with { Body = body };
        var key = body.Contains("count") ? "count" : "ok";
        var filter = new LogFilter { BodyJsonFilters = [new BodyJsonFilter { Path = key, Value = expectedValue }] };

        Assert.True(LogFilterMatcher.Matches(logEvent, filter));
    }

    [Fact]
    public void Matches_MultipleBodyJsonFilters_AreAnded()
    {
        var logEvent = MinimalLogEvent() with { Body = """{"user":{"id":"42"},"status":"failed"}""" };
        var filter = new LogFilter
        {
            BodyJsonFilters =
            [
                new BodyJsonFilter { Path = "user.id", Value = "42" },
                new BodyJsonFilter { Path = "status", Value = "ok" }, // doesn't match
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
