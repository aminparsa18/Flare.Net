# ADR-0078: Sub-path hosting via a runtime-rewritten base path

Status: Accepted

Date: 2026-10-02

## Context

Flare assumed it owned the root of a host: the dashboard set no SvelteKit
`paths.base` and the API had no path base, so `https://example.com/flare/`
didn't work. Prior art:
[signoz#10943](https://github.com/SigNoz/signoz/commit/ef298af3885b4a0f4f38e49241e6316367721a9e).

SvelteKit's `paths.base` is a build-time constant: it is inlined into the
server and client bundles and names adapter-node's static-file directory
(`build/client<base>/`). The published image is built once and configured by
environment variables at `docker run` time, so it can't take the value at build.

## Decision

**One setting, `FLARE_BASE_PATH` (dashboard) / `Flare__BasePath` (API), e.g.
`/flare`. Empty means the root, as before.**

- **Dashboard:** the image is built with the placeholder
  `/__FLARE_BASE_PATH__` as `paths.base`. `server.js` calls
  `apply-base-path.mjs` on every start, which copies the pristine `build/` to
  `build-runtime/`, replaces the placeholder in every text file (regenerating
  the `.gz`/`.br` siblings sirv prefers) and moves the static directory to match.
  Rewriting a copy rather than `build/` makes a changed env var plus restart
  always work. A build without the placeholder (local `npm run build`, or one
  made with `FLARE_BASE_PATH` set) is served as-is. All in-app links, `goto`
  calls and deep-link builders go through `$lib/paths` (`withBase`/`stripBase`),
  because SvelteKit does not prefix hand-written root-relative URLs.
- **API:** `UseFlareBasePath` moves the prefix from `Request.Path` to
  `Request.PathBase`, and sets `PathBase` even when the proxy already stripped the
  prefix (the stock `UsePathBase` would leave it empty there). OIDC/Entra
  `RedirectUri`s, the redirect URIs shown to admins, and the session cookie
  `Path` derive from `PathBase`. Routes stay `/api/...`.
- **Notification links** needed no code: `Alerting__PublicUrl` is already a base
  URL, so the operator includes the prefix.

## Consequences

- No second image variant and no rebuild to change the prefix.
- The dashboard container does a small file copy on startup (a few MB).
- New code that builds an in-app URL by hand must use `withBase`; a bare
  `href="/x"` silently breaks sub-path deployments only.
- Not covered: the Aspire hosting integration and the `flare` CLI don't expose
  the setting yet; the standalone compose file does.
