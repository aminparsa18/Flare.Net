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

`vite.config.ts` uses `@sveltejs/adapter-auto`; swap in a specific adapter once a deployment target is picked (tracked as the `docker-compose.yml` v1 roadmap item).

## Status

v1, phase 1: a single Overview page (`/`) that smoke-tests the three Flare.Api surfaces — search, aggregate, and the live-tail WebSocket — end to end. The real log table, filtering, event detail, and volume chart land in later phases.
