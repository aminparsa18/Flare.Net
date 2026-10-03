using System.Text.Json.Nodes;
using static ExampleApp.Seeder.SeedContext;

namespace ExampleApp.Seeder.Scenarios;

/// <summary>
/// RED metrics and logs for the four checkout services, a "Checkout overview" custom dashboard
/// (value/pie/threshold/stacked-bar/logs panels, two rows, a service variable), and a saved Logs
/// view. For the Metrics explorer, custom dashboards and saved views docs.
/// </summary>
public sealed class OverviewScenario : Scenario
{
    public override string Name => "overview";

    public override string Description => "RED metrics + logs for 4 services, the Checkout overview dashboard, a saved Logs view";

    private static readonly Localized DashboardName = new("Checkout overview", "Обзор оформления заказа", "结账概览");
    private static readonly Localized ViewName = new("Checkout warnings and errors", "Предупреждения и ошибки оформления", "结账警告与错误");

    public override IEnumerable<(FlareApiClient.Kind, Localized)> Objects =>
        [(FlareApiClient.Kind.Dashboards, DashboardName), (FlareApiClient.Kind.Views, ViewName)];

    // base requests/s, error share, base memory MB, p50 latency s
    private static readonly (string Service, double Rps, double ErrorShare, double MemoryMb, double P50)[] Services =
    [
        ("storefront", 42, 0.01, 310, 0.045),
        ("checkout-api", 18, 0.03, 420, 0.12),
        ("payment-service", 9, 0.08, 260, 0.35),
        ("order-service", 7, 0.02, 190, 0.09),
    ];

    private static readonly double[] Bounds = [0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5];

    private static readonly Dictionary<string, string> Routes = new()
    {
        ["storefront"] = "GET /cart",
        ["checkout-api"] = "POST /checkout",
        ["payment-service"] = "POST /payments/charge",
        ["order-service"] = "POST /orders/confirm",
    };

    public override void Generate(SeedContext c)
    {
        var ticks = c.Ticks(30).ToArray();
        // A traffic spike 60-65% of the way through the window, on checkout and payments.
        var spikeFrom = (int)(ticks.Length * 0.6);
        var spikeTo = (int)(ticks.Length * 0.65);

        foreach (var (service, rps, errorShare, memoryMb, p50) in Services)
        {
            var resource = c.Batch.Service(service, ("deployment.environment", "production"));
            double okTotal = 0, errorTotal = 0, sum = 0;
            long count = 0;
            var buckets = new long[Bounds.Length + 1];
            List<JsonObject> requests = [], memory = [], cpu = [], durations = [];

            for (var i = 0; i < ticks.Length; i++)
            {
                var t = ticks[i];
                var wave = 1 + 0.35 * Math.Sin(i / 18.0) + (c.Rng.NextDouble() * 0.16 - 0.08);
                var spike = i >= spikeFrom && i <= spikeTo && service is "checkout-api" or "payment-service" ? 2.2 : 1;
                var n = rps * 30 * wave * spike;
                var errors = n * errorShare * (spike > 1 ? 3 : 1);
                okTotal += n - errors;
                errorTotal += errors;
                requests.Add(Point(t, Math.Round(okTotal, 1), Otlp.Attrs(("http.response.status_code", "200")), c.StartNanos));
                requests.Add(Point(t, Math.Round(errorTotal, 1), Otlp.Attrs(("http.response.status_code", "500")), c.StartNanos));
                memory.Add(Point(t, (memoryMb + 40 * Math.Sin(i / 11.0) + i * 0.4 + (c.Rng.NextDouble() * 12 - 6)) * 1024 * 1024));
                cpu.Add(Point(t, Math.Clamp(0.22 * wave * spike + (c.Rng.NextDouble() * 0.06 - 0.03), 0.02, 0.97)));

                for (var k = 0; k < (int)(n / 10); k++)
                {
                    var d = c.LogNormal(p50 * (spike > 1 ? 1.6 : 1), 0.6);
                    buckets[Bucket(Bounds, d)]++;
                    sum += d;
                    count++;
                }

                durations.Add(HistogramPoint(c.StartNanos, t, Bounds, [.. buckets], count, sum));
            }

            var scope = "Microsoft.AspNetCore.Hosting";
            c.Batch.AddMetric(resource, scope, Sum("http.server.request.count", "{request}", "HTTP requests served", requests, monotonic: true));
            c.Batch.AddMetric(resource, scope, Gauge("process.memory.usage", "By", "Process resident memory", memory));
            c.Batch.AddMetric(resource, scope, Gauge("process.cpu.utilization", "1", "Process CPU utilization", cpu));
            c.Batch.AddMetric(resource, scope, Histogram("http.server.request.duration", "s", "HTTP server request duration", durations, temporality: 2));

            GenerateLogs(c, resource, service, rps, errorShare);
        }
    }

