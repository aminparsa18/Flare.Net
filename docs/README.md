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

**How-to guides**
- [Run standalone](how-to/run-standalone.md)
- [Run with .NET Aspire](how-to/run-with-aspire.md)
- [Run with the CLI](how-to/run-with-cli.md)
- [Configure authentication](how-to/configure-authentication.md)
- [Run in cluster mode](how-to/run-cluster-mode.md)
- [Let an AI assistant query Flare](how-to/connect-ai-assistants.md)
- [Query Flare with Grafana or the Prometheus API](how-to/query-with-prometheus-api.md)
- [Serve under a sub-path](how-to/serve-under-a-sub-path.md)
- [Deploy with Helm](how-to/deploy-with-helm.md)
- [Build a custom dashboard](how-to/build-custom-dashboards.md)
- [Email a dashboard on a schedule](how-to/schedule-dashboard-reports.md)
- [Extract or redact fields at ingest](how-to/manage-pipeline-rules.md)
- [Monitor hosts with the OpenTelemetry Collector](how-to/monitor-hosts.md)
- [Monitor Kubernetes clusters (nodes, workloads, pods, volumes) with the OpenTelemetry Collector](how-to/monitor-kubernetes.md)
- [Monitor message queues](how-to/monitor-message-queues.md)
- [Monitor external APIs](how-to/monitor-external-apis.md)
- [Monitor LLM calls](how-to/monitor-llm-calls.md)
- [Find where requests drop off with trace funnels](how-to/analyze-trace-funnels.md)
- [Find N+1 queries](how-to/find-n-plus-one-queries.md)
- [Explore continuous profiles](how-to/profile-with-continuous-profiling.md)
- [Find .NET runtime health problems](how-to/find-runtime-health-problems.md)
- [Compare two deploys of a service](how-to/compare-deploys.md)
- [Define SLOs and get alerted when the error budget burns](how-to/define-slos.md)
- [Organize teams with projects](how-to/organize-teams-with-projects.md)
- [Find traces by how their spans relate](how-to/find-traces-by-structure.md)
- [Link exception stack traces to your source code](how-to/link-exceptions-to-source-code.md)
- [Triage errors and catch regressions](how-to/triage-errors.md)
- [Get an AI summary of a fired alert](how-to/summarize-alerts-with-ai.md)
- [Acknowledge or snooze a firing alert](how-to/acknowledge-and-snooze-alerts.md)
- [Share notification wording across alert rules](how-to/share-alert-notification-templates.md)
- [Send browser telemetry to Flare](how-to/send-browser-telemetry.md)
- [Monitor endpoints, ports and certificates with synthetic probes](how-to/synthetic-monitoring.md)
- [Publish a status page](how-to/publish-a-status-page.md)
- [Tell whether a span was slow for what it is](how-to/compare-span-duration.md)
- [Find high-cardinality metrics](how-to/find-high-cardinality-metrics.md)
- [Reduce a metric's attributes at ingest](how-to/reduce-metric-attributes.md)
- [Turn a log search into a metric](how-to/log-based-metrics.md)
- [Set data retention and move old data to cold storage](how-to/set-data-retention.md)
- [See what is driving storage](how-to/see-what-drives-storage.md)
- [Forward telemetry to another OTLP endpoint](how-to/forward-telemetry-to-another-otlp-endpoint.md)
- [Archive telemetry to S3-compatible storage](how-to/archive-telemetry-to-s3.md)
- [Speed up filters on a frequently used log or span attribute](how-to/promote-attribute-columns.md)

**Reference**
- [CLI commands](reference/cli-commands.md)
- [Aspire hosting](reference/aspire-hosting.md)
- [Authentication config](reference/authentication-config.md)
- [Clustering config](reference/clustering-config.md)
- [OTLP logger versions](reference/otlp-logger-versions.md)

**Explanation**
- [Architecture](explanation/architecture.md)
- [Clustering](explanation/clustering.md)
- [Authentication model](explanation/authentication-model.md)

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