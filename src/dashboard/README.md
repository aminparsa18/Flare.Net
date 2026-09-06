# Flare Dashboard

The SPA frontend for [Flare](../../docs/explanation/architecture.md) — SvelteKit 2 (Svelte 5, runes) + Tailwind 4 + shadcn-svelte (`mira` style).

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

## Internationalization

UI strings go through [Paraglide JS](https://paraglidejs.com) (`@inlang/paraglide-js`) — compile-time, type-safe `m.someKey()` calls, not a runtime `$t('key')` store. Two locales exist today: `en` (the base locale) and `zh-CN`, hand-translated (not machine-translated — unrelated to `scripts/translate-docs.py`, which only covers `docs/`/README markdown, not this app).

- **Where translations live:** `project.inlang/settings.json` (locale registry + compiler config) and `messages/en.json`/`messages/zh-CN.json` (flat `key: string` maps, `<area>_<description>` camelCase keys) — these are the real, hand-authored source and are committed. `src/lib/paraglide/` (runtime, per-message modules, `messages.ts`, `server.ts`) is generated from them and gitignored — never hand-edit it.
- **Locale detection/persistence:** cookie first (`PARAGLIDE_LOCALE`, set by the nav bar's language switcher), then the browser's `Accept-Language` header, then `en` — see the `strategy` array in `vite.config.ts`'s `paraglideVitePlugin()` and `package.json`'s `codegen` script (kept in sync, one drives the dev/build/preview Vite pipeline, the other drives `svelte-check`, which never runs Vite). `src/hooks.server.ts` resolves the locale for every SSR'd response (this app's shell is server-rendered — see "Building" above) and stamps it onto `<html lang>` in `app.html`.
- **Adding a new message key:** add it to `messages/en.json`, add the matching key to `messages/zh-CN.json`, then `npm run codegen` (or just `npm run dev`) to regenerate `src/lib/paraglide/`. Use it as `import * as m from '$lib/paraglide/messages'` → `m.yourKey()` (or `m.yourKey({ param })` for an interpolated message — see `connError_description`/`login_signInWithSso` for examples with parameters).
- **A key missing from a non-base locale is not a build error** — Paraglide silently falls back to the base locale's (`en`'s) text for that one key at runtime. This is deliberate for rolling out translations incrementally file-by-file rather than in one pass (partially-translated `zh-CN` mid-rollout just shows English for anything not yet translated), but it also means a typo'd or forgotten key in `messages/zh-CN.json` won't be caught by `npm run check` or the build — proofread `messages/zh-CN.json` against `messages/en.json` directly when adding keys.
- **Adding a third locale:** add `messages/<locale>.json` with every key from `messages/en.json` translated, add the locale tag to `project.inlang/settings.json`'s `locales` array, and add a label to `LanguageSwitcher.svelte`'s `localeLabels` map. Nothing else changes.

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
