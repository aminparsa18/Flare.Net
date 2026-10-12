# How to see what is driving storage

Use the **Usage** page to see which services, ingest keys and attributes account for the most stored data, before you choose sampling or [retention](set-data-retention.md) rules.

## Open the page

Open **Settings > Workspace > Usage** (admin only), or go to `/settings/usage`. Pick a window of 1, 7 or 30 days.

## What it shows

| Section | Meaning |
|---|---|
| Signal tiles | Stored events per signal in the window, and each signal's current compressed size on disk |
| Volume by service | Events and events per day per service (top 50 per signal), with an estimated on-disk size |
| Ingest keys | Events and bytes each active key was accepted for in the current UTC day |
| Largest attributes | Attribute keys ranked by key plus value bytes in a sample of recent rows |

## Reading the numbers

- **Estimated on disk** is the signal's compressed table size split by the service's share of events in the window. ClickHouse keeps no per-service size, so use it to rank services, not to bill them.
- **Ingest keys** cover the current UTC day only, because that is all the per-key counters in Redis keep.
- **Largest attributes** samples up to 100,000 rows from the last day per source and reports uncompressed bytes. Share is within the same source.
- Trace-sampled spans count once, as stored.

## Act on it

A service dominating a signal is a candidate for a shorter [retention](set-data-retention.md) rule or trace sampling. A large attribute that is rarely queried can be dropped with a [pipeline rule](manage-pipeline-rules.md).
