# Flare

> This document has moved as part of a documentation restructuring
> ([`docs-internal/planning/DOCUMENTATION-MIGRATION-PLAN.md`](docs-internal/planning/DOCUMENTATION-MIGRATION-PLAN.md)).
> `Planning.md` had grown into four documents at once — a product pitch, an
> architecture explanation, a decision log, and a change diary — with
> ~3,000 lines of shipped-and-checked-off history dressed up as a
> roadmap. Its durable content now lives, split by type, at:
>
> - **[docs/explanation/architecture.md](docs/explanation/architecture.md)**
>   — what Flare is, why it exists, design principles, the ingest →
>   dashboard pipeline, non-goals
> - **[docs-internal/adr/](docs-internal/adr/)** — every architectural
>   decision this project has made and why (13 as of this restructuring:
>   OTLP-only ingestion, ClickHouse as the storage engine, the SvelteKit
>   dashboard stack, Redis Streams buffering, cluster-mode sharding, the
>   ClickHouse schema conventions, and more)
> - **[docs-internal/investigations/](docs-internal/investigations/)** —
>   every technical investigation with lasting value (cluster operational
>   issues, Kubernetes deploy bugs, the ingest/query benchmark, CLI
>   verification bugs, the Logs `VirtualList` hardening deep-dive)
> - **[docs-internal/planning/roadmap.md](docs-internal/planning/roadmap.md)**
>   — the handful of genuinely still-open future items (the ~145
>   shipped-and-checked-off items that used to fill this file are not
>   carried forward; `git log` on this file preserves that history)
> - **[docs/](docs/)** — every user-facing how-to, reference, and tutorial
>   page this file's version-by-version detail used to duplicate
>
> This file is kept as a short redirect (not deleted outright) because
> roughly 60 source files across the repo still reference `Planning.md`
> by name in comments — updating those is a tracked follow-up, not done
> as part of this restructuring. Start at
> [docs/explanation/architecture.md](docs/explanation/architecture.md) if
> you're looking for "what is Flare and how does it work," or
> [docs-internal/planning/roadmap.md](docs-internal/planning/roadmap.md)
> if you're looking for "what's next."