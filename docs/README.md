# Documentation

## Quick links

| Type | Purpose | Start here |
|------|---------|------------|
| [Tutorials](tutorials/) | Learn by doing | For newcomers |
| [How-to guides](how-to/) | Solve a specific problem | For practitioners |
| [Reference](reference/) | Exact technical details | For lookup |
| [Explanation](explanation/) | Understand why Flare works this way | For deeper knowledge |

- **New to Flare?** Start with [tutorials](tutorials/).
- **Need to accomplish a task** (run standalone, configure auth, run a
  cluster)? Check [how-to guides](how-to/).
- **Looking up a CLI command, config key, or schema column?** See
  [reference](reference/).
- **Want to understand why Flare is built this way?** Read
  [explanation](explanation/).

Maintainer-facing documentation (architecture decisions, technical
investigations, the roadmap) lives outside this tree, in
[`../docs-internal/`](../docs-internal/) — see that folder's `README.md` for
the full rule set on what goes where.

## All pages

**Tutorials**
- [Getting started](tutorials/getting-started.md)
- [Tutorial: your first trace](tutorials/first-trace.md)
- [Tutorial: your first alert](tutorials/first-alert.md)
- [Tutorial: your first metrics dashboard](tutorials/first-dashboard.md)
- [Tutorial: debug a slow checkout end to end](tutorials/debug-a-slow-request.md)
- [Tutorial: add Flare to a .NET Aspire app](tutorials/add-flare-to-an-aspire-app.md)
- [Tutorial: monitor a .NET MAUI app](tutorials/monitor-a-maui-app.md)

**How-to guides**

*Deploy and operate*

- [Run standalone](how-to/deploy/run-standalone.md)
- [Run with .NET Aspire](how-to/deploy/run-with-aspire.md)
- [Run with the CLI](how-to/deploy/run-with-cli.md)
- [Run in cluster mode](how-to/deploy/run-cluster-mode.md)
- [Serve under a sub-path](how-to/deploy/serve-under-a-sub-path.md)
- [Configure security response headers](how-to/deploy/security-response-headers.md)
- [Deploy with Helm](how-to/deploy/deploy-with-helm.md)

*Administration*

- [Configure authentication](how-to/admin/configure-authentication.md)
- [Organize teams with projects](how-to/admin/organize-teams-with-projects.md)

*Instrument your apps (browser, .NET MAUI)*

- [Send browser telemetry to Flare](how-to/instrument/send-browser-telemetry.md)
- [Send telemetry from a .NET MAUI app to Flare](how-to/instrument/send-maui-telemetry.md)
- [Send telemetry from an Orleans app to Flare](how-to/instrument/send-orleans-telemetry.md)

*Monitor infrastructure and services*

- [Monitor hosts with the OpenTelemetry Collector](how-to/monitor/monitor-hosts.md)
- [Monitor Kubernetes clusters (nodes, workloads, pods, volumes) with the OpenTelemetry Collector](how-to/monitor/monitor-kubernetes.md)
- [Monitor message queues](how-to/monitor/monitor-message-queues.md)
- [Monitor Dapr applications](how-to/monitor/monitor-dapr-applications.md)
- [Monitor external APIs](how-to/monitor/monitor-external-apis.md)
- [Monitor LLM calls](how-to/monitor/monitor-llm-calls.md)
- [Monitor endpoints, ports and certificates with synthetic probes](how-to/monitor/synthetic-monitoring.md)
- [Monitor cron jobs and scheduled tasks with check-ins](how-to/monitor/monitor-cron-jobs.md)
- [Publish a status page](how-to/monitor/publish-a-status-page.md)

*Investigate traces, errors and releases*

- [Find where requests drop off with trace funnels](how-to/investigate/analyze-trace-funnels.md)
- [Find N+1 queries](how-to/investigate/find-n-plus-one-queries.md)
- [Explore continuous profiles](how-to/investigate/profile-with-continuous-profiling.md)
- [Find .NET runtime health problems](how-to/investigate/find-runtime-health-problems.md)
- [Compare two deploys of a service](how-to/investigate/compare-deploys.md)
- [Track releases and the errors they introduce](how-to/investigate/track-releases.md)
- [Find traces by how their spans relate](how-to/investigate/find-traces-by-structure.md)
- [Link exception stack traces to your source code](how-to/investigate/link-exceptions-to-source-code.md)
- [Triage errors and catch regressions](how-to/investigate/triage-errors.md)
- [Tell whether a span was slow for what it is](how-to/investigate/compare-span-duration.md)

*Alerting and SLOs*

- [Define SLOs and get alerted when the error budget burns](how-to/alerting/define-slos.md)
- [Get an AI summary of a fired alert](how-to/alerting/summarize-alerts-with-ai.md)
- [Acknowledge or snooze a firing alert](how-to/alerting/acknowledge-and-snooze-alerts.md)
- [Share notification wording across alert rules](how-to/alerting/share-alert-notification-templates.md)

*Dashboards*

- [Build a custom dashboard](how-to/dashboards/build-custom-dashboards.md)
- [Email a dashboard on a schedule](how-to/dashboards/schedule-dashboard-reports.md)

*Manage data, retention and cost*

- [Extract or redact fields at ingest](how-to/data/manage-pipeline-rules.md)
- [Find high-cardinality metrics](how-to/data/find-high-cardinality-metrics.md)
- [Reduce a metric's attributes at ingest](how-to/data/reduce-metric-attributes.md)
- [Turn a log search into a metric](how-to/data/log-based-metrics.md)
- [Set data retention and move old data to cold storage](how-to/data/set-data-retention.md)
- [Erase or export one person's telemetry](how-to/data/erase-or-export-a-data-subjects-telemetry.md)
- [See what is driving storage](how-to/data/see-what-drives-storage.md)
- [Forward telemetry to another OTLP endpoint](how-to/data/forward-telemetry-to-another-otlp-endpoint.md)
- [Archive telemetry to S3-compatible storage](how-to/data/archive-telemetry-to-s3.md)
- [Speed up filters on a frequently used log or span attribute](how-to/data/promote-attribute-columns.md)

*Integrations*

- [Let an AI assistant query Flare](how-to/integrations/connect-ai-assistants.md)
- [Query Flare with Grafana or the Prometheus API](how-to/integrations/query-with-prometheus-api.md)
- [Read Flare traces from Grafana with the Tempo API](how-to/integrations/query-with-tempo-api.md)
- [Read Flare logs from Grafana with the Loki API](how-to/integrations/query-with-loki-api.md)
- [Chart Flare sessions, errors and release health in Grafana](how-to/integrations/query-with-grafana-plugin.md)

**Reference**
- [CLI commands](reference/cli-commands.md)
- [Aspire hosting](reference/aspire-hosting.md)
- [Authentication config](reference/authentication-config.md)
- [Clustering config](reference/clustering-config.md)
- [Live-tail config](reference/live-tail-config.md)
- [OTLP logger versions](reference/otlp-logger-versions.md)

**Explanation**
- [Architecture](explanation/architecture.md)
- [Clustering](explanation/clustering.md)
- [Authentication model](explanation/authentication-model.md)
- [How alerting works](explanation/how-alerting-works.md)
- [How Flare stores and queries telemetry](explanation/how-flare-stores-and-queries-telemetry.md)
- [Ingest reliability and delivery guarantees](explanation/ingest-reliability-and-delivery-guarantees.md)
- [Sampling, clustering and cost](explanation/sampling-clustering-and-cost.md)
- [Projects and access scoping](explanation/projects-and-access-scoping.md)
- [Retention and tiering](explanation/retention-and-tiering.md)
- [Data residency and privacy](explanation/data-residency-and-privacy.md)

## Contributing documentation

When adding new documentation, work out its type first — see
[`../docs-internal/README.md`](../docs-internal/README.md#where-does-new-information-belong)
for the decision tree (it also covers when something is an ADR or
investigation instead of a `docs/` page):

1. **Tutorial**: step-by-step lesson for a beginner
2. **How-to guide**: task-focused instructions for someone who knows what
   they want, not how
3. **Reference**: factual, comprehensive, dry description
4. **Explanation**: context, background, and why