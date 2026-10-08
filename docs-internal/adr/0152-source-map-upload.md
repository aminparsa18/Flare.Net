# ADR-0152: Source-map upload and read-time symbolication

Status: Accepted

Date: 2026-10-09

## Context

Browser errors reach `/errors` as span exception events ([ADR-0151](0151-browser-errors-and-web-vitals-conventions.md)),
but production bundles are minified, so a stack reads `at o (…/app-abc123.js:1:30)`. The
roadmap item also covers mobile (AOT/trimmed .NET stacks), which needs the same upload
surface, so the storage and key should not be browser-specific in shape.

## Decision

- **Storage is the Identity database** (Sqlite `0032` / Postgres `0007`, table `SourceMaps`),
  not ClickHouse. A map is configuration-like data written by CI, read by one query path, and
  has to survive a ClickHouse retention change. Content is stored gzipped; the row carries the
  uncompressed size. One row per `(ServiceName, Version, Bundle)`; re-uploading replaces it.
- **Key.** `Version` is the occurrence's `service.version`, falling back to the
  `vcs.revision`-style value `ExceptionOccurrence.Revision` already computes, so either can be
  used at upload. `Bundle` is the script's path without the `.map` suffix. A frame matches the
  longest stored bundle that its script URL's path ends with (`/`-aligned), so a CDN prefix or a
  hashed filename does not need configuring. Debug-id matching (bundler-injected) is not done.
- **API.** `PUT /api/source-maps?service=&version=&bundle=` with the raw map JSON (max 50 MB,
  validated as a v3 map; index maps with `sections` are rejected), `GET /api/source-maps`
  (any signed-in user) and `DELETE /api/source-maps?service=&version=[&bundle=]`. Writes are
  Admin-only, so CI uses an admin's personal access token ([ADR-0019](0019-personal-access-tokens.md)).
- **CLI.** `flare sourcemaps upload|list|delete`. Unlike other commands it accepts `--url` and
  `--token`/`FLARE_API_TOKEN`, since the usual caller is a CI job rather than the local stack.
- **Symbolication happens at read time**, in `POST /api/errors/occurrences`, not at ingest. The
  stored span is never rewritten, so a map uploaded after the error still applies, and a wrong or
  deleted map is harmless. Frames in V8 (`at fn (url:l:c)`) and Firefox/Safari (`fn@url:l:c`)
  form are rewritten to V8 form; other lines are untouched. Parsed maps are cached in-process by
  upload time (128 MB budget).
- **Function names** come from the map's `names` entry at the *next* frame's position (the call
  site names the callee), not the frame's own, because a frame's own position is usually an
  identifier inside the function rather than its name. A frame without one keeps the engine's
  name.
- `ExceptionOccurrence` gains `ServiceVersion` and `Symbolicated` (trailing members); the
  dashboard shows a "Source map applied" badge.

## Not decided here

A dashboard page for managing maps, `sourcesContent` inline snippets (the existing inline-source
feature fetches from the repo host), retention of old releases' maps, and symbolicating mobile
stacks (the same table works, but the frame parser and a Native AOT/trimmed mapping format do
not exist).

## Consequences

Maps are stored in the Identity DB, so Postgres-backed clusters hold them with the rest of the
configuration and large uploads add to its size; deleting old releases is manual. The occurrences
endpoint does extra work only for stacks with browser-style frames. A source-link "open in repo"
target for a symbolicated frame uses the map's source path (often `../../src/...`), which may
need the service's path prefix adjusted.
