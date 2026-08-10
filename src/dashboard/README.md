# Flare Dashboard

The SPA frontend for [Flare](../../Planning.md) — SvelteKit 2 (Svelte 5, runes) + Tailwind 4 + shadcn-svelte (`mira` style).

Talks to `Flare.Api` over plain HTTP/WebSocket (see `src/lib/api.ts`); no server-side rendering of log data is planned, so this stays a straightforward client-rendered app.

## Developing

Flare.Api must be running first (`dotnet run --project ../Flare.Api`, or via the Aspire AppHost) — copy `.env.example` to `.env` and point `PUBLIC_API_URL` at wherever it's listening (defaults to `http://localhost:5085`, Flare.Api's fixed standalone dev port).

```sh
npm install
npm run dev -- --open
```

## Building

```sh
npm run build
npm run preview
```

`vite.config.ts` uses `@sveltejs/adapter-node` — picked as part of the `docker-compose.yml` v1 roadmap item (see the repo root's `docker-compose.yml` and this project's `Dockerfile`). Has to be a real Node server, not `adapter-static`: `PUBLIC_API_URL` (`src/lib/api.ts`) is read via `$env/dynamic/public`, resolved per-request at runtime, not baked in at build time.

## Status

v1: two pages, sharing `AppNav.svelte`'s app-shell nav (`+layout.svelte`) —

- **`/` — Logs Explorer.** Virtualized log table, filter toolbar, event detail sheet,
  live tail, and volume chart, talking to `Flare.Api`'s search/aggregate/live-tail
  surfaces end to end.
- **`/alerts` — Alerts.** Threshold/query-based alert rule list, create/edit form (with
  a live "test against current data" dry-run before saving) and fired-alert history
  view, talking to `Flare.Api`'s `/api/alerts/*` surface. A rule notifies exactly one of
  three channels — webhook/Slack, Telegram, or email — via the form's "Notify via"
  selector; see [`../Flare.Api/README.md`](../Flare.Api/README.md#alerting) for what
  each channel needs configured server-side.
