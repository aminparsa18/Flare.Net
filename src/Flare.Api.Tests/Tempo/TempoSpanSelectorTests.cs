using Flare.Api.Query;
using Flare.Api.Tempo;
using Xunit;

namespace Flare.Api.Tests.Tempo;

public class TempoSpanSelectorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly IReadOnlyDictionary<string, string> None = new Dictionary<string, string>();

    private static SpanTreeRow Row(string id, string? parent, string service, string name, uint mask, int seq, string status = "STATUS_CODE_UNSET", byte kind = 2,
        Dictionary<string, string>? span = null, Dictionary<string, string>? resource = null, params Dictionary<string, string>[] events) =>
        new("t1", id, parent ?? "", name, service, T0.AddMilliseconds(seq), 1_000_000, kind, status, mask, span ?? None, resource ?? None, events);

    private static TempoSearchPlan Plan(string traceQl) => TempoSearchPlanner.Plan(TraceQlParser.Parse(traceQl));

    // root(1..12) -> [cart(2..5) -> db(3..4)], [pay(6..9) -> charge(7..8)], render(10..11)
    private static SpanTreeRow[] Tree(uint mask = 0) =>
    [
        Row("root", null, "web", "GET /checkout", mask, 0),
        Row("cart", "root", "cart", "POST /cart", mask, 1),
        Row("db", "cart", "cart", "SELECT", mask, 2, kind: 3),
        Row("pay", "root", "pay", "POST /pay", mask, 3),
        Row("charge", "pay", "pay", "charge", mask, 4, kind: 3),
        Row("render", "root", "web", "render", mask, 5, kind: 1),
    ];

    [Fact]
    public void NestedSets_NumberTheTreeDepthFirst()
    {
        var rows = Tree();
        var parent = TempoSpanSelector.ParentIndexes(rows, 0, rows.Length);

        var (left, right, parentLeft) = TempoSpanSelector.NestedSets(parent);

        Assert.Equal([1, 2, 3, 6, 7, 10], left);
        Assert.Equal([12, 5, 4, 9, 8, 11], right);
        Assert.Equal([-1, 1, 2, 1, 6, 1], parentLeft);
    }

    [Fact]
    public void NestedSets_NumberAnOrphanAsAnotherRoot()
    {
        SpanTreeRow[] rows = [Row("a", null, "s", "a", 0, 0), Row("b", "missing", "s", "b", 0, 1), Row("c", "b", "s", "c", 0, 2)];

        var (left, right, parentLeft) = TempoSpanSelector.NestedSets(TempoSpanSelector.ParentIndexes(rows, 0, 3));

        Assert.Equal([1, 3, 4], left);
        Assert.Equal([2, 6, 5], right);
        Assert.Equal([-1, -1, 3], parentLeft);
    }

    [Fact]
    public void NestedSets_SurviveAParentCycleAndADeepChain()
    {
        // a <-> b never reaches a root; they are still numbered, once each.
        SpanTreeRow[] cycle = [Row("a", "b", "s", "a", 0, 0), Row("b", "a", "s", "b", 0, 1)];
        var (left, right, _) = TempoSpanSelector.NestedSets(TempoSpanSelector.ParentIndexes(cycle, 0, 2));
        Assert.Equal([1, 2, 3, 4], left.Concat(right).Order());

        var chain = Enumerable.Range(0, 50_000).Select(i => Row($"s{i}", i == 0 ? null : $"s{i - 1}", "s", "n", 0, i)).ToArray();
        var (l, r, _) = TempoSpanSelector.NestedSets(TempoSpanSelector.ParentIndexes(chain, 0, chain.Length));
        Assert.Equal(1, l[0]);
        Assert.Equal(100_000, r[0]);
        Assert.Equal(50_000, l[^1]);
    }

    [Fact]
    public void DrilldownStructureQuery_ListsRootsAndServerSpans_WithTheTreeNumbers()
    {
        // A = root, B = server, C = root: roots always, plus the server spans below a root. Bits: A=1, B=2, C=4.
        var plan = Plan("({ nestedSetParent < 0 && true } &>> { kind = server }) || ({ nestedSetParent < 0 && true }) | select(status, resource.service.name, name, nestedSetParent, nestedSetLeft, nestedSetRight)");
        SpanTreeRow[] rows =
        [
            Row("root", null, "web", "GET /checkout", 1 | 2 | 4, 0, status: "STATUS_CODE_ERROR"),
            Row("cart", "root", "cart", "POST /cart", 2, 1),
            Row("db", "cart", "cart", "SELECT", 0, 2, kind: 3),
            Row("pay", "root", "pay", "POST /pay", 2, 3),
            Row("render", "root", "web", "render", 0, 4, kind: 1),
        ];

        var trace = Assert.Single(TempoSpanSelector.Select(rows, plan, 20));

        Assert.Equal(3, trace.Matched);
        Assert.Equal(["root", "cart", "pay"], trace.Spans.Select(s => s.SpanId));
        var root = trace.Spans[0].Attributes;
        Assert.Equal(new SelectedAttribute("status", "error"), root.Single(a => a.Key == "status"));
        Assert.Equal(new SelectedAttribute("nestedSetParent", null, -1), root.Single(a => a.Key == "nestedSetParent"));
        Assert.Equal(new SelectedAttribute("nestedSetLeft", null, 1), root.Single(a => a.Key == "nestedSetLeft"));
        Assert.Equal(new SelectedAttribute("nestedSetRight", null, 10), root.Single(a => a.Key == "nestedSetRight"));
        var cart = trace.Spans[1].Attributes;
        Assert.Equal(new SelectedAttribute("service.name", "cart"), cart[0]);
        Assert.Equal(new SelectedAttribute("nestedSetParent", null, 1), cart.Single(a => a.Key == "nestedSetParent"));
        Assert.Equal(new SelectedAttribute("nestedSetLeft", null, 2), cart.Single(a => a.Key == "nestedSetLeft"));
        Assert.Equal(new SelectedAttribute("nestedSetRight", null, 5), cart.Single(a => a.Key == "nestedSetRight"));
    }

    [Fact]
    public void UnionDescendant_ListsOnlyTheSpansTakingPart()
    {
        // { A } &>> { B } alone: B spans with an A above, and A spans with a B below. Bits: A=1, B=2.
        var plan = Plan("{ name = \"POST /pay\" } &>> { kind = client }");
        SpanTreeRow[] rows =
        [
            Row("root", null, "web", "GET /checkout", 0, 0),
            Row("cart", "root", "cart", "POST /cart", 0, 1),
            Row("db", "cart", "cart", "SELECT", 2, 2, kind: 3),        // client, but no A above: out
            Row("pay", "root", "pay", "POST /pay", 1, 3),             // A with a B below: in
            Row("charge", "pay", "pay", "charge", 2, 4, kind: 3),     // B under A: in
            Row("pay2", "root", "pay", "POST /pay", 1, 5),            // A without a B below: out
        ];

        var trace = Assert.Single(TempoSpanSelector.Select(rows, plan, 20));

        Assert.Equal(["pay", "charge"], trace.Spans.Select(s => s.SpanId));
        Assert.Equal(2, trace.Matched);
    }

    [Fact]
    public void UnionDescendant_AnAncestorOfBothKindsIsListedOnce()
    {
        var plan = Plan("{ name = \"a\" } &>> { name = \"b\" }");
        SpanTreeRow[] rows =
        [
            Row("a1", null, "s", "a", 1, 0),
            Row("a2", "a1", "s", "a", 1 | 0, 1),
            Row("b1", "a2", "s", "b", 2, 2),
        ];

        var trace = Assert.Single(TempoSpanSelector.Select(rows, plan, 20));

        Assert.Equal(["a1", "a2", "b1"], trace.Spans.Select(s => s.SpanId));
    }

    [Fact]
    public void ASpanMatchingAConditionOutsideAnyUnion_IsListedWhateverItsPosition()
    {
        var plan = Plan("({ name = \"a\" } &>> { name = \"b\" }) || { status = error }");
        SpanTreeRow[] rows =
        [
            Row("x", null, "s", "x", 4, 0, status: "STATUS_CODE_ERROR"),
            Row("a", null, "s", "a", 1, 1),
        ];

        var trace = Assert.Single(TempoSpanSelector.Select(rows, plan, 20));

        Assert.Equal(["x"], trace.Spans.Select(s => s.SpanId));
    }

    [Fact]
    public void SpansPerSpanSet_KeepsTheEarliestInTheOrderOfTheWalk_AndMatchedCountsAll()
    {
        var plan = Plan("{ name != \"zzz\" } | select(nestedSetLeft)");
        var rows = Tree(1);

        var trace = Assert.Single(TempoSpanSelector.Select(rows, plan, 3));

        Assert.Equal(6, trace.Matched);
        Assert.Equal(["root", "cart", "db"], trace.Spans.Select(s => s.SpanId));
    }

    [Fact]
    public void Attributes_AreSelectedFieldsThenWhatTheQueryComparedOn_KeyedWithoutScope()
    {
        var plan = Plan("{ span.http.route = \"/pay\" } | select(resource.k8s.pod.name, span.http.method, status, kind, name, event.exception.message, event.exception.type)");
        var row = Row("s", null, "pay", "POST /pay", 1, 0, status: "STATUS_CODE_ERROR", kind: 2,
            span: new() { ["http.method"] = "POST", ["http.route"] = "/pay", ["unset"] = "" },
            resource: new() { ["k8s.pod.name"] = "pay-1", ["service.name"] = "pay" },
            events: [new(), new() { ["exception.type"] = "Boom", ["exception.message"] = "bad" }, new() { ["exception.type"] = "Later" }]);

        var span = Assert.Single(Assert.Single(TempoSpanSelector.Select([row], plan, 3)).Spans);

        Assert.Equal(
            [
                new SelectedAttribute("service.name", "pay"),
                new SelectedAttribute("k8s.pod.name", "pay-1"),
                new SelectedAttribute("http.method", "POST"),
                new SelectedAttribute("status", "error"),
                new SelectedAttribute("kind", "server"),
                new SelectedAttribute("name", "POST /pay"),
                new SelectedAttribute("exception.message", "bad"),
                new SelectedAttribute("exception.type", "Boom"),
                new SelectedAttribute("http.route", "/pay"),
            ],
            span.Attributes);
    }

    [Fact]
    public void ASelectedAttributeTheSpanLacks_IsLeftOut()
    {
        var plan = Plan("{ status = error } | select(span.missing, event.exception.message)");

        var span = Assert.Single(Assert.Single(TempoSpanSelector.Select([Row("s", null, "pay", "x", 1, 0)], plan, 3)).Spans);

        Assert.Equal([new SelectedAttribute("service.name", "pay")], span.Attributes);
    }

    [Fact]
    public void Traces_AreSeparatedByTraceId()
    {
        var plan = Plan("{ status = error } | select(status)");
        SpanTreeRow[] rows =
        [
            Row("a", null, "s", "a", 1, 0) with { TraceId = "t1" },
            Row("b", null, "s", "b", 1, 1) with { TraceId = "t2" },
            Row("c", null, "s", "c", 1, 2) with { TraceId = "t2" },
        ];

        var traces = TempoSpanSelector.Select(rows, plan, 20);

        Assert.Equal(["t1", "t2"], traces.Select(t => t.TraceId));
        Assert.Equal([1, 2], traces.Select(t => t.Matched));
    }
}
