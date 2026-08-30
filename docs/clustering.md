# Multi-node ClickHouse (opt-in cluster mode)

> This document has moved as part of a documentation restructuring
> ([`DOCUMENTATION-MIGRATION-PLAN.md`](../docs-internal/planning/DOCUMENTATION-MIGRATION-PLAN.md),
> Phase 3). Its content now lives, split by type, at:
>
> - **[explanation/clustering.md](explanation/clustering.md)** — topology,
>   how sharding/replication/load-balancing/pattern-clustering work, the
>   dashboard's Cluster panel
> - **[how-to/run-cluster-mode.md](how-to/run-cluster-mode.md)** — turning
>   cluster mode on and verifying it
> - **[reference/clustering-config.md](reference/clustering-config.md)** —
>   the exact config keys
> - **[`../docs-internal/adr/0003-distributed-tables-plain-names-and-sharding.md`](../docs-internal/adr/0003-distributed-tables-plain-names-and-sharding.md)**
>   — the `Distributed`-table naming and sharding-key decision
> - **[`../docs-internal/investigations/clickhouse-cluster-operational-notes.md`](../docs-internal/investigations/clickhouse-cluster-operational-notes.md)**
>   — the concrete bugs found standing this cluster up for real
>
> This file is kept as a redirect (not deleted outright) because a number of
> source-code comments across `Flare.Api`/`Flare.Ingest`/`Flare.Cli`/the
> dashboard still point at `docs/clustering.md` by name — updating those is
> tracked as a follow-up, not done in this pass. Start at
> [explanation/clustering.md](explanation/clustering.md) if you followed one
> of those comments here.