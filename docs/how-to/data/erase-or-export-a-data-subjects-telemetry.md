# How to erase or export one person's telemetry

Retention removes telemetry by age. When a person asks for their data to be deleted (GDPR Art. 17) or handed over (Art. 15), you need to remove or export it by who it belongs to instead. Flare matches rows on attributes such as `user.id`, `user.email` or `client.address`, across logs, spans (including exception events), metrics, profiles, and the screenshots and feedback of the user's sessions. Only an Admin can do this.

You can do this in the dashboard under **Settings > Data erasure**, or with the `flare erasure` CLI and the `/api/erasure` API.

Flare can only find data that carries the identifier as an attribute. If your app never records `user.email`, searching for an email finds nothing.

## In the dashboard

1. Open **Settings**, then **Data erasure** under Workspace.
2. Enter one or more identifiers (attribute key and value), for example `user.id` = `42`. Choose **Add identifier** for more.
3. Choose **Preview** to see the matching rows per table, or **Export** to download them as NDJSON.
4. To delete, type the first identifier's value into the confirmation box and choose **Erase**. The button stays disabled until the preview matches the current identifiers.

The **Erasure requests** list below shows each request with per-table progress and refreshes while a delete is running.

The sections below show the same steps with the CLI.

## Preview what would be deleted

```bash
flare erasure preview --id user.id=42 --id user.email=jane@example.com
```

Each `--id KEY=VALUE` is one identifier of the same person. A row belongs to them if any identifier matches. Matching is exact equality on a resource, scope, log, span, data-point or span-event attribute. The command prints the number of matching rows per table.

Screenshots and feedback are matched through the session: Flare finds the `session.id` of every log and span that matches, then includes the screenshots and feedback recorded in those sessions. Feedback is also matched on its own user id and email fields. If more than 10,000 sessions match, the preview warns that the rest are not covered.

The same call is `POST /api/erasure/preview` with `{"identifiers": [{"key": "user.id", "value": "42"}]}`.

## Export their data

```bash
flare erasure export --id user.id=42 --output subject-42.ndjson
```

The export is newline-delimited JSON, one `{"table": "logs", "row": {...}}` line per row. A table is capped at 100,000 rows; a capped table ends with `{"table": "logs", "truncated": true}`, and you can narrow the request or export again. The API form is `POST /api/erasure/export`.

Export before you erase if the person asked for both.

## Erase their data

```bash
flare erasure run --id user.id=42 --id user.email=jane@example.com --confirm 42
```

`--confirm` must equal the first `--id` value, so a stray command cannot delete anything. Deletion is permanent.

The API accepts the request with `202` and a transaction id, then queues one ClickHouse `ALTER TABLE ... DELETE` per table that has matching rows. ClickHouse rewrites the affected data parts in the background, so on a large table it can take a long while. The CLI waits and prints progress by default; pass `--no-wait` to return at once and check later:

```bash
flare erasure status            # recent requests
flare erasure status <id>       # one request, per table
```

Each table shows `pending`, `running`, `success` or `failed`, and how many data parts the delete still has to rewrite. A request is `success` when every table's delete has finished. Only one request can be submitted at a time; a second one gets `409` until the first has queued its deletes.

## What stays

- **Rollups.** Service maps, SLOs, dashboards and other pre-aggregated tables hold counts and quantiles with no identifiers, so they are not changed.
- **Backups and archives.** Data already forwarded to another OTLP endpoint or archived to S3 is outside Flare. Delete it there.
- **Rows without the attribute.** A log line that mentions the person only in its message body is not matched. Search for it in Logs.
- **Rows ingested after the request.** Erasure deletes what exists when it runs. Stop the source from sending the identifier, or add a [pipeline rule](manage-pipeline-rules.md) to redact it.

## The audit trail

Every erasure and export is recorded in the audit log (Settings > Audit log) with who ran it, when, which attribute keys were matched and a short hash of the values. The values themselves are not stored, because they are the personal data. Requests stay in the erasure history for three years as evidence that the request was handled.

See [ADR-0198](../../../docs-internal/adr/0198-data-subject-erasure-export.md) for the design.
