using System.Text.Json.Nodes;
using static ExampleApp.Seeder.SeedContext;

namespace ExampleApp.Seeder.Scenarios;

/// <summary>
/// Two classic cardinality mistakes next to a few well-behaved metrics, for the Metrics catalog's
/// high-cardinality docs: a counter tagged with <c>user.id</c> (~12,500 series) and a request
/// histogram tagged with the raw <c>url.path</c> instead of the route (~1,600 series). The
/// well-behaved ones are deliberately not the ones <c>overview</c> sends, so the two don't
/// double up.
/// </summary>
public sealed class CardinalityScenario : Scenario
{
    public override string Name => "cardinality";

    public override string Description => "A user.id-tagged counter and a raw-url.path histogram, plus normal metrics";

    private static readonly double[] Bounds = [0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5];

    public override void Generate(SeedContext c)
    {
        var ticks = c.Ticks(30).ToArray();
        foreach (var service in (string[])["storefront", "checkout-api", "payment-service", "order-service"])
        {
            var resource = c.Batch.Service(service, ("deployment.environment", "production"));
            var connections = new List<JsonObject>();
            var collections = new List<JsonObject>();
            var gen0 = 0.0;
            var gen2 = 0.0;
            for (var i = 0; i < ticks.Length; i++)
            {
                foreach (var (state, baseline) in (ReadOnlySpan<(string, double)>)[("idle", 12), ("used", 6)])
                {
                    connections.Add(Point(ticks[i], Math.Max(0, baseline + 3 * Math.Sin(i / 9.0) + c.Rng.NextDouble() * 4 - 2),
                        Otlp.Attrs(("db.client.connections.state", state), ("db.client.connections.pool.name", "main"))));
                }

                gen0 += c.Rng.Next(4, 12);
                gen2 += c.Rng.NextDouble() < 0.1 ? 1 : 0;
                collections.Add(Point(ticks[i], gen0, Otlp.Attrs(("gc.heap.generation", "gen0")), c.StartNanos));
                collections.Add(Point(ticks[i], gen2, Otlp.Attrs(("gc.heap.generation", "gen2")), c.StartNanos));
            }

            c.Batch.AddMetric(resource, "Npgsql", Gauge("db.client.connections.usage", "{connection}", "Open database connections", connections));
            c.Batch.AddMetric(resource, "System.Runtime", Sum("dotnet.gc.collections", "{collection}", "Garbage collections since the process started", collections, monotonic: true));
        }

        // Raw URL paths recorded instead of the route template.
        var storefront = c.Batch.Service("storefront", ("deployment.environment", "production"));
        var paths = Enumerable.Range(0, 1600).Select(_ => $"/products/{c.Rng.Next(10000, 99999)}").ToArray();
        foreach (var chunk in paths.Chunk(400))
        {
            var points = new List<JsonObject>();
            foreach (var path in chunk)
            {
                for (var k = 0; k < c.Rng.Next(1, 4); k++)
                {
                    var t = c.RandomTime(30);
                    var buckets = new long[Bounds.Length + 1];
                    var n = c.Rng.Next(1, 7);
                    var sum = 0.0;
                    for (var j = 0; j < n; j++)
                    {
                        var d = c.LogNormal(0.08, 0.5);
                        buckets[Bucket(Bounds, d)]++;
                        sum += d;
                    }

                    points.Add(HistogramPoint(t - 30 * Otlp.NanosPerSecond, t, Bounds, buckets, n, sum,
                        Otlp.Attrs(("http.request.method", "GET"), ("url.path", path), ("http.response.status_code", "200"))));
                }
            }

            c.Batch.AddMetric(storefront, "Microsoft.AspNetCore.Hosting",
                Histogram("http.server.request.duration", "s", "Duration of HTTP server requests", points, temporality: 1));
        }

        // A user id recorded as an attribute.
        var checkout = c.Batch.Service("checkout-api", ("deployment.environment", "production"));
        var users = Enumerable.Range(0, 12500).Select(_ => $"u_{c.Rng.NextInt64(1L << 40):x10}").ToArray();
        foreach (var chunk in users.Chunk(2500))
        {
            var points = new List<JsonObject>();
            foreach (var user in chunk)
            {
                var value = 0;
                var currency = c.Pick<string>(["USD", "EUR", "GBP"]);
                for (var k = 0; k < c.Rng.Next(1, 4); k++)
                {
                    value += c.Rng.Next(1, 5);
                    points.Add(Point(c.RandomTime(30), value, Otlp.Attrs(("user.id", user), ("cart.currency", currency)), c.StartNanos));
                }
            }

            c.Batch.AddMetric(checkout, "ExampleApp.Shop", Sum("checkout.cart.items_added", "{item}", "Items added to a shopping cart", points, monotonic: true));
        }
    }
}

/// <summary>
/// Kafka publish/process spans (partitions, consumer groups) plus the collector metric behind
/// the Backlog column - <c>kafka.consumer_group.lag</c>, with one group falling behind. For the
/// Message queues docs. Kafka only, same as the live demo.
/// </summary>
public sealed class MessagingScenario : Scenario
{
    public override string Name => "messaging";

    public override string Description => "Kafka spans and consumer lag (one group falling behind)";

    public override void Generate(SeedContext c)
    {
        const string KafkaScope = "OpenTelemetry.Instrumentation.ConfluentKafka";
        (string Topic, string Producer, (string Service, string Group, double P50, double Errors)[] Consumers, double PerHour)[] kafka =
        [
            ("orders.created", "checkout-api", [("order-service", "order-processor", 18, 0.004), ("notification-service", "order-notifier", 9, 0)], 1550),
            ("payments.completed", "payment-service", [("order-service", "payment-reconciler", 42, 0.02), ("ledger-service", "ledger-writer", 65, 0)], 930),
            ("inventory.reserved", "inventory-service", [("order-service", "inventory-sync", 12, 0)], 720),
            ("clickstream.events", "storefront", [("analytics-ingest", "clickstream-loader", 4, 0)], 2700),
        ];
        foreach (var (topic, producer, consumers, perHour) in kafka)
        {
            for (var n = 0; n < c.PerHour(perHour); n++)
            {
                var traceId = Otlp.TraceId(c.Ids);
                var t = c.RandomTime();
                var partition = c.Rng.Next(0, 6).ToString();
                var publish = c.Span(c.Batch.Service(producer), KafkaScope, traceId, null, $"{topic} publish", KindProducer, t, c.LogNormal(3, 0.5),
                    Otlp.Attrs(("messaging.system", "kafka"), ("messaging.destination.name", topic), ("messaging.operation.type", "publish")),
                    error: c.Rng.NextDouble() < 0.003 ? "Local: Message timed out" : null, okStatus: 0);
                foreach (var (service, group, p50, errors) in consumers)
                {
                    c.Span(c.Batch.Service(service), KafkaScope, traceId, publish, $"{topic} process", KindConsumer, t + c.Rng.Next(5, 400) * Otlp.NanosPerMs, c.LogNormal(p50, 0.45),
                        Otlp.Attrs(("messaging.system", "kafka"), ("messaging.destination.name", topic), ("messaging.operation.type", "process"),
                            ("messaging.consumer.group.name", group), ("messaging.destination.partition.id", partition)),
                        error: c.Rng.NextDouble() < errors ? "Local: Message timed out" : null, okStatus: 0);
                }
            }
        }

        // Backlog: what otelcol-contrib's kafkametrics receiver reports, every 30 s.
        var ticks = c.Ticks(30).ToArray();
        var lag = new List<JsonObject>();
        (string Topic, string Group, double Base, bool Growing)[] groups =
        [
            ("payments.completed", "payment-reconciler", 180, true), ("payments.completed", "ledger-writer", 12, false),
            ("orders.created", "order-processor", 4, false), ("orders.created", "order-notifier", 1, false),
            ("inventory.reserved", "inventory-sync", 0, false), ("clickstream.events", "clickstream-loader", 60, false),
        ];
        foreach (var (topic, group, baseline, growing) in groups)
        {
            for (var partition = 0; partition < 6; partition++)
            {
                for (var k = 0; k < ticks.Length; k++)
                {
                    var value = baseline * (1 + (double)k / ticks.Length * (growing ? 3 : 0)) * (c.Rng.NextDouble() * 0.8 + 0.6) / 6;
                    lag.Add(Point(ticks[k], (int)value, Otlp.Attrs(("group", group), ("topic", topic), ("partition", partition)), asInt: true));
                }
            }
        }

        c.Batch.AddMetric(c.Batch.Service("otelcol-kafka"), "kafkametricsreceiver", Gauge("kafka.consumer_group.lag", "1", "Current approximate lag of consumer group at partition of topic", lag));
    }
}

