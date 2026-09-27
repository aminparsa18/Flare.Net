# ADR-0064: "Resolved" alert notifications

Status: Accepted

Date: 2026-09-27

## Context

Alerting only ever sent a notification when a rule fired.
`AlertEvaluationWorker` checked a breach against the rule's cooldown and
notified. When the condition recovered, nothing was sent, and there was no
per-rule firing/ok state at all. On-call heard "it broke" but never "it's
fixed". PagerDuty incidents stayed open until someone closed them by hand,
because every trigger was sent without a `dedup_key` that a later resolve
could target. Prior art:
[signoz#7240](https://github.com/SigNoz/signoz/commit/8abba261a86692d5331e0cce283df572259193d8)
added a per-channel `send_resolved` setting.

Rules evaluate one aggregate series, not one series per group (see
ADR-0052's `{{labels.*}}` note), so there is exactly one firing/ok state
per rule.

## Decision

- **Firing/ok state comes from `alert_events` itself.** Migration
  `0033_alert_resolved_notifications.sql` adds `alert_events.Resolved`
  (`UInt8`, default 0). A resolution is recorded as another history row
  with `Resolved = 1`. A rule is firing while its latest fire is newer than
  its latest resolution. `IAlertQueryService.GetFiringStatesAsync` reads
  that for every due rule in one `GROUP BY RuleId` query per tick, using
  `maxIfOrNull` over fires, notified fires and resolutions. The query also
  reports whether any fire since the last resolution actually notified,
  rather than being a maintenance-window `Suppressed` row. There is no
  separate state table, so no second copy of the state can drift from the
  history it summarizes.
- **Firing→ok is a real "not breached" evaluation.** When a firing rule
  evaluates as not breached, the worker resolves it. An evaluation that
  can't decide never resolves: insufficient data (ADR-0050), a
  misconfigured rule, or a failed query. For a no-data rule, data coming
  back with the threshold not breached is a recovery. There is no extra
  hysteresis. The existing cooldown already caps a flapping rule at one fire
  per cooldown period. After a resolution, a new breach inside that cooldown
  waits for the cooldown to end, and then fires as a new incident.
- **The pure decision lives in `AlertResolutionPolicy.Decide`:**
  - Not firing: do nothing.
  - Every fire since the last resolution was suppressed: record the
    resolution with `NotificationStatus = 'Skipped'` and send nothing.
    Nobody was paged, but the rule should still read as ok.
  - Someone was paged and a maintenance window covering the rule is
    active: defer. Nothing is recorded, and the first ok evaluation after
    the window ends sends the resolution. A window silences every
    notification while it's active. Recording the resolution silently
    instead would leave a paged PagerDuty incident open for good. Deferring
    matches how a breach that outlasts a window already notifies as soon as
    the window ends (ADR-0055).
  - Otherwise: notify.
- **Per-channel opt-out.** `NotificationChannel.SendResolved` is a new
  column on `notification_channels`, `UInt8`, default 1. The worker sends
  the resolution only to the rule's channels that have it set. A legacy
  inline channel always has it set. If no channel is left, the resolution
  is still recorded, as `Skipped`. It defaults to on because a resolution is
  what most on-call setups expect, and the roadmap item asked for an
  opt-out.
- **The notifiers take a `resolved` flag**, next to `noData`/`anomaly`:
  - The built-in text is ":white_check_mark: Alert "X" resolved: 3 events
    (threshold >= 10) in the last 300s". It uses the same terms as the fired
    text, so the pair reads as one incident.
  - A custom title/body template still renders, prefixed with `[Resolved] `
    the way a test send gets `[Test] `. A template written for the firing
    case ("checkout is down") therefore can't read as a new incident.
    `{{status}}` is `resolved` for templates that want to word it
    themselves.
  - Email's built-in subject is "Flare alert resolved: X".
  - The webhook payload gains `status: "firing" | "resolved"`, which is
    Alertmanager's vocabulary.
- **PagerDuty uses a per-rule `dedup_key`.** A real trigger now carries
  `dedup_key = flare-alert-{ruleId:N}`. A resolution sends
  `event_action: "resolve"` with the same key, which closes the incident.
  A test send gets a one-off key, so it never merges into a real incident.

## Alternatives considered

- **A separate `alert_state` table (or Redis key) holding firing/ok.** This
  is cheaper to read, but it's a second copy of state the history already
  implies, and it needs its own migration path for rules that fired before
  it existed. Redis in particular isn't durable storage for incident state
  (ADR-0046 used Redis only for "when did I last evaluate", which is safe to
  lose). Deriving the state from `alert_events` costs one small indexed
  query per tick.
- **Resolving during a maintenance window.** This would close incidents
  sooner, but it breaks the rule that a window means "nothing is sent". It
  would also make a resolved notification the only kind that pierces a
  window.
- **Recording a silent `Suppressed` resolution during a window.** Rejected.
  After that the rule reads as ok, so no resolution is ever sent and a paged
  PagerDuty incident stays open.
- **Recovery hysteresis ("ok for N consecutive evaluations").** Rejected for
  now. Cooldown already limits a flapping rule's noise, and most rules
  evaluate over a rolling window that smooths single-tick blips. It could be
  a per-rule field later.
- **Opt-in `sendResolved`.** This would keep existing channels silent on
  recovery, but the roadmap asked for opt-out, and a resolved message is
  the expected default in PagerDuty/Alertmanager-style setups.

## Consequences

- `alert_events` gains `Resolved`, and `AlertHistoryEntry` appends
  `Resolved`. `notification_channels` gains `SendResolved`, and
  `NotificationChannel` appends `SendResolved`. `NotificationChannelRequest`
  has a nullable `SendResolved`, where null means true. The hand-written
  `$lib/memorypack/AlertHistoryEntry.ts` and `NotificationChannel.ts` append
  the fields, so older payloads still deserialize.
- `NotificationStatus` has a fourth value, `Skipped`, used only on
  resolution rows.
- `GetLastFiredAsync`, the cooldown query, ignores resolution rows, so a
  resolution doesn't restart the cooldown.
- **Behavior change for PagerDuty:** while a rule's incident is open, a
  re-fire after cooldown now adds to that incident instead of opening a new
  one.
- **Behavior change for every existing channel:** it now receives resolved
  notifications, until someone turns `SendResolved` off.
- A resolution whose sends fail is still recorded, with status `Failed`, and
  the rule reads as ok. It isn't retried, which is the same as a failed
  fire.
- A rule that is disabled or deleted while firing is no longer evaluated,
  so it never sends a resolution. A disabled rule that is re-enabled
  resolves on its first ok evaluation.
- `flare notification-channels create/update` take `--send-resolved`.
  `update` always sends the existing value, because `PUT` replaces the
  whole channel and would otherwise reset the setting to true.