    private static void GenerateLogs(SeedContext c, string resource, string service, double rps, double errorShare)
    {
        var route = Routes[service];
        string[] debug = ["Resolved route {r}"];
        string[] info = ["Handled {r} in {ms} ms", "Cache hit for {r}", "Request completed with status 200"];
        string[] warn = ["Slow response from upstream for {r} ({ms} ms)", "Retrying {r} after timeout"];
        string[] error = ["Request {r} failed: upstream returned 502", "Unhandled exception while processing {r}"];
        var weights = new[] { 10, 70, 12 + errorShare * 100, 3 + errorShare * 150 };

        for (var i = 0; i < c.PerHour(rps * 9); i++)
        {
            var roll = c.Rng.NextDouble() * weights.Sum();
            var (severity, pool) = roll < weights[0] ? (5, debug)
                : roll < weights[0] + weights[1] ? (9, info)
                : roll < weights[0] + weights[1] + weights[2] ? (13, warn)
                : (17, error);
            var body = c.Pick(pool).Replace("{r}", route).Replace("{ms}", c.Rng.Next(8, 900).ToString());
            c.Log(resource, $"Shop.{service}", c.RandomTime(1), severity, body, Otlp.Attrs(("http.route", route.Split(' ')[1])));
        }
    }

    public override async Task CreateObjectsAsync(SeedContext c, CancellationToken ct)
    {
        var dashboard = new JsonObject
        {
            ["name"] = c.T(DashboardName),
            ["description"] = c.T(new("Traffic, latency and errors for the checkout flow", "Трафик, задержки и ошибки процесса оформления заказа", "结账流程的流量、延迟和错误")),
            ["layoutJson"] = Layout(c),
        };
        Console.WriteLine($"  dashboard {await c.Api.CreateAsync(FlareApiClient.Kind.Dashboards, dashboard, ct)}");

        var view = new JsonObject
        {
            ["name"] = c.T(ViewName),
            ["description"] = "",
            ["pageType"] = "Logs",
            ["state"] = new JsonObject
            {
                ["timeRangePreset"] = c.Preset,
                ["customRange"] = null,
                ["services"] = new JsonArray("checkout-api", "payment-service"),
                ["severityNumbers"] = new JsonArray(13, 17),
                ["search"] = "",
                ["attributeFilters"] = new JsonArray(),
                ["bodyJsonFilters"] = new JsonArray(),
                ["postProcessFunctions"] = new JsonArray(),
                ["timeShiftSeconds"] = null,
            },
        };
        Console.WriteLine($"  saved view {await c.Api.CreateAsync(FlareApiClient.Kind.Views, view, ct)}");
    }