/// <summary>
/// hostmetrics-shaped CPU/memory/filesystem/load for seven hosts - one saturated, one stale
/// (stopped reporting 21 minutes ago), one macOS build agent - plus checkout-api logs carrying
/// <c>host.name</c> during the saturation, for the Hosts docs.
/// </summary>
public sealed class HostsScenario : Scenario
{
    public override string Name => "hosts";

    public override string Description => "7 hosts from the hostmetrics receiver (one saturated, one stale)";

    private const double GiB = 1024d * 1024 * 1024;

    public override void Generate(SeedContext c)
    {
        (string Name, string Os, int Cpus, double Busy, double MemGb, double MemUsed, double DiskGb, double DiskUsed, int StaleMinutes)[] hosts =
        [
            ("web-01", "linux", 4, 0.42, 16, 0.58, 200, 0.41, 0),
            ("web-02", "linux", 4, 0.86, 16, 0.81, 200, 0.44, 0),
            ("db-01", "linux", 8, 0.63, 64, 0.72, 2000, 0.83, 0),
            ("worker-01", "linux", 8, 0.27, 32, 0.39, 500, 0.22, 0),
            ("worker-02", "linux", 8, 0.31, 32, 0.41, 500, 0.24, 0),
            ("build-agent-mac", "darwin", 10, 0.08, 32, 0.47, 1000, 0.61, 0),
            ("worker-03", "linux", 8, 0.29, 32, 0.40, 500, 0.23, Math.Min(21, c.WindowMinutes / 3)),
        ];
        var minutes = c.Ticks(60).ToArray();
        var saturatedFrom = (int)(minutes.Length * 0.63);
        var saturatedTo = (int)(minutes.Length * 0.77);
        const string Scope = "github.com/open-telemetry/opentelemetry-collector-contrib/receiver/hostmetricsreceiver";

        foreach (var (name, os, cpus, busy, memGb, memUsed, diskGb, diskUsed, staleMinutes) in hosts)
        {
            var end = c.NowNanos - staleMinutes * 60L * Otlp.NanosPerSecond;
            List<JsonObject> cpu = [], memory = [], filesystem = [], load1 = [], load5 = [], load15 = [];
            var totals = new double[cpus, 4]; // user, system, iowait, idle
            string[] states = ["user", "system", "iowait", "idle"];

            for (var i = 0; i < minutes.Length && minutes[i] <= end; i++)
            {
                var t = minutes[i];
                var wave = busy * (1 + 0.18 * Math.Sin(i / 9.0)) + (c.Rng.NextDouble() * 0.08 - 0.04);
                if (name == "web-02" && i >= saturatedFrom && i <= saturatedTo)
                {
                    wave = 0.97;
                }

                wave = Math.Clamp(wave, 0.01, 0.99);
                for (var core = 0; core < cpus; core++)
                {
                    totals[core, 0] += 60 * wave * 0.7;
                    totals[core, 1] += 60 * wave * 0.2;
                    totals[core, 2] += 60 * wave * 0.1;
                    totals[core, 3] += 60 * (1 - wave);
                    for (var s = 0; s < 4; s++)
                    {
                        cpu.Add(Point(t, Math.Round(totals[core, s], 2), Otlp.Attrs(("cpu", $"cpu{core}"), ("state", states[s])), c.StartNanos));
                    }
                }

                var used = memGb * GiB * Math.Min(0.97, memUsed * (1 + 0.05 * Math.Sin(i / 13.0)) + (name == "web-02" && i > saturatedFrom ? 0.1 : 0));
                foreach (var (state, value) in (ReadOnlySpan<(string, double)>)[("used", used), ("free", memGb * GiB - used - 0.08 * memGb * GiB), ("cached", 0.08 * memGb * GiB)])
                {
                    memory.Add(Point(t, Math.Max(value, 0), Otlp.Attrs(("state", state)), c.StartNanos, asInt: true));
                }

                var fsUsed = diskGb * GiB * (diskUsed + i * 0.0004);
                foreach (var (state, value) in (ReadOnlySpan<(string, double)>)[("used", fsUsed), ("free", diskGb * GiB - fsUsed - 0.05 * diskGb * GiB), ("reserved", 0.05 * diskGb * GiB)])
                {
                    filesystem.Add(Point(t, value, Otlp.Attrs(
                        ("device", os == "darwin" ? "/dev/disk3s1" : "/dev/nvme0n1p1"), ("mountpoint", "/"),
                        ("type", os == "darwin" ? "apfs" : "ext4"), ("mode", "rw"), ("state", state)), c.StartNanos, asInt: true));
                }

                var load = cpus * wave * (c.Rng.NextDouble() * 0.2 + 0.9);
                load1.Add(Point(t, Math.Round(load * (c.Rng.NextDouble() * 0.5 + 0.8), 2)));
                load5.Add(Point(t, Math.Round(load, 2)));
                load15.Add(Point(t, Math.Round(cpus * busy * 1.02, 2)));
            }

            var resource = c.Batch.Resource(("host.name", name), ("os.type", os), ("host.arch", os == "darwin" ? "arm64" : "amd64"));
            c.Batch.AddMetric(resource, Scope, Sum("system.cpu.time", "s", "Total seconds each logical CPU spent on each mode.", cpu, monotonic: true));
            c.Batch.AddMetric(resource, Scope, Sum("system.memory.usage", "By", "Bytes of memory in use.", memory, monotonic: false));
            c.Batch.AddMetric(resource, Scope, Sum("system.filesystem.usage", "By", "Filesystem bytes used.", filesystem, monotonic: false));
            c.Batch.AddMetric(resource, Scope, Gauge("system.cpu.load_average.1m", "{thread}", "Average CPU Load over 1 minute.", load1));
            c.Batch.AddMetric(resource, Scope, Gauge("system.cpu.load_average.5m", "{thread}", "Average CPU Load over 5 minutes.", load5));
            c.Batch.AddMetric(resource, Scope, Gauge("system.cpu.load_average.15m", "{thread}", "Average CPU Load over 15 minutes.", load15));
        }

        // App logs from web-02 while it's saturated - host.name on the resource is what lets the
        // log detail panel show that host's metrics.
        var logs = c.Batch.Service("checkout-api", ("host.name", "web-02"));
        for (var k = 0; k < 40; k++)
        {
            var t = minutes[saturatedFrom] + (long)(k * (saturatedTo - saturatedFrom) / 40.0 * 60 * Otlp.NanosPerSecond);
            var error = k % 4 == 0;
            c.Log(logs, "Checkout.Api", t, error ? 17 : 13,
                error ? "Request timed out after 30000 ms waiting for a worker thread" : "Thread pool starvation detected: 184 queued work items");
        }
    }
}

