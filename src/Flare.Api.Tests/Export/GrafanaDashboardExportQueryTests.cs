using Flare.Api.Loki;
using Flare.Api.Prometheus;
using Flare.Api.Tempo;
using Xunit;

namespace Flare.Api.Tests.Export;

/// <summary>
/// The dashboard's "Export as Grafana JSON" (ADR-0253, <c>grafana-export.ts</c>) writes PromQL, LogQL and TraceQL
/// that Grafana sends to Flare's own compatible APIs. These are the shapes it produces, with Grafana's own
/// substitutions (<c>$__interval</c>, <c>$var</c>) already applied, so a parser change that would reject one
/// fails here rather than in a user's Grafana.
/// </summary>
public class GrafanaDashboardExportQueryTests
{
    [Theory]
    [InlineData("""histogram_quantile(0.95, sum by (le, service_name) (rate(http_server_request_duration_seconds_bucket{service_name="shop",service_name=~"(shop|api)"}[1m])))""")]
    [InlineData("""topk(20, sum by (service_name, http_route) (increase(http_server_requests_total{service_name=~".+"}[1m])))""")]
    [InlineData("""avg by (service_name) (process_memory_bytes{service_name="shop"})""")]
    [InlineData("""avg by (service_name) (process_memory_bytes{})""")]
    [InlineData("""sum by (le) (increase(http_server_request_duration_seconds_bucket{service_name="shop"}[1m]))""")]
    [InlineData("""sum by (service_name) (increase(http_server_request_duration_seconds_sum{service_name="shop"}[1m])) / sum by (service_name) (increase(http_server_request_duration_seconds_count{service_name="shop"}[1m]))""")]
    [InlineData("""(((sum by (service_name) (increase(a_total{service_name="shop"}[1m]))) / (sum by (service_name) (increase(b_total{service_name="shop"}[1m])))) * 100)""")]
    [InlineData("""sqrt((avg by (service_name) (process_memory_bytes{service_name="shop"})))""")]
    public void PromQl_ParsesExportedQuery(string query) => Assert.NotNull(PromQlParser.Parse(query));

    [Theory]
    [InlineData("""sum by (detected_level) (count_over_time({service_name=~"shop|api", service_name=~"(shop|api)", detected_level=~"error|fatal", k8s_pod_name=~"a|b\\.c"} |~ "(?i)time\"out" | json | user_id="42" | http_status_code >= 500 [1m]))""")]
    [InlineData("""sum(count_over_time({service_name=~".+"} [1m]))""")]
    [InlineData("""sum by (http_route) (count_over_time({service_name=~".+", scope_name=~"Microsoft\\.AspNetCore.*", trace_id="abc", http_route=~".+", env=""} [1m]))""")]
    public void LogQl_ParsesExportedQuery(string query) => Assert.NotNull(LokiQlParser.Parse(query));

    [Theory]
    [InlineData("""{ true }""")]
    [InlineData("""{ resource.service.name =~ "(shop|api)" && status = error && kind = server && span.http.route != nil && resource.deployment.environment = "prod" }""")]
    [InlineData("""{ (status = error || status = ok) && (kind = server || kind = client) && name =~ "GET /a|GET /b" && span.http.status_code >= 500 && span.x = nil }""")]
    public void TraceQl_ParsesExportedQuery(string query) => Assert.NotNull(TraceQlParser.Parse(query));
}
