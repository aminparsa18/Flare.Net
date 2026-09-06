# Roadmap

Forward-looking, still-open items only — no diary of what's already
shipped. See [`../README.md`](../README.md) for the rule this file exists
to enforce (a completed item is deleted here the same PR that ships it,
not checked off and kept); `git log` and the `adr`/`investigations`
folders are where "what happened and why" actually lives.

- **Retention policies + cold storage to S3-compatible object storage
  (RustFS).** A separate item from multi-node scaling (which shipped —
  see [`../adr/0003-distributed-tables-plain-names-and-sharding.md`](../adr/0003-distributed-tables-plain-names-and-sharding.md)
  and [`../../docs/explanation/clustering.md`](../../docs/explanation/clustering.md)):
  this one is retention/cold storage, not horizontal availability/
  throughput. Not started.
- **Research: a real "skip-index effectiveness" signal for the Indexing
  page.** Deliberately not shipped — ClickHouse doesn't expose this as
  reliable production telemetry today. Full findings, including upstream
  ClickHouse's own attempt at exactly this (merged then reverted for a
  correctness bug) and what to check before revisiting:
  [`../investigations/skip-index-effectiveness-signal.md`](../investigations/skip-index-effectiveness-signal.md).
  Until upstream lands something reliable, the fallback is a
  differently-labeled, genuinely-computable proxy (e.g. "% of queries
  reading under N% of their table's total rows" from `system.query_log`) —
  real, just not skip-index-specific, since primary-key pruning contributes
  too.
- **Dashboard i18n: remaining rollout phases (Phases 2-7).** Branch
  `i18n/dashboard-paraglide-en-zh-cn` brought up Paraglide JS (en +
  zh-CN) from scratch — Phase 0 (infra + app shell + login) and Phase 1
  (Logs Explorer) are done, committed, and verified; everything else in
  the dashboard is still 100% hardcoded English. Same mechanical pattern
  each time (extract strings to `messages/en.json`, hand-author the
  zh-CN translation, wire `m.*()` calls, `npm run check` + a grep sweep
  for stragglers to verify) — pick up wherever is next:
  - **Phase 2 — Alerts (`/alerts`).** `AlertHistorySheet.svelte`,
    `AlertRuleFormDialog.svelte` (the largest file here, ~330 lines —
    name/description/condition/threshold/channel form fields, the
    webhook/Telegram/email per-channel hint text, the dry-run test
    result badge), `AlertRuleTable.svelte`.
  - **Phase 3 — Traces, Metrics, Resources.** `src/lib/components/traces/`
    + `routes/traces/+page.svelte` + `routes/traces/[traceId]/+page.svelte`;
    `src/lib/components/metrics/`; `routes/resources/+page.svelte`. Note:
    `MetricsToolbar.svelte`/`TracesToolbar.svelte` already had their
    time-range-preset labels fixed during Phase 1 (a real bug — a static
    module-scope `.label` field can't reflect a per-request locale, see
    `src/lib/logs/time-range.ts`'s comment) — the rest of both files'
    strings are still untranslated.
  - **Phase 4 — Ingestion, Indexing, Saved Views.**
    `src/lib/components/ingestion/`, `src/lib/components/indexing/`,
    `src/lib/components/saved-views/`, plus their routes.
  - **Phase 5 — Auth settings (`/auth`, admin-only, lowest traffic).**
    `src/lib/components/auth/` + `routes/auth/+page.svelte`.
  - **Phase 6 — `src/lib/data-sources/catalog.ts`** (own PR — largest
    single-file effort, ~14 `GuideItem`s' prose). Leave `code.text`/
    `code.label` blocks untranslated (literal shell/YAML/env-var
    snippets — translating them breaks copy-paste). Also
    `routes/data-sources/+page.svelte`'s chrome text.
  - **Phase 7 — sweep for stragglers** across the whole `src/dashboard/src`
    tree (toast/error strings in `*-api.ts` catch blocks, `CommandPalette.svelte`,
    `TerminalModal.svelte`) once phases 2-6 land.
