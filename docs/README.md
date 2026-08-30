# Documentation

This project uses the [Diátaxis](https://diataxis.fr/) documentation
framework (also installed as an AI skill: `.claude/skills/diataxis`).

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

## Migration in progress

This `tutorials/`/`how-to/`/`reference/`/`explanation/` structure was
scaffolded as Phase 2 of a documentation restructuring
(see [`DOCUMENTATION-MIGRATION-PLAN.md`](DOCUMENTATION-MIGRATION-PLAN.md)
for the full plan). It is **not yet populated** — the documents below still
live at the top of `docs/` in their pre-migration form and each is a mix of
several Diátaxis types. They move into the folders above, split by type, in
later phases:

| Existing doc | Will split into |
|---|---|
| ~~[getting-started.md](getting-started.md)~~ | **Done (Phase 6)** → [tutorials/getting-started.md](tutorials/getting-started.md) + [explanation/architecture.md](explanation/architecture.md) (Tour of the dashboard). Old path kept as a redirect stub. |
| ~~[standalone.md](standalone.md)~~ | **Done (Phase 6)** → [how-to/run-standalone.md](how-to/run-standalone.md) + [reference/otlp-logger-versions.md](reference/otlp-logger-versions.md) + [ADR-0005](../docs-internal/adr/0005-docker-socket-proxy-for-resources-page.md). Old path kept as a redirect stub. |
| ~~[aspire-hosting.md](aspire-hosting.md)~~ | **Done (Phase 6)** → [how-to/run-with-aspire.md](how-to/run-with-aspire.md) + [reference/aspire-hosting.md](reference/aspire-hosting.md) + [ADR-0006](../docs-internal/adr/0006-kubernetes-resource-graph-rbac-scoping.md) + [an investigation](../docs-internal/investigations/aspire-kubernetes-publish-and-resource-graph.md). Old path kept as a redirect stub. |
| ~~[cli.md](cli.md)~~ | **Done (Phase 4)** → [reference/cli-commands.md](reference/cli-commands.md) + [how-to/run-with-cli.md](how-to/run-with-cli.md) + [explanation/architecture.md](explanation/architecture.md) (seeded, not yet complete — see that file's own note). Old path kept as a redirect stub — see that file. |
| ~~[clustering.md](clustering.md)~~ | **Done (Phase 3)** → [explanation/clustering.md](explanation/clustering.md) + [how-to/run-cluster-mode.md](how-to/run-cluster-mode.md) + [reference/clustering-config.md](reference/clustering-config.md) + [ADR-0003](../docs-internal/adr/0003-distributed-tables-plain-names-and-sharding.md) + [an investigation](../docs-internal/investigations/clickhouse-cluster-operational-notes.md). Old path kept as a redirect stub — see that file. |
| ~~[auth.md](auth.md)~~ | **Done (Phase 5)** → [how-to/configure-authentication.md](how-to/configure-authentication.md) + [explanation/authentication-model.md](explanation/authentication-model.md) + [reference/authentication-config.md](reference/authentication-config.md) + [ADR-0004](../docs-internal/adr/0004-embedded-sqlite-for-identity.md). Old path kept as a redirect stub — see that file. |
| ~~[benchmark.md](benchmark.md)~~ | **Done (Phase 7)** → moved wholesale, unsplit, to [`../docs-internal/investigations/benchmark-ingest-and-query.md`](../docs-internal/investigations/benchmark-ingest-and-query.md) — it was evidence end-to-end, not a guide. Old path kept as a redirect stub. |

Until each phase lands, treat the files above as the current source of truth
for their topic, and the folders in this README as the destination, not yet
the source.

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
