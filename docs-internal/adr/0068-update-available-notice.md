# ADR-0068: "New version available" notice

Status: Accepted

Date: 2026-09-27

## Context

Self-hosted users got no signal that a newer Flare was out. The roadmap
asked for `Flare.Api` to check the latest GitHub release (cached, with an
opt-out for air-gapped installs) and for the dashboard to show a dismissible
notice with the release notes. Prior art:
[signoz#8270](https://github.com/SigNoz/signoz/commit/3b1bf34d3e8faf850eb551d62914c85569d3a468).

Two facts shaped the design. First, the images didn't know their own version:
every build reported the SDK default `1.0.0`. Second, the repository had
`v*.*.*` tags but no published GitHub Releases, so `/releases/latest` returned
404.

## Decision

- **The API image carries its version.** `src/Flare.Api/Dockerfile` takes a
  `FLARE_VERSION` build arg and publishes with
  `-p:InformationalVersion=$FLARE_VERSION`. `docker-publish.yml` passes
  `docker/metadata-action`'s version, so a `v0.6.0` tag builds `0.6.0` and a
  main push builds `edge`. `Flare.Api.csproj` defaults to `dev`, so local and
  Aspire builds never pass for a release.
- **Only a real version is checked.** `dev` and `edge` don't parse as
  SemVer, so `ReleaseCheckService` skips the GitHub call and reports no
  update. Local dev loops never call out.
- **Server-side, lazy, cached.** `GET /api/version` (authenticated) looks up
  GitHub only when asked and at most once per `UpdateCheck:Interval`
  (default 24h). One lookup runs at a time. A failure is remembered for an
  hour, so an unreachable GitHub isn't retried on every page load. An idle
  instance makes no calls. The browser never talks to GitHub, so a locked-
  down client network doesn't matter and the dashboard needs no CSP change.
- **Opt-out, not opt-in.** `UpdateCheck__Enabled=false` turns it off. It's on
  by default because the notice is only useful if people see it; it sends
  nothing but a `User-Agent: Flare/<version>` request to the public API.
- **Source: `/releases/latest`, falling back to tags.** The release endpoint
  skips drafts and prereleases and carries the notes. On 404 the check picks
  the highest stable `vX.Y.Z` tag from `/tags` (other tag families such as
  `flare-cli-v*` don't parse), which gives a version and link without notes.
  `docker-publish.yml` now creates the GitHub Release, with generated notes,
  after a tag's images are pushed, so future releases carry notes.
- **Dashboard.** A strip under the nav bar says a newer version exists and
  opens a dialog with the notes; dismissing it stores that release's version
  in `localStorage`, so the next release shows again. The user menu always
  shows the running version, plus an "Update available" item after
  dismissal. Notes are shown as Markdown source, not rendered, to avoid adding
  a Markdown renderer and sanitizer for one dialog.

## Consequences

- One outbound HTTPS call per day at most, per `Flare.Api` instance, to
  `api.github.com`. Unauthenticated GitHub requests are limited to 60 per hour
  per IP, far above this.
- The notice compares `Flare.Api`'s version only. All images are built from
  the same tag, so that's the stack's version, unless someone pins the images
  to different tags.
- Aspire (`Flare.Hosting.Aspire`) and `flare` CLI users see the notice too,
  since both run the same `flare-api` image.
- A fork that publishes its own images sets `UpdateCheck__Repository`.
