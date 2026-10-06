# How to email a dashboard on a schedule

Flare can render a dashboard to a PDF or PNG on a schedule and email it, for example a weekly SLO and latency report to a team. Each schedule has a cron cadence, recipients, a relative time range and optional variable values. Every attempt is recorded, with its error if it failed.

Reports are rendered by a headless Chromium inside `Flare.AlertWorker`, which opens the real dashboard, so a report looks exactly like the dashboard does. They are off until you turn them on, because the worker needs a Chromium and an SMTP server.

## Turn reports on

You need an SMTP server (the `SMTP_*` values in `.env`, or `Email__*` settings on the worker, which alert emails use too) and a Chromium for the worker.

**Docker Compose.** Add two lines to `.env`, then rebuild the worker so its image includes Chromium:

```bash
FLARE_REPORTS_CHROMIUM=true
FLARE_REPORTS_ENABLED=true
```

```bash
docker compose up -d --build alert-worker
```

**Any other install.** Set these on the alert worker:

| Setting | Meaning |
| --- | --- |
| `Reports__Enabled` | `true` to run schedules. Default `false`. |
| `Reports__DashboardUrl` | The dashboard's URL as the browser reaches it. Falls back to `Alerting__PublicUrl`. |
| `Reports__ApiUrl` | The API's URL as the dashboard reaches it, the same value as the dashboard's `PUBLIC_API_URL`. |
| `Reports__ChromiumPath` | A Chromium or Chrome executable. The Compose image needs none: it carries Playwright's own Chromium. |
| `Reports__BrowserWsEndpoint` | A Playwright server to connect to instead of launching a local Chromium. |
| `Reports__PollInterval` | How often due schedules are looked for. Default 30 seconds. |
| `Reports__RenderTimeout` | Longest one render may take. Default 2 minutes. |
| `Reports__SettleDelay` | Extra wait for charts to finish drawing after the page goes quiet. Default 3 seconds. |
| `Reports__MaxAttachmentBytes` | Largest file sent. Default 20 MB. |

## Create a schedule

1. Open the dashboard and choose the **Scheduled reports** button (the calendar icon) in its toolbar.
2. Choose **New schedule**.
3. Give it a name, pick a cadence (or type a five-field cron expression such as `0 8 * * 1`), a time zone and the recipients.
4. Pick a time range and a format. **Use the current view** copies the time range and variable selections you have on screen.
5. Save. The schedule shows its next run.

A schedule runs with your access: the report contains what you can see. If your account is disabled the schedule starts failing. Creating, editing and deleting schedules needs the Member or Admin role.

## Test it and read the history

**Send now** queues the schedule, and the worker sends it within about a minute. **Run history** lists each attempt with its status, how long it took, the file size, and the error text for a failure. The usual ones:

- *SMTP is not configured*: set `Email__Host` and `Email__From` on the worker.
- *No Chromium to render with*: build the worker image with Chromium or set `Reports__ChromiumPath`.
- *Rendering took longer than ...*: raise `Reports__RenderTimeout`, or shorten the time range.
- *The rendered report is ... MB*: use a shorter range or fewer panels, or raise `Reports__MaxAttachmentBytes` if your mail server allows it.

## Limits

- A report is one tall page, capped at 16,000 pixels. A longer dashboard is cut there.
- A report is a picture of the dashboard at that moment, not a data export. If a panel still shows a spinner in the file, raise `Reports__SettleDelay`.
- A render that is lost because the worker restarted is not retried. The next cron tick runs normally.
- Email is the only destination.
