# ADR-0210: Runtime settings and a dashboard toggle for managed PII redaction

Status: Accepted

Date: 2026-10-13

## Context

[ADR-0199](0199-managed-pii-redaction.md) made managed PII redaction a `PipelineRules__Pii__*` configuration, so turning
it on or off meant editing `Flare.Ingest`'s environment and restarting it. `Flare.Ingest` and `Flare.Api` are separate
processes with separate configuration, so the API cannot flip a flag in Ingest's memory, and Ingest had no runtime
settings mechanism.

## Decision

**A `runtime_settings` ClickHouse table that `Flare.Api` writes and `Flare.Ingest` polls, with the first key `pii.enabled`.**

- Migration `0085` (single and cluster). One row per key, a fixed `Id` per key, `ReplacingMergeTree(UpdatedAt)` with
  tombstones, read through `ORDER BY UpdatedAt DESC LIMIT 1 BY Id` rather than `FINAL` ([ADR-0074](0074-config-tables-latest-version-reads.md)).
  `ValueJson` holds the value, `UpdatedBy` the username. The table is generic so later switches can reuse it; only the
  PII key is wired.
- **Ingest polls it in `PipelineRuleCache`**, in the same loop and on the same `PipelineRules__RefreshInterval` (30 s) as
  the rules, as a separate query so one failure cannot freeze the other. `IPipelineRuleCache.PiiEnabledOverride` is the
  published value; the log annotator and `SpanFlushWorker` resolve it through `PiiRedactionOptions.EffectiveDetectors`.
- **Precedence: the override beats `PipelineRules__Pii__Enabled`; no row means follow configuration.** Existing
  deployments behave as before. `PipelineRules__Enabled=false` still disables everything. The detector list stays
  configuration-only.
- **Failure keeps the last known value.** A failed poll never reverts to configuration, because that could silently
  switch redaction off. A stored value that is not a JSON boolean reads as "no override".
- **API:** `GET /api/settings/pii-redaction` (Member), `PUT` and `DELETE` (Admin). `DELETE` writes a tombstone to return
  to "follow configuration". The API cannot see Ingest's configuration, so it reports only the override (`enabled` is
  null when none is saved), never an "effective" value. `PUT` and `DELETE` are in the audit allowlist
  (`pii-redaction-setting`, [ADR-0079](0079-audit-log.md)) with a before/after diff.
- **Dashboard:** a card on Settings > Pipeline rules with Follow configuration / On / Off, disabled for non-Admins.

## Consequences

- A change takes up to one refresh interval to reach each Ingest replica. After an Ingest restart there is a short
  window before the first poll completes in which only the configured value applies.
- Redaction is not retroactive; events stored before the switch keep their original values.
- Rejected: pushing changes over Redis pub/sub (a second moving part for a rarely flipped switch), and storing the
  setting in the Identity database (Ingest already reads ClickHouse for pipeline rules; the Identity store is for
  accounts and auth).
