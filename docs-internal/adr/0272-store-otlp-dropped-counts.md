# ADR-0272: Store OTLP dropped-item counts

Status: Accepted

Date: 2026-10-22

## Context

An OpenTelemetry SDK enforces count limits (`OTEL_SPAN_ATTRIBUTE_COUNT_LIMIT`, `OTEL_SPAN_EVENT_COUNT_LIMIT`, `OTEL_SPAN_LINK_COUNT_LIMIT`, `OTEL_LOGRECORD_ATTRIBUTE_COUNT_LIMIT`, each defaulting to `OTEL_ATTRIBUTE_COUNT_LIMIT`, 128). When a span or log record exceeds one, the SDK discards the surplus and reports how many it discarded: `dropped_attributes_count` on spans and log records, `dropped_events_count` and `dropped_links_count` on spans.

Flare mapped none of these. A span that lost half its attributes looked complete, and nothing in the UI hinted that raising a limit would fix a missing attribute. The .NET SDK 1.20 changed which variable caps event, link and scope attributes, so hitting a limit unnoticed got easier.

## Decision

- **Three `UInt32 DEFAULT 0` columns on `spans`** (`DroppedAttributesCount`, `DroppedEventsCount`, `DroppedLinksCount`) **and one on `logs`** (`DroppedAttributesCount`), added by migration 0091 in both `db/clickhouse` and `db/clickhouse-cluster` (`_local` and Distributed tables).
- **Stored per row, not aggregated.** They are not in any `ORDER BY`, projection or materialized view; they are read back with the row. Rows from before the migration, and data from SDKs with no limit hit, read 0.
- **Appended last on every wire type** (`SpanRecord`, `LogEvent`, `BufferedLogEvent`, `SpanDto`, `LogEventDto`, and the hand-written TypeScript decoders). MemoryPack is order-based, so an append keeps payloads buffered in Redis and responses from an older server decodable; the missing members read as 0.
- **Shown only on the detail views.** The span sheet and the log event detail render a warning badge ("Dropped attributes: N", and for spans "Dropped events" and "Dropped links") when a count is non-zero, with a tooltip naming the `OTEL_*_COUNT_LIMIT` settings. Nothing renders when all are 0. The label is "Dropped attributes: N" rather than "N attributes dropped" because it needs no plural forms in English, Russian or Chinese.
- **Scope and metric exemplar attributes are out of scope.** Scope `dropped_attributes_count` is on the scope message, not per record, and exemplars already keep their filtered attributes.

## Consequences

- Four extra 4-byte columns, compressed with ZSTD(1); almost always 0, so close to free on disk.
- Search, filters and alerts cannot use the counts. If "show me every span that dropped attributes" is wanted later it needs a filter field, and probably a skip index, not just these columns.
- The counts are the SDK's own claim. Flare does not cross-check them against the attributes it received.
- Not built: CLI and MCP exposure (`LogWire`, span wire types), a Services-level "services dropping data" rollup.
