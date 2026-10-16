using Flare.Api.Loki;
using Xunit;

namespace Flare.Api.Tests.Loki;

public class LokiTemplateTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 10, 16, 12, 0, 0, 123, TimeSpan.Zero);

    private static string Render(string template, params (string Name, string Value)[] labels) =>
        LokiTemplate.Parse(template).Render(name => labels.Where(l => l.Name == name).Select(l => l.Value).FirstOrDefault(), "the original line", Timestamp);

    [Fact]
    public void Render_SubstitutesLabelsAndKeepsLiteralText()
    {
        Assert.Equal("GET /x -> 500", Render("{{.method}} {{ .path }} -> {{.status}}", ("method", "GET"), ("path", "/x"), ("status", "500")));
    }

    [Fact]
    public void Render_MissingLabelIsEmpty()
    {
        Assert.Equal("[]", Render("[{{.nothing}}]"));
    }

    [Fact]
    public void Render_LineAndTimestampFunctions()
    {
        Assert.Equal("the original line", Render("{{ __line__ }}"));
        Assert.Equal("2026-10-16T12:00:00.1230000Z", Render("{{ __timestamp__ }}"));
    }

    [Fact]
    public void Render_PipelinePassesTheResultAsTheLastArgument()
    {
        Assert.Equal("HELLO", Render("{{ .m | upper }}", ("m", "hello")));
        Assert.Equal("Hello World", Render("{{ .m | lower | title }}", ("m", "HELLO WORLD")));
        Assert.Equal("hel", Render("{{ .m | trunc 3 }}", ("m", "hello")));
        Assert.Equal("llo", Render("{{ trunc -3 .m }}", ("m", "hello")));
        Assert.Equal("fallback", Render("{{ .m | default \"fallback\" }}", ("m", "")));
        Assert.Equal("kept", Render("{{ .m | default \"fallback\" }}", ("m", "kept")));
    }

    [Fact]
    public void Render_StringFunctions()
    {
        Assert.Equal("a-b-c", Render("""{{ replace "_" "-" .m }}""", ("m", "a_b_c")));
        Assert.Equal("x", Render("""{{ .m | trim }}""", ("m", "  x  ")));
        Assert.Equal("ok", Render("""{{ .m | trimPrefix "ERR:" | trimSuffix "!" }}""", ("m", "ERR:ok!")));
        Assert.Equal("core", Render("""{{ trimAll "*-" .m }}""", ("m", "*-core-*")));
    }

    [Fact]
    public void Render_TrimMarkersRemoveAdjacentWhitespace()
    {
        Assert.Equal("a=1,b=2", Render("a={{ .a -}}  ,\n  b={{- .b }}", ("a", "1"), ("b", "2")).Replace("\n", string.Empty).Replace(" ", string.Empty));
        Assert.Equal("x1", Render("x  {{- .a }}", ("a", "1")));
    }

    [Fact]
    public void Render_LiteralOperands()
    {
        Assert.Equal("hello 42", Render("""{{ "hello" }} {{ 42 }}"""));
    }

    [Fact]
    public void Labels_ListsEveryLabelTheTemplateReads()
    {
        var template = LokiTemplate.Parse("{{.a}} {{ .b | upper }} {{ replace \"x\" .c .d }}");

        Assert.Equal(["a", "b", "c", "d"], template.Labels.Order());
    }

    [Theory]
    [InlineData("{{ .a ", "unclosed")]
    [InlineData("{{ }}", "empty action")]
    [InlineData("{{ if .a }}x{{ end }}", "'if' is not supported")]
    [InlineData("{{ range .a }}x{{ end }}", "'range' is not supported")]
    [InlineData("{{ nosuch .a }}", "nosuch")]
    [InlineData("{{ upper }}", "takes 1 argument")]
    [InlineData("{{ upper .a .b }}", "takes 1 argument")]
    [InlineData("{{ replace \"a\" .b }}", "takes 3 arguments")]
    [InlineData("{{ .a | replace \"a\" }}", "takes 3 arguments")]
    [InlineData("{{ .a | .b }}", "give a value")]
    [InlineData("{{ upper bare }}", "not a value")]
    [InlineData("{{ bare }}", "\"bare\" is not supported")]
    [InlineData("{{ \"open }}", "unterminated")]
    public void Parse_RejectsMalformedAndUnsupportedTemplates(string template, string mention)
    {
        var ex = Assert.Throws<LokiException>(() => LokiTemplate.Parse(template).Render(_ => "v", string.Empty, Timestamp));

        Assert.Contains(mention, ex.Message);
    }

    [Fact]
    public void Truncate_RejectsNonNumbers()
    {
        Assert.Throws<LokiException>(() => Render("{{ trunc \"x\" .m }}", ("m", "hello")));
    }
}
