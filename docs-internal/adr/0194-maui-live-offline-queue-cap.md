# ADR-0194: MAUI live offline queue cap

Status: Accepted

Date: 2026-10-12

## Context

[ADR-0177](0177-maui-before-send-and-queue-limits.md) bounded the disk-retry directory with `OfflineQueueMaxAge` and
`OfflineQueueMaxBytes`, but only at startup. A long session on a device that stays offline kept writing batches and
could grow the queue past the cap until the next launch.

## Decision

- **Re-apply the same trim on a timer.** `UseFlare` starts a `System.Threading.Timer` that runs `OfflineQueue.Prune`
  every `OfflineQueueTrimInterval` (default 1 minute, must be positive). The startup trim is unchanged and still runs
  before the exporter is created.
- **No file watcher and no hook into the exporter.** The exporter owns the directory and its writes are not
  observable, so the cap is a bound on how long the queue can stay over, not a hard limit. Between trims it can exceed
  `OfflineQueueMaxBytes` by whatever the exporter writes in one interval.
- **A trim that races the exporter is harmless.** `Prune` already ignores files that are in use or already gone, and it
  deletes oldest-first, so the batch being written is the last candidate.
- The timer is disposed by `Shutdown` (tests); apps leave it running until exit.

## Not decided here

A size-triggered trim (for example after a failed export) in place of the interval.