    private static JsonObject Layout(SeedContext c)
    {
        JsonObject MetricQuery(string metric, string service, string type, string? groupBy = null) => new()
        {
            ["timeRangePreset"] = c.Preset,
            ["customRange"] = null,
            ["services"] = new JsonArray(),
            ["compareEnabled"] = false,
            ["groupByAttributeKey"] = groupBy,
            ["topN"] = 10,
            ["havingOperator"] = null,
            ["havingValue"] = null,
            ["mode"] = "single",
            ["selectedMetric"] = new JsonObject { ["metricName"] = metric, ["serviceName"] = service, ["type"] = type },
        };

        // panelType is an identifier the dashboard switches on - never translate it, only titles.
        JsonObject Panel(string id, Localized title, int x, int y, int w, int h, JsonObject query, string panelType = "Metrics", string? row = null, params (string Key, JsonNode? Value)[] extra)
        {
            var panel = new JsonObject
            {
                ["id"] = id,
                ["panelType"] = panelType,
                ["title"] = c.T(title),
                ["layout"] = new JsonObject { ["x"] = x, ["y"] = y, ["w"] = w, ["h"] = h },
                ["query"] = query,
                ["rowId"] = row,
            };
            foreach (var (key, value) in extra)
            {
                panel[key] = value;
            }

            return panel;
        }

        var logsQuery = new JsonObject
        {
            ["timeRangePreset"] = c.Preset,
            ["customRange"] = null,
            ["services"] = new JsonArray(),
            ["severityNumbers"] = new JsonArray(13, 17),
            ["search"] = "",
            ["attributeFilters"] = new JsonArray(),
            ["bodyJsonFilters"] = new JsonArray(),
            ["postProcessFunctions"] = new JsonArray(),
            ["timeShiftSeconds"] = null,
        };

        return new JsonObject
        {
            ["panels"] = new JsonArray(
                Panel("p1", new("Storefront requests", "Запросы витрины", "店面请求数"), 0, 0, 4, 3,
                    MetricQuery("http.server.request.count", "storefront", "Sum"), extra: ("visualization", "value")),
                Panel("p2", new("Checkout responses by status", "Ответы checkout по статусу", "按状态划分的结账响应"), 4, 0, 4, 3,
                    MetricQuery("http.server.request.count", "checkout-api", "Sum", "http.response.status_code"), extra: ("visualization", "pie")),
                Panel("p3", new("Checkout memory", "Память checkout", "结账服务内存"), 8, 0, 4, 3,
                    MetricQuery("process.memory.usage", "checkout-api", "Gauge"), extra: [("visualization", "value"), ("reducer", "last")]),
                Panel("p4", new("Checkout request rate by status", "Частота запросов checkout по статусу", "按状态划分的结账请求速率"), 0, 0, 6, 4,
                    MetricQuery("http.server.request.count", "checkout-api", "Sum", "http.response.status_code"), row: "r1",
                    extra: ("thresholds", new JsonArray(new JsonObject { ["id"] = "t1", ["operator"] = ">", ["value"] = 15, ["color"] = "red" }))),
                Panel("p5", new("Payment latency", "Задержка платежей", "支付延迟"), 6, 0, 6, 4,
                    MetricQuery("http.server.request.duration", "payment-service", "Histogram"), row: "r1"),
                Panel("p6", new("Storefront requests by status", "Запросы витрины по статусу", "按状态划分的店面请求"), 0, 4, 12, 4,
                    MetricQuery("http.server.request.count", "storefront", "Sum", "http.response.status_code"), row: "r1", extra: [("visualization", "bar"), ("stacking", "normal")]),
                Panel("p7", new("Warnings and errors", "Предупреждения и ошибки", "警告与错误"), 0, 0, 12, 5, logsQuery, panelType: "Logs", row: "r2")),
            ["rows"] = new JsonArray(
                new JsonObject { ["id"] = "r1", ["title"] = c.T(new("Traffic", "Трафик", "流量")) },
                new JsonObject { ["id"] = "r2", ["title"] = c.T(new("Logs", "Логи", "日志")) }),
            ["variables"] = new JsonArray(new JsonObject
            {
                ["id"] = "v1",
                ["name"] = "service",
                ["description"] = c.T(new("Limit every panel to one service", "Ограничить все панели одним сервисом", "将所有面板限定为一个服务")),
                ["target"] = "Service",
                ["sourceKind"] = "Query",
                ["defaultValue"] = null,
            }),
        };
    }
}
