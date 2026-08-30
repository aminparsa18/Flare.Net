# Architecture

> **This page is a seed, not a finished tour.** It currently covers only
> the material Phase 4 of the documentation migration surfaced (why the
> `flare` CLI install path exists, and its image-tag/security rationale).
> The full architecture write-up — the ingest → buffer → ClickHouse → API
> → dashboard pipeline, design principles, and the "tour of the dashboard"
> content — lands in a later phase, migrated from `Planning.md`'s intro and
> `docs/getting-started.md`. See
> [`../DOCUMENTATION-MIGRATION-PLAN.md`](../DOCUMENTATION-MIGRATION-PLAN.md).

## Three ways to run Flare, and why each exists

Flare has three legitimate install paths, each solving a different problem
rather than being redundant with the others:

- **[.NET Aspire](../how-to/run-with-aspire.md)** (`Flare.Hosting.Aspire`) —
  for an app that already has an AppHost. Flare joins the resource graph;
  `aspire start` already orchestrates its lifecycle alongside everything
  else.
- **[Standalone Docker Compose](../how-to/run-standalone.md)** — for a
  one-off, repo-local evaluation. `docker compose up` at the repo root is
  the fastest way to just look at Flare once.
- **[The `flare` CLI](../how-to/run-with-cli.md)** (`Flare.Cli`) — for a
  standing instance you start once and forget about, from any directory,
  shared across many unrelated local projects, independent of any single
  AppHost's lifecycle. This is the case the other two paths structurally
  can't cover: Aspire mode ties Flare's lifecycle to one AppHost, and a
  repo-local Compose stack isn't meant to run for weeks in the background
  serving unrelated projects.

## Why the CLI pins image tags instead of tracking `latest`

`Flare.Cli`-managed instances default to a specific, tested `vX.Y.Z` image
tag rather than the floating `edge`/`latest` tags (see
[the reference](../reference/cli-commands.md#image-tag-policy) for the
exact defaults and version history) — deliberately, so a given `Flare.Cli`
version keeps pulling the same images forever until you explicitly move it.
`flare update` (no `--tag`) re-pulls the same pinned tag rather than
auto-discovering newer releases, and deliberately never will: only this
CLI's own author knows which newer Flare Docker images have actually been
tested against a given `Flare.Cli` version — a newer tag existing on Docker
Hub isn't the same claim. Each new `Flare.Cli` release re-pins its own
template's default once tested against a newer Flare image; existing
installs keep tracking whatever tag they were generated with until you move
them explicitly with `flare update --tag TAG`.

## Why CLI-managed instances get random passwords

The repo's own `docker-compose.yml` ships a documented `flare`/`flare`
default password — fine for a stack you stand up, evaluate, and tear down.
A `Flare.Cli`-managed instance is meant to stand for weeks with its ports
bound on your machine the whole time, not be torn down after a quick eval,
so reusing a public, documented default password for something long-lived
is a foot-gun the CLI doesn't default into. Passwords are generated once at
first init and never rotated afterward (rotating would break
`identity-data`/ClickHouse auth on the next start) — the file is plain text
and yours to hand-edit if you'd rather set your own value.