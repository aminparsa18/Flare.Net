# How to monitor cron jobs and scheduled tasks with check-ins

A job that crashes writes an error you can alert on. A job that never starts writes nothing: the scheduler died, the CronJob was deleted, the host was down at 02:00. A **check-in monitor** covers that case. The job calls a Flare URL when it starts and finishes, and Flare alerts when a call is late or reports failure.

## Create a monitor

Open **Settings > Workspace > Cron and job check-ins** and choose **New check-in monitor**, or use the API (`POST /api/checkin-monitors`, Member or Admin) with a session cookie or a personal access token:

```bash
curl -X POST "$FLARE_API/api/checkin-monitors" \
  -H "Authorization: Bearer $FLARE_PAT" -H "Content-Type: application/json" \
  -d '{"name":"Nightly backup","cronExpression":"0 2 * * *","timeZone":"Europe/Berlin","graceSeconds":600,"maxRuntimeSeconds":3600}'
```

| Field | Meaning |
| --- | --- |
| `name` | Required. |
| `slug` | The name in the ping URL: lower-case letters, digits, `-` and `_`, up to 64 characters. Derived from the name when omitted, and fixed after creation. |
| `cronExpression` | A five-field cron expression (minute hour day-of-month month day-of-week) or a macro such as `@daily`, read in `timeZone`. Set this or `intervalSeconds`. |
| `timeZone` | An IANA name such as `UTC` (the default) or `Europe/Berlin`. Used with `cronExpression` only. |
| `intervalSeconds` | The other kind of schedule: a ping is expected every this many seconds, 60 to 2678400. It is measured from the last ping. |
| `graceSeconds` | How long after the expected time a ping may arrive before the job counts as late. 300 by default. |
| `maxRuntimeSeconds` | Optional. A run that has started and not finished within this time counts as down. 0 (default) means no limit. |
| `enabled` | `false` pauses the monitor. |

`GET`, `PUT` and `DELETE` on `/api/checkin-monitors/{id}` read, change and remove one. Everyone signed in can list monitors; creating, changing and deleting need the Member or Admin role.

From a terminal, `flare checkin-monitors create "Nightly backup" --cron "0 2 * * *" --timezone Europe/Berlin --grace 600 --max-runtime 3600` does the same against a local Flare, and `list`, `update` and `delete` manage the rest; `list` shows each monitor's state and its last and next expected ping. See the [CLI reference](../../reference/cli-commands.md).

With the Flare Terraform / OpenTofu provider, a monitor is a `flare_checkin_monitor` resource (set `cron_expression` or `interval_seconds`, not both):

```hcl
resource "flare_checkin_monitor" "nightly_backup" {
  name            = "Nightly backup"
  cron_expression = "0 2 * * *"
  time_zone       = "Europe/Berlin"
  grace_seconds   = 600
}
```

## Send check-ins from the job

The job calls `Flare.Ingest` on the HTTP port (4318 by default) with an [ingest key](../admin/configure-authentication.md), the same credential your OpenTelemetry exporters use. A bare path is a success; `/start` and `/fail` are the other two:

```bash
FLARE="https://ingest.example.com"
AUTH="Authorization: Bearer $FLARE_INGEST_KEY"

curl -fsS -m 10 -H "$AUTH" "$FLARE/v1/checkins/nightly-backup/start"
./backup.sh && curl -fsS -m 10 -H "$AUTH" "$FLARE/v1/checkins/nightly-backup" \
           || curl -fsS -m 10 -H "$AUTH" --data "backup.sh exited $?" "$FLARE/v1/checkins/nightly-backup/fail"
```

| Path | Meaning |
| --- | --- |
| `/v1/checkins/{slug}` or `/v1/checkins/{slug}/ok` | The job finished successfully. |
| `/v1/checkins/{slug}/start` | The job started. Optional; send it to catch a job that starts and hangs. |
| `/v1/checkins/{slug}/fail` | The job finished and failed. |

Both `GET` and `POST` work. A `POST` body of up to 4 KiB is kept as a message (the first 1,000 characters). The endpoint answers `204`, `400` for a bad slug or status, `401` for a missing or wrong ingest key, and `503` if the ping could not be stored, so `curl -f` fails loudly. A ping for a slug with no monitor is accepted and ignored, unless it asks Flare to create the monitor (next section).

The monitors table has a **Copy ping command** button on each row.

## Create the monitor from the first ping

A job can create its own monitor instead of someone creating it first. Add `create=1` and a schedule to the ping URL:

```bash
curl -fsS -m 10 -H "$AUTH" "$FLARE/v1/checkins/nightly-backup/start?create=1&cron=0%202%20*%20*%20*&tz=Europe/Berlin&grace=600&name=Nightly%20backup"
```

| Parameter | Meaning |
| --- | --- |
| `create` | `1` or `true` asks for the monitor. Without it the other parameters are ignored. |
| `cron` or `interval` | The schedule, exactly one of them, as in the table above. URL-encode the spaces in a cron expression. |
| `tz` | The IANA zone for `cron`. `UTC` by default. |
| `grace`, `max_runtime` | Seconds, as `graceSeconds` and `maxRuntimeSeconds`. 300 and 0 by default. |
| `name` | The display name. The slug by default. |

The monitor is created once, from the first such ping, and the ping is stored as usual. If a monitor with that slug already exists the parameters are ignored, so later pings never change a schedule: edit it in the dashboard, the API or Terraform. An invalid schedule answers `400` and stores nothing. Leave `create=1` on the job's pings or remove it after the first run; both work.

Auto-create can be turned off with `CheckIns__AutoCreate=false` on `Flare.Ingest` (such pings then answer `403`), and `CheckIns__MaxMonitors` (500 by default) caps how many monitors exist, so a leaked ingest key cannot fill the table. Without `create=1`, a typo in a slug still creates nothing.

## How the state is decided

The next ping is expected at the first scheduled time after the last ping. Each monitor is in one of these states:

| State | When | `checkin.up` |
| --- | --- | --- |
| Waiting for first check-in | No ping yet and the first one is not overdue. | 1 |
| On schedule | The last run finished and the next one is not overdue. | 1 |
| Running | A run started and has not finished, within `maxRuntimeSeconds` if set. | 1 |
| Late | The next ping did not arrive within the grace period. | 0 |
| Failed | The last run reported `fail`. It stays failed until a run reports success. | 0 |
| Running too long | A run started and did not finish within `maxRuntimeSeconds`. | 0 |
| Paused | The monitor is disabled. It writes no metric. | none |

Saving a monitor restarts its clock: the first ping is expected after the schedule's next occurrence from that moment, so editing or re-enabling a monitor never reports it late at once. A job that starts but never finishes, with no `maxRuntimeSeconds`, is reported late once its next scheduled time plus the grace period passes.

## See a monitor's runs

The history button on a monitor's row lists its recent runs, newest first: when each started, how long it took, whether it succeeded, failed or never finished, and the message it sent. `GET /api/checkin-monitors/{id}/runs?limit=50` returns the same list (up to 200).

Runs are paired from the pings you already send: a finish ping (`ok` or `fail`) closes the latest start before it, and a second `start` while one is open leaves the first one marked **Never finished**. A job that sends only a finish ping still shows up, without a duration. Pings carry no run id, so two overlapping runs of the same job can be paired wrongly.

## Alert on a monitor

Every 15 seconds `Flare.AlertWorker` writes a gauge `checkin.up` (service `flare-checkin`, attribute `monitor` set to the slug). Alerting on it is an ordinary metric alert: **Last** below 1, filtered by `monitor`. The bell button on a monitor's row opens the alert form with exactly that filled in, so you only pick a notification channel.

To be told when the monitor itself stops reporting (for example the worker is down), add **Absent data** to the rule.

## Limits

- Any valid ingest key can ping any slug, and (unless you turn it off) create a monitor with `create=1`. Give jobs their own key rather than reusing a browser key.
- The grace period should cover clock skew, and for an interval schedule also the job's run time, because the interval is measured from the last ping.
- A late job is reported up to `CheckIn__PollInterval` (15 s) after its grace period ends, so use alert windows longer than that.
- Set `CheckIn__Enabled=false` on the worker to turn judging off. Pings are still stored.
- Pings are kept for one year.