/// <summary>
/// kubeletstats + k8s_cluster receiver metrics for a three-node cluster - pods across four
/// namespaces, a crash-looping payment-service pod (14 restarts), a Pending pod leaving the
/// payment-service Deployment at 2/3, a failed nightly Job - for the Kubernetes docs. The live
/// shop demo can't produce these (no cluster), so this is the only source.
/// </summary>
public sealed class KubernetesScenario : Scenario
{
    public override string Name => "kubernetes";

    public override string Description => "A 3-node cluster: crash-looping pod, Pending pod, 2/3 Deployment, failed Job";

    private const double MiB = 1024d * 1024;
    private const string KubeletStats = "github.com/open-telemetry/opentelemetry-collector-contrib/receiver/kubeletstatsreceiver";
    private const string K8sCluster = "github.com/open-telemetry/opentelemetry-collector-contrib/receiver/k8sclusterreceiver";

    public override void Generate(SeedContext c)
    {
        var minutes = c.Ticks(60).ToArray();
        var cluster = ("k8s.cluster.name", (object)"prod-eu-1");
        (string Name, int Cores, int Gb)[] nodes =
        [
            ("ip-10-0-1-12.eu-west-1.compute.internal", 4, 16),
            ("ip-10-0-2-37.eu-west-1.compute.internal", 4, 16),
            ("ip-10-0-3-58.eu-west-1.compute.internal", 8, 32),
        ];

        // namespace, pod, workload kind, workload, node (-1 = unscheduled), cpu cores, memory MiB, cpu limit, memory limit MiB, phase, restarts
        (string Ns, string Pod, string Kind, string Workload, int Node, double Cpu, double Mem, double CpuLimit, double MemLimit, int Phase, int Restarts)[] pods =
        [
            ("shop", "storefront-7d9f8c6b5-2xkqp", "deployment", "storefront", 0, 0.32, 410, 1.0, 1024, 2, 0),
            ("shop", "storefront-7d9f8c6b5-8hvzn", "deployment", "storefront", 1, 0.29, 395, 1.0, 1024, 2, 0),
            ("shop", "storefront-7d9f8c6b5-qm4tw", "deployment", "storefront", 2, 0.35, 420, 1.0, 1024, 2, 0),
            ("shop", "checkout-api-5c8b7f4d9-4jxlr", "deployment", "checkout-api", 0, 0.61, 690, 1.0, 768, 2, 1),
            ("shop", "checkout-api-5c8b7f4d9-w7fcd", "deployment", "checkout-api", 2, 0.58, 702, 1.0, 768, 2, 0),
            ("payments", "payment-service-6f4d8b9c7-kz2mx", "deployment", "payment-service", 1, 0.44, 505, 1.0, 512, 2, 14),
            ("payments", "payment-service-6f4d8b9c7-p9rtq", "deployment", "payment-service", 2, 0.41, 480, 1.0, 512, 2, 0),
            ("payments", "payment-service-6f4d8b9c7-zc8vn", "deployment", "payment-service", -1, 0, 0, 1.0, 512, 1, 0),
            ("payments", "fraud-check-8b6c5d7f4-hx3lw", "deployment", "fraud-check", 0, 0.22, 260, 0.5, 512, 2, 0),
            ("data", "postgres-0", "statefulset", "postgres", 2, 1.4, 3900, 2.0, 6144, 2, 0),
            ("data", "redis-0", "statefulset", "redis", 1, 0.12, 610, 0.5, 1024, 2, 0),
            ("data", "nightly-report-29311840-7qk2d", "job", "nightly-report-29311840", 0, 0, 0, 1.0, 1024, 4, 3),
            ("observability", "otel-collector-agent-6rj2k", "daemonset", "otel-collector-agent", 0, 0.09, 180, 0.25, 256, 2, 0),
            ("observability", "otel-collector-agent-b8xhq", "daemonset", "otel-collector-agent", 1, 0.08, 172, 0.25, 256, 2, 0),
            ("observability", "otel-collector-agent-t2mvd", "daemonset", "otel-collector-agent", 2, 0.11, 191, 0.25, 256, 2, 0),
            ("observability", "otel-collector-cluster-7f5c9d8b6-lnq4s", "deployment", "otel-collector-cluster", 1, 0.05, 140, 0.25, 256, 2, 0),
            ("observability", "grafana-agent-5b7d9c8f6-v4kpz", "deployment", "grafana-agent", 2, 0.03, 95, 0.25, 256, 2, 2),
        ];

        JsonObject Series(string name, string unit, Func<int, double> value, Func<JsonArray>? attributes = null) =>
            Gauge(name, unit, "", minutes.Select((t, i) => Point(t, value(i), attributes?.Invoke())));

        var nodeCpu = new double[nodes.Length];
        var nodeMem = new double[nodes.Length];
        foreach (var (ns, pod, kind, workload, node, cpu, mem, cpuLimit, memLimit, phase, restarts) in pods)
        {
            List<(string, object)> attributes = [cluster, ("k8s.namespace.name", ns), ("k8s.pod.name", pod), ("k8s.pod.uid", PodUid(pod)), ($"k8s.{kind}.name", workload)];
            if (node >= 0)
            {
                attributes.Add(("k8s.node.name", nodes[node].Name));
            }

            if (kind == "deployment")
            {
                attributes.Add(("k8s.replicaset.name", pod[..pod.LastIndexOf('-')]));
            }

            if (kind == "job")
            {
                attributes.Add(("k8s.cronjob.name", "nightly-report"));
            }

            var resource = c.Batch.Resource([.. attributes]);
            c.Batch.AddMetric(resource, KubeletStats, Series("k8s.pod.phase", "", _ => phase));
            c.Batch.AddMetric(resource, KubeletStats, Series("k8s.container.restarts", "{restart}", _ => restarts, () => Otlp.Attrs(("k8s.container.name", workload))));
            if (node >= 0 && cpu > 0)
            {
                var cpuAt = minutes.Select((_, i) => Math.Max(0.005, cpu * (1 + 0.2 * Math.Sin(i / 7.0)) + (c.Rng.NextDouble() * 0.06 - 0.03))).ToArray();
                var memAt = minutes.Select((_, i) => (mem + (pod.StartsWith("payment-service", StringComparison.Ordinal) ? i * 1.2 : 0)) * MiB * (c.Rng.NextDouble() * 0.04 + 0.98)).ToArray();
                c.Batch.AddMetric(resource, KubeletStats, Series("k8s.pod.cpu.usage", "{cpu}", i => cpuAt[i]));
                c.Batch.AddMetric(resource, KubeletStats, Series("k8s.pod.memory.working_set", "By", i => memAt[i]));
                c.Batch.AddMetric(resource, KubeletStats, Series("k8s.pod.cpu_limit_utilization", "1", i => cpuAt[i] / cpuLimit));
                c.Batch.AddMetric(resource, KubeletStats, Series("k8s.pod.memory_limit_utilization", "1", i => memAt[i] / (memLimit * MiB)));
                nodeCpu[node] += cpu;
                nodeMem[node] += mem;
            }
        }

        for (var n = 0; n < nodes.Length; n++)
        {
            var (name, cores, gb) = nodes[n];
            var baseCpu = nodeCpu[n] + 0.35;
            var baseMem = (nodeMem[n] + 1400) * MiB;
            var resource = c.Batch.Resource(cluster, ("k8s.node.name", name));
            c.Batch.AddMetric(resource, KubeletStats, Series("k8s.node.cpu.usage", "{cpu}", i => baseCpu * (1 + 0.15 * Math.Sin(i / 8.0)) + (c.Rng.NextDouble() * 0.1 - 0.05)));
            c.Batch.AddMetric(resource, KubeletStats, Series("k8s.node.memory.working_set", "By", _ => baseMem * (c.Rng.NextDouble() * 0.04 + 0.98)));
            c.Batch.AddMetric(resource, KubeletStats, Series("k8s.node.memory.available", "By", _ => gb * 1024 * MiB - baseMem - 1.5 * 1024 * MiB));
            c.Batch.AddMetric(resource, K8sCluster, Series("k8s.node.condition_ready", "", _ => 1));
            c.Batch.AddMetric(resource, K8sCluster, Series("k8s.node.allocatable_cpu", "{cpu}", _ => cores - 0.1));
        }

        foreach (var ns in (string[])["shop", "payments", "data", "observability"])
        {
            c.Batch.AddMetric(c.Batch.Resource(cluster, ("k8s.namespace.name", ns)), K8sCluster, Series("k8s.namespace.phase", "", _ => 1));
        }

        (string Ns, string Kind, string Name, (string Metric, double Value)[] Values)[] workloads =
        [
            ("shop", "deployment", "storefront", [("k8s.deployment.desired", 3), ("k8s.deployment.available", 3)]),
            ("shop", "deployment", "checkout-api", [("k8s.deployment.desired", 2), ("k8s.deployment.available", 2)]),
            ("payments", "deployment", "payment-service", [("k8s.deployment.desired", 3), ("k8s.deployment.available", 2)]),
            ("payments", "deployment", "fraud-check", [("k8s.deployment.desired", 1), ("k8s.deployment.available", 1)]),
            ("observability", "deployment", "otel-collector-cluster", [("k8s.deployment.desired", 1), ("k8s.deployment.available", 1)]),
            ("observability", "deployment", "grafana-agent", [("k8s.deployment.desired", 1), ("k8s.deployment.available", 1)]),
            ("data", "statefulset", "postgres", [("k8s.statefulset.desired_pods", 1), ("k8s.statefulset.ready_pods", 1)]),
            ("data", "statefulset", "redis", [("k8s.statefulset.desired_pods", 1), ("k8s.statefulset.ready_pods", 1)]),
            ("observability", "daemonset", "otel-collector-agent", [("k8s.daemonset.desired_scheduled_nodes", 3), ("k8s.daemonset.ready_nodes", 3)]),
            ("data", "job", "nightly-report-29311840", [("k8s.job.desired_successful_pods", 1), ("k8s.job.successful_pods", 0), ("k8s.job.failed_pods", 3), ("k8s.job.active_pods", 0)]),
            ("data", "cronjob", "nightly-report", [("k8s.cronjob.active_jobs", 0)]),
        ];
        foreach (var (ns, kind, name, values) in workloads)
        {
            var resource = c.Batch.Resource(cluster, ("k8s.namespace.name", ns), ($"k8s.{kind}.name", name));
            foreach (var (metric, value) in values)
            {
                c.Batch.AddMetric(resource, K8sCluster, Series(metric, "", _ => value));
            }
        }
    }

    // Stable per pod name, so a re-seed keeps the same uid.
    private static string PodUid(string pod) =>
        Convert.ToHexStringLower(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(pod)));
}
