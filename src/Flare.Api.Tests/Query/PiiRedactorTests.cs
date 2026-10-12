using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class PiiRedactorTests
{
    [Theory]
    [InlineData("mail bob@example.com now", "mail [REDACTED:email] now")]
    [InlineData("card 4111 1111 1111 1111", "card [REDACTED:card]")]
    [InlineData("iban GB82 WEST 1234 5698 7654 32 end", "iban [REDACTED:iban] end")]
    [InlineData("order 4111 1111 1111 1112", "order 4111 1111 1111 1112")]
    public void Redact_matches_ingest_behaviour(string input, string expected) =>
        Assert.Equal(expected, PiiRedactor.Redact(input, PiiDetectors.All));
}

public class PiiSpanFieldsTests
{
    [Fact]
    public void Enumerate_covers_status_attributes_events_and_links_but_not_resource_attributes()
    {
        var span = new Flare.Api.Model.SpanDto
        {
            TraceId = "t", SpanId = "s", ParentSpanId = "", TraceState = "", Name = "GET /", Kind = 2,
            StartTime = DateTimeOffset.UnixEpoch, EndTime = DateTimeOffset.UnixEpoch, IngestedAt = DateTimeOffset.UnixEpoch,
            DurationNano = 0, StatusCode = "STATUS_CODE_ERROR", StatusMessage = "bad bob@example.com",
            ServiceName = "svc", ResourceSchemaUrl = "", ResourceAttributes = new Dictionary<string, string> { ["host"] = "a@b.co" },
            ScopeSchemaUrl = "", ScopeName = "", ScopeVersion = "", ScopeAttributes = new Dictionary<string, string>(),
            SpanAttributes = new Dictionary<string, string> { ["user"] = "x" },
            Events =
            [
                new Flare.Api.Model.SpanEventDto { Timestamp = DateTimeOffset.UnixEpoch, Name = "exception", Attributes = new Dictionary<string, string> { ["exception.message"] = "m" } },
            ],
            Links =
            [
                new Flare.Api.Model.SpanLinkDto { TraceId = "t2", SpanId = "s2", TraceState = "", Attributes = new Dictionary<string, string> { ["k"] = "v" } },
            ],
        };

        var fields = PiiSpanFields.Enumerate(span).Select(f => f.Field).ToArray();

        Assert.Equal(["status_message", "attr:user", "event:exception:exception.message", "link:k"], fields);
    }
}
