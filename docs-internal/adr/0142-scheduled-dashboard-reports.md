# ADR-0142: Scheduled dashboard reports

Status: Accepted

Date: 2026-10-06

## Context

A dashboard can only be read by opening it. Teams want the weekly SLO or latency view in their
inbox without anyone logging in. That means three things Flare did not have: a schedule, a way to
turn a dashboard into a file, and a way to send it.

Dashboards are client-rendered. Panel queries run in the browser against the per-domain query
endpoints (ADR-0023), so there is no server-side representation of a finished dashboard to export.
Anything that produces a faithful file has to run the dashboard in a browser.

## Decision

- **Render with headless Chromium, driven by Playwright, in `Flare.AlertWorker`.** The worker
  already owns scheduled work, shares the Redis lock pattern, and already has the SMTP settings
  alert emails use. `PlaywrightDashboardRenderer` opens the real dashboard route, so a report looks
  like the dashboard and never drifts from it. The alternative, re-implementing every panel type
  server-side, would be a second renderer to keep in step with the first.
- **Chromium is not in the default worker image.** It adds a few hundred MB, and most installs
  never schedule a report. `INSTALL_CHROMIUM=true` (compose: `FLARE_REPORTS_CHROMIUM`) adds Playwright's own
  Chromium (Ubuntu's apt `chromium` is only a snap stub); `Reports:ChromiumPath` points at any other, and `Reports:BrowserWsEndpoint`
  connects to a Playwright server instead. The feature is off until `Reports:Enabled`.
- **A schedule is a config table row** (`dashboard_schedules`, migration 0066, ADR-0009 and
  ADR-0074 conventions): dashboard, name, 5-field cron, IANA time zone, recipients, relative time
  range (a `?range=` preset), variable values (the dashboard's own `var-<id>=...` query string),
  format (`pdf` or `png`), owner, and `NextRunAt`. Range and variables reuse the URL state the
  dashboard already reads (`$lib/dashboards/url-state.ts`), so a schedule is "this URL, on a timer".
  Cron is parsed by Cronos; a cron that never fires again is parked at the year 2100.
- **Run history is `dashboard_report_runs`**, append-only, one row per attempt with status, duration,
  size and the error text, kept 90 days. A failed render or send is visible in the schedule's
  history instead of only in the worker log.
- **Report mode.** `/dashboards/<id>?report=1` hides the app chrome and toolbar, expands every row,
  loads every panel at once (a headless browser never scrolls, and panels lazy-load on scroll,
  ADR-0023 follow-ups), and sets `<html data-report-ready>` when the dashboard has loaded. The
  renderer then waits for the network to go quiet plus `Reports:SettleDelay`, grows the viewport to
  the tallest scroll container, waits again, and captures one tall page (PDF with a page the size of
  the dashboard, or a full-page PNG), so panels are never split across page breaks.
- **Auth is a short-lived signed render token, not a stored credential.** The worker mints
  `flr_render_...` (ASP.NET Core Data Protection, purpose `Flare.DashboardRender.v1`, lifetime
  `Reports:TokenLifetime`, default 10 minutes) carrying the schedule owner's user id and the dashboard
  id, and hands it to the browser as the session cookie for the API host. `SessionAuthenticationHandler`
  recognises the prefix and signs in that user, **always as a Viewer**, with a
  `flare:render_dashboard_id` claim. This is the ack-link mechanism (ADR-0127) again: the worker and
  API already share a Data Protection key ring through Redis, so there is no new secret, no service
  account per schedule, and nothing to rotate. The token never leaves the worker's browser.
- **The schedule runs as its creator.** A render sees what the creator sees, project scoping included
  (ADR-0123), and stops working if that user is disabled. Creating, editing, deleting and sending now
  need Member or Admin (a schedule makes the server email arbitrary addresses) plus write access to the
  dashboard's project; changing or deleting someone else's schedule needs Admin.
- **Claim, then render: at most once.** Each tick takes a short Redis lock, lists due schedules,
  moves each `NextRunAt` to its next cron occurrence, and releases the lock. Rendering happens
  afterwards, outside the lock, because it takes seconds to minutes. A second replica or a restart in
  the middle of a render cannot send a report twice; the price is that a render lost with the process is
  not retried and leaves no run row. "Send now" is the same mechanism: it sets `NextRunAt` to the current
  time and the worker picks it up within `Reports:PollInterval` (30 s).
- **Mail goes through the existing `Email:*` SMTP settings** as a plain-text message with the file
  attached and a link back to the dashboard (without `report=1`). A render over
  `Reports:MaxAttachmentBytes` (20 MB) is recorded as a failure, with advice, instead of bouncing at the
  mail server.

## Consequences

- A new optional runtime dependency (Chromium) for anyone who turns reports on. The worker image stays
  small by default; the compose and Dockerfile switches are documented in the how-to.
- In Docker the browser inside the worker must reach the dashboard and API at the URLs users' browsers
  use (the SPA calls `PUBLIC_API_URL`, and the cookie is scoped to that host). Compose points
  `Reports:DashboardUrl` / `ApiUrl` at the published `localhost` ports and adds a Chromium
  `--host-resolver-rules=MAP localhost host.docker.internal` switch so those resolve to the Docker host.
  Deployments behind a reverse proxy use the public URLs and need no mapping.
- A report is a picture of the dashboard at one moment, not a data export. A panel that is still
  loading at capture time shows its loading state; raise `Reports:SettleDelay` for slow dashboards.
- The render token is accepted on every API route, as a Viewer, for its lifetime. A Viewer cannot
  mutate, but it can query anything the owner can read. The token is minted per render, lives in a
  throwaway browser context, and expires in minutes; that is the same exposure as the ack link's
  bearer-credential trade-off (ADR-0127) with a shorter life.
- `dashboard_schedules` and `dashboard_report_runs` get cluster variants (`_local` plus `Distributed`),
  like the other config and history tables.

## Not decided here

- **Multi-page PDFs and per-panel layout.** A report is one tall page. A paginated, print-styled layout
  is a larger design.
- **Other destinations.** Slack, Teams and webhook delivery of the file, and notification-channel based
  recipients instead of a plain address list. Email only for now.
- **Retrying a failed run.** A failure is recorded and the next cron tick runs normally.
- **Not exercised end to end:** `Reports:BrowserWsEndpoint` (connecting to a Playwright server). The
  local-Chromium path is what has been run.
