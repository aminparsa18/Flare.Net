# Contributing to Flare

Thanks for considering it. Flare is actively developed (see
[docs/explanation/architecture.md](docs/explanation/architecture.md) for
what it is and [docs-internal/planning/roadmap.md](docs-internal/planning/roadmap.md)
for what's currently open) — not pre-alpha, but the architecture can still
shift. For anything beyond a small, self-contained fix, open an issue to
discuss direction before writing a large PR.

## Building and running it locally

There's no single top-level build script — each project has its own
README with build/run/test instructions:
[Flare.Ingest](src/Flare.Ingest/README.md),
[Flare.Api](src/Flare.Api/README.md),
[dashboard](src/dashboard/README.md),
[Flare.Cli](src/Flare.Cli/README.md). The whole stack together is
orchestrated locally via [Flare.AppHost](src/Flare.AppHost) (.NET
Aspire) — see [docs/how-to/run-with-aspire.md](docs/how-to/run-with-aspire.md)
for the general pattern, or `docker compose up` at the repo root for a
standalone stack (see [docs/how-to/run-standalone.md](docs/how-to/run-standalone.md)).

## Where things live

Before adding a new document (or wondering where a fact you're changing
should be recorded), read
[docs-internal/README.md](docs-internal/README.md)'s decision tree. Short
version:

- **User-facing docs** (how to use Flare) live in `docs/`, split by
  [Diátaxis](https://diataxis.fr/) type — tutorial, how-to, reference,
  explanation.
- **Architecture decisions** — what was decided and why — go in
  [docs-internal/adr/](docs-internal/adr/) as a new numbered ADR, only for
  genuinely significant, hard-to-reverse calls. See that folder's own
  bar in `docs-internal/README.md` before adding one.
- **Technical investigations** — what you discovered debugging/
  benchmarking something, with real evidence — go in
  [docs-internal/investigations/](docs-internal/investigations/), when the
  finding has future maintenance value.
- **Future work** goes in
  [docs-internal/planning/roadmap.md](docs-internal/planning/roadmap.md)
  as a short, forward-looking line — not a diary entry. A completed
  roadmap item is deleted, not checked off and kept; this is the rule
  that keeps this file from becoming another sprawling `Planning.md`
  (see `git log -- Planning.md` for what that looked like before it was
  pruned).

## Pull requests

- If your change affects user-visible behavior, update the relevant
  `docs/how-to/` or `docs/reference/` page in the same PR.
- If it adds a config option, update `docs/reference/` in the same PR.
- If it's a significant architectural decision, add an ADR in the same
  PR — not after the fact.
- Run `python3 scripts/check-docs-links.py` if you touched any Markdown
  under `docs/`, `docs-internal/`, this file, or `README.md` — it checks
  that every relative link (including `#heading` anchors) actually
  resolves, and that every `docs/{tutorials,how-to,reference,explanation}/`
  page is reachable from `docs/README.md`'s index.
- Keep commits focused — a docs-only change, a behavior change, and an
  ADR extraction are each easier to review as separate commits/PRs than
  bundled into one.

## Reporting issues

Open a GitHub issue. There's no separate `SECURITY.md` yet for reporting
vulnerabilities privately — for now, flag anything sensitive in the issue
itself and note that it's sensitive, or reach the maintainer directly.