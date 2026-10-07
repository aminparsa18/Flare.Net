# How to set data retention and move old data to cold storage

By default Flare keeps every log, span, metric and profile forever. Retention bounds disk use: each signal gets a lifetime, ClickHouse deletes rows older than that, and optionally moves aged data to cheaper object storage first. Only an Admin can change it; anyone signed in can read it.

## Set retention in the dashboard

1. Open **Settings**, then **Retention** under Workspace.
2. Each signal (logs, traces, metrics, profiles) has its own card. Under **Keep for (days)**, enter a number of days. `0` keeps the signal forever.
3. Choose **Apply**.

The card shows **In ClickHouse now**, read live from the database, and flags **Differs from last request** when it no longer matches what Flare last applied, for example after someone edited a TTL by hand. A TTL Flare didn't write reads as **Custom TTL**.

Applying is asynchronous. The card shows **Applying…** until ClickHouse has the new TTL in place, and only one change can run at a time. ClickHouse then deletes expired data during background merges, so disk frees up over hours, not at the moment the change succeeds. Shortening retention cannot be undone.

The rollups behind the Services map, SLOs and dashboards (`service_metrics` and similar) have no TTL, so they outlive the raw data they were computed from.

## Keep some resources longer or shorter

Under **Per-resource rules**, choose **Add rule** and enter a resource attribute, a value and a number of days, for example `deployment.environment` = `dev` for `7` days. Rows whose resource attribute equals the value use that lifetime. The first matching rule wins, and **Default (days)** covers everything else.

- Matching is exact equality on a resource attribute, not a prefix or pattern, and not on log or span attributes.
- A signal takes at most 20 rules.
- Rules apply to rows as they are written. Data already merged under an older rule set keeps the lifetime it was given.

## Move old data to cold storage

Cold storage is off by default. Start Flare with the cold-storage overlay to turn it on:

```bash
docker compose -f docker-compose.yml -f docker-compose.cold-storage.yml up -d
```

(`docker-compose.cold-storage.cluster.yml` is the cluster variant; with Aspire, call `AddFlare().WithColdStorage()`.) This adds a RustFS container as S3-compatible storage and a ClickHouse storage policy that uses it. The **Cold storage** card on the Retention page then lists the disks with their free space, and each signal gets a **Move to cold after (days)** field.

Set it lower than the signal's retention, or lower than the shortest of its rules, otherwise data would be deleted before it moves. `0` means no tiering. With retention `0` and cold-after set, data moves to cold storage and is never deleted.

Cold data stays in the same table, so searches, alerts and dashboards keep working; reads from cold storage are slower. Moves happen in the background, over minutes to hours. Turning tiering off later removes the move rule but leaves parts already in cold storage where they are.

## From the command line

```bash
flare retention show
flare retention set logs --days 30
flare retention set logs --days 30 --cold-after 7
flare retention set logs --days 30 --rule deployment.environment=dev:7 --rule k8s.namespace=load-test:1
flare retention set logs --clear-rules
```

`show` prints each signal's live TTL, cold-after, rules and the status of the last change. `set` waits until ClickHouse has applied the change (`--no-wait` returns once the API accepts it). Options you leave out keep their current value, so `--days 14` alone doesn't reset the rules. `--rule` takes `ATTRIBUTE=VALUE:DAYS`, is repeatable, and replaces the existing rules.

## Limits

- Days run from 0 (forever) to 18250 (50 years).
- A second change is rejected with `409` while one is still applying. An applying change that hasn't finished after 30 minutes, for example because the API restarted, is reported as failed.
- The API is `GET /api/retention` and `PUT /api/retention`. The design is in [ADR-0143](../../docs-internal/adr/0143-retention-ttl.md) (TTLs), [ADR-0144](../../docs-internal/adr/0144-cold-storage-rustfs.md) (cold storage) and [ADR-0145](../../docs-internal/adr/0145-per-resource-retention.md) (per-resource rules).
