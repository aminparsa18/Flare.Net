> This is the full documentation architecture and migration plan produced
> during Phase 0 (inventory) and executed across phases 1–10. It moved
> here from `docs/DOCUMENTATION-MIGRATION-PLAN.md` as part of Phase 10 —
> it was never a Diátaxis document (not a tutorial/how-to/reference/
> explanation), so it doesn't belong under `docs/` any more than
> `roadmap.md` does.
>
> Migration status: **Phase 10 done.** See `../README.md` for the
> governance rules this plan established, `../adr/` for the 13 ADRs, and
> `../investigations/` for the 5 investigations. Phases 1–9 migrated every
> user-facing doc (`docs/clustering.md`, `docs/cli.md`, `docs/auth.md`,
> `docs/standalone.md`, `docs/aspire-hosting.md`, `docs/getting-started.md`,
> `docs/benchmark.md`) into the Diátaxis split, each old path kept as a
> redirect stub; extracted every ADR and investigation whose source lived
> in `Planning.md` or `db/clickhouse/README.md`. **Phase 10 pruned
> `Planning.md` itself** — completed `docs/explanation/architecture.md`
> with the design-principles/pipeline/non-goals content from Planning.md's
> intro (plus 2 more ADRs, 0012–0013, for the OTLP-only and
> ClickHouse-as-storage-engine decisions that intro named), wrote
> `roadmap.md` from the handful of genuinely still-open items, and reduced
> `Planning.md` to a redirect stub — same pattern as every other migrated
> doc, warranted even more here given ~60 source files reference
> `Planning.md` by name. Phase 11 (`CONTRIBUTING.md` + validation tooling)
> is not started yet.

# Flare.Net Documentation Architecture & Migration Plan

## 1. Executive summary

Flare has one architecturally-real problem, not a Markdown-quality problem: **there is exactly one place engineering knowledge gets written down — [Planning.md](../../Planning.md) — and it has been asked to be four documents at once**: a product pitch, an architecture explanation, a decision log, and a change diary. It has grown to 3,155 lines / 250KB, with **148 checked-off roadmap items** (only a handful still open), because "done, so record what happened" has been the default move for eight months of `v1`→`v21`. `docs/` is smaller but has the same disease at file scale: `docs/auth.md` (645 lines) and `docs/cli.md` (274 lines) each interleave *how it works* (explanation), *do this* (how-to), *exact values* (reference), and *known limitations* (part explanation, part unfinished investigation) in the same document with no signal to a reader — or an AI agent about to edit it — about which mode they're in.

Three consequences fall out of that:
1. **Duplication is already happening.** Planning.md's v21 entry says "Full write-up in `docs/aspire-hosting.md`'s Kubernetes section" — the same Kubernetes-provider decision and its three field-tested bugs exist as diary text in Planning.md *and* as doc text in `docs/aspire-hosting.md`, with no rule for which one a future edit should update.
2. **Real ADR-grade decisions are unfindable.** Genuinely significant calls — OTLP-only ingestion, Redis Streams for durable buffering, SvelteKit over Blazor, `Distributed` tables keeping plain names, JSON vs. Map for structured attributes — are buried mid-paragraph inside checklist items or a five-line "Open questions" section and a "Design decisions" heading inside a *project* README (`db/clickhouse/README.md`), not in one predictable place.
3. **Nothing stops it from recurring.** Diátaxis is already installed as a skill (`.agents/skills/diataxis`, mirrored at `.claude/skills/diataxis`) but has never been invoked against this repo — `docs/` doesn't follow its taxonomy, and there is no ADR or investigation concept at all today. Without a second bucket for "durable engineering knowledge that isn't a how-to," the diary will keep landing in Planning.md, because it's the only committed place that has ever accepted it.

The fix is architectural, not cosmetic: split **public docs** (Diátaxis: tutorials / how-to / reference / explanation) from **maintainer docs** (ADRs / investigations / planning), give each an authoritative owner for its kind of fact, and make Planning.md forward-looking only.

---

## 2. Current documentation assessment

| Location | Size | What it actually contains | Audience | Current/historical | Verdict |
|---|---|---|---|---|---|
| `README.md` | 105 lines | Pitch, feature table, "why", 3 getting-started paths, status list, license | User (GitHub visitor) | Current, well-scoped | **Keep**, minor trim |
| `Planning.md` L1–117 | ~117 lines | "Why this exists", design principles, v1 architecture diagram, component table, ingestion table, dashboard vision | User/explanation | Current but duplicates README's "why" and `docs/` architecture content | **Split**: merge into `docs/explanation/architecture.md`; delete duplication with README |
| Planning.md L119–3119 (Roadmap v1–v21) | ~3000 lines, 148/151 items checked | Per-feature: what shipped, decisions + alternatives considered, bugs found during verification, UX-iteration narrative, test counts | Maintainer (mostly), some pure diary | **Historical diary**, wearing a "roadmap" costume | **Triage line-by-line**: decisions → ADR, bug findings → investigation, still-relevant behavior → concept/reference, pure narrative → delete (Git history keeps it) |
| Planning.md L3121–3126 (Non-goals) | 6 lines | Product boundary ("not an APM/SIEM") | User | Current | **Move** to `docs/explanation/architecture.md` or README |
| Planning.md L3129–3136 (Open questions) | 8 lines | Two *already-decided* architecture calls (dashboard stack, buffering layer) written as resolved Q&A | Maintainer | Current, decision-grade | **Extract as ADRs** — highest-value single extraction in the file |
| Planning.md L3139–3155 (Tech stack, Contributing, License) | 15 lines | Stack list; 2-sentence contributing stub | User/maintainer | Current but thin | Tech stack → README/explanation; Contributing stub → real `CONTRIBUTING.md` (doesn't exist today) |
| `docs/getting-started.md` | 125 lines | Two setup paths + a "tour of the dashboard" | User | Current | Tutorial/how-to hybrid + explanation tail — **split** |
| `docs/standalone.md` | 318 lines | `docker compose up`, per-logger snippets, resources page, known-good versions | User | Current | Mostly clean how-to; "Known-good versions" is reference — **light split** |
| `docs/aspire-hosting.md` | 477 lines | AddFlare usage, params, publishing to Compose/K8s, SSL troubleshooting | User | Current, but Kubernetes section duplicates Planning.md v21's diary | How-to + reference (`Parameters`) + troubleshooting — **split**; dedupe against Planning v21 |
| `docs/cli.md` | 274 lines | Install/quick-start, command reference table, data layout, multi-instance, cluster mode, image-tag policy, known limitations | User | Current | Classic 4-way mixed doc — **split** |
| `docs/clustering.md` | 492 lines | Topology, **"Design decision: `Distributed` tables keep plain names"**, "**Operational notes confirmed against a real running cluster**" (evidence-flavored), dashboard cluster-status feature, verification SQL | User + maintainer | Current | Contains one clean ADR, one investigation-shaped section, and legitimate how-to/reference — **best representative split candidate** |
| `docs/auth.md` | 645 lines | Shared model, 4 auth methods each with How-it-works/Role-provisioning/Setup/Limitations, Managing users, Configuration reference, Backups | User | Current | Deepest 4-way mix in the repo — **split**, but keep as one how-to per method (don't over-fragment) |
| `docs/benchmark.md` | 163 lines | Methodology, environment, results, reproduction commands, explicit scope-out | Maintainer (evidence) | Current but is *evidence*, not a guide | **Move wholesale** to `docs-internal/investigations/` |
| `db/clickhouse/README.md` | 483 lines | Schema reference, **"Design decisions" (210 lines)**, field mappings, verification queries | Maintainer/contributor | Current | Reference stays with the project; "Design decisions" is the single richest ADR vein in the repo — **extract, don't delete the README** |
| `src/*/README.md` (Flare.Api, Flare.Ingest, Flare.Cli, Aspire.Flare, Aspire.Hosting.Flare, dashboard) | 25–324 lines each | "What it does today", project layout, run/build/test instructions, one or two design notes | Contributor (this-package-only) | Current | **Out of scope for this migration.** Legitimate per-project contributor READMEs — leave in place |
| `examples/README.md` | 71 lines | Runbook for the example app | Contributor | Current | Fine as-is |
| `.agents/skills/diataxis` / `.claude/skills/diataxis` | — | Installed Diátaxis skill (scaffold/write/audit), byte-identical in both locations, from `pfeff/claude-skills` per `skills-lock.json` | AI agent | Installed but never run against this repo | **Adopt as the public-docs taxonomy authority** — its own `scaffold` operation already targets `docs/{tutorials,how-to,reference,explanation}/` |
| `CONTRIBUTING.md`, `SECURITY.md`, `CODE_OF_CONDUCT.md` | — | **Don't exist** | User/contributor | — | **Gap.** Planning.md's 2-sentence "Contributing" section is the only trace of one |

---

## 3. Proposed documentation architecture

```text
/
├── README.md                # unchanged role: landing page, links out, doesn't grow
├── CONTRIBUTING.md          # Phase 11 — currently a 2-sentence stub, now removed
│                            # from Planning.md's own stub rather than left stale
├── LICENSE
├── Planning.md              # DONE (Phase 10): now a short redirect stub
│
├── docs/                                  # user-facing, Diátaxis — matches the
│   ├── README.md                          # installed skill's own scaffold shape
│   ├── tutorials/
│   │   └── getting-started.md
│   ├── how-to/
│   │   ├── run-standalone.md
│   │   ├── run-with-aspire.md
│   │   ├── run-with-cli.md
│   │   ├── configure-authentication.md
│   │   ├── run-cluster-mode.md
│   │   └── troubleshoot-deployment.md     # not yet created — no dedicated
│   │                                       # troubleshooting content surfaced
│   │                                       # as its own guide through phase 10
│   ├── reference/
│   │   ├── cli-commands.md
│   │   ├── authentication-config.md
│   │   ├── clustering-config.md
│   │   ├── aspire-hosting.md
│   │   ├── otlp-logger-versions.md
│   │   └── environment-variables.md       # not yet created — no single
│   │                                       # consolidated env-var reference
│   │                                       # exists yet; config keys are
│   │                                       # currently split across the
│   │                                       # reference files above by domain
│   ├── explanation/
│   │   ├── architecture.md                # DONE (Phase 10): design principles,
│   │   │                                   # pipeline diagram, non-goals, tour
│   │   ├── clustering.md
│   │   └── authentication-model.md
│   └── screenshots/, social-preview.png   # kept at their existing docs/ path,
│                                           # not moved under a docs/assets/ —
│                                           # no phase found a reason to move them
│
├── docs-internal/            # maintainer-facing, still committed to Git
│   ├── README.md             # the governance doc: what goes where, and why
│   ├── adr/                  # 13 done as of Phase 10
│   │   ├── 0001-sveltekit-dashboard.md
│   │   ├── 0002-redis-streams-buffering.md
│   │   ├── 0003-distributed-tables-plain-names-and-sharding.md
│   │   ├── 0004-embedded-sqlite-for-identity.md
│   │   ├── 0005-docker-socket-proxy-for-resources-page.md
│   │   ├── 0006-kubernetes-resource-graph-rbac-scoping.md
│   │   ├── 0007-pattern-clustering-at-flush-time.md
│   │   ├── 0008-clickhouse-attribute-typing.md
│   │   ├── 0009-crud-tables-use-replacingmergetree-tombstones.md
│   │   ├── 0010-logs-order-by-service-first.md
│   │   ├── 0011-spans-order-by-trace-id-first.md
│   │   ├── 0012-otlp-only-ingestion.md
│   │   └── 0013-clickhouse-as-storage-engine.md
│   ├── investigations/       # 5 done as of Phase 9
│   │   ├── clickhouse-cluster-operational-notes.md
│   │   ├── aspire-kubernetes-publish-and-resource-graph.md
│   │   ├── benchmark-ingest-and-query.md
│   │   ├── cli-verification-bugs.md
│   │   └── logs-virtuallist-hardening.md
│   └── planning/
│       ├── roadmap.md                     # DONE (Phase 10)
│       └── DOCUMENTATION-MIGRATION-PLAN.md  # this file, DONE (Phase 10)
│
└── .github/
```

This is deliberately **two top-level doc trees, not more**. A third tree for "contributor process" docs isn't justified — Flare doesn't yet have enough process content to need more than `CONTRIBUTING.md`, and per-project READMEs already cover per-project contributor needs.

**Why this doesn't need a documentation website yet, and why it can add one later without rework:** `docs/`'s four Diátaxis folders map 1:1 onto the nav sections of any of MkDocs Material, Docusaurus, or Starlight. Total public-doc volume today is comfortably browsable as raw GitHub Markdown. Introduce a generator only when docs outgrow flat-file browsing, Flare needs versioned docs across breaking changes, or search/nav quality actually blocks users.

---

## 4. Documentation taxonomy

| Type | Question it answers | Owns | Lives in |
|---|---|---|---|
| **Tutorial** | "Walk me through my first success with Flare" | The one guided path | `docs/tutorials/` |
| **How-to** | "How do I do X?" | Task procedures | `docs/how-to/` |
| **Reference** | "What are the exact options?" | CLI commands, config/env vars, ClickHouse schema, auth config keys | `docs/reference/` |
| **Explanation** | "Why does it work this way?" | Architecture, clustering model, auth model, data model | `docs/explanation/` |
| **ADR** | "How did we decide X, and why?" | One immutable record per significant decision | `docs-internal/adr/` |
| **Investigation** | "What did we discover investigating X?" | Evidence, benchmarks, root-cause write-ups | `docs-internal/investigations/` |
| **Planning** | "What are we planning to do?" | Roadmap, open items — **no diary** | `docs-internal/planning/` |
| **README** | "What is this, and where do I go next?" | Pitch + links only, never the content itself | `README.md` |
| **Contributor docs** | "How do I build/test/contribute to *this* package?" | Local dev loop for one project | `src/*/README.md` (unchanged, out of scope) |

Categories deliberately **not** created: a separate "concepts" vs. "architecture" split, a `docs/troubleshooting/` top-level folder (troubleshooting is task-oriented → a how-to), per-decision ADR subfolders (too few ADRs to categorize).

---

## 5. Rules for deciding where information belongs

1. Does Git history already tell this story on its own? → nothing to write. Stop.
2. Is this "how do I do X" for an end user? → `docs/how-to/`, or `docs/tutorials/` only if it's the single onboarding path.
3. Is this "what exact values/options exist"? → `docs/reference/`. A how-to *links* to reference instead of restating it.
4. Is this "why does X work this way" for an end user? → `docs/explanation/`.
5. Was this a decision with real alternatives, a long-term constraint, or would a future maintainer be confused without knowing why? → `docs-internal/adr/`. See §7 for the bar.
6. Was this a discovery from debugging, benchmarking, or field-testing (evidence, not a decision)? → `docs-internal/investigations/`. If it produced a decision, the decision still gets its own ADR that links back.
7. Is this a still-open future intent? → `docs-internal/planning/roadmap.md`, one line, no narrative.
8. Is this only relevant to building/testing one specific project? → that project's own `src/*/README.md`.
9. None of the above → it doesn't need to be written down. "We shipped it, tests passed, no decision or discovery worth keeping" is what a commit message and PR description are for.

**Ownership model (no duplication):** reference owns exact values; how-to/tutorials link to reference rather than restating config tables; explanation owns *why current behavior is what it is*; ADRs own *why a past decision was made* and are never edited to reflect new context (a changed decision gets a new ADR that supersedes the old one); investigations own *evidence*; README only summarizes and links.

---

## 6. Current → proposed migration map

All items below are **done** as of Phase 10:

- Planning.md's "Open questions" (L3129–3136) → `docs-internal/adr/0001-sveltekit-dashboard.md` + `0002-redis-streams-buffering.md` (Phase 1). Two of the five numbered questions were never explicitly resolved in place — item 4 (OTLP transport priority) turned out to be resolved in practice (both gRPC and HTTP are supported, confirmed throughout the migrated docs) and needed no ADR; item 5 (timestamp/timezone/clock-skew handling) is genuinely still open and moved to `roadmap.md` instead.
- Planning.md v16's ingest-vs-query-time tradeoff → `docs-internal/adr/0007-pattern-clustering-at-flush-time.md` (Phase 8)
- Planning.md's own design principles + "Storage is a solved problem"/"One protocol in: OTLP" framing → `docs-internal/adr/0012-otlp-only-ingestion.md` + `0013-clickhouse-as-storage-engine.md`, plus the non-ADR-grade principles folded directly into `docs/explanation/architecture.md` (Phase 10)
- `docs/clustering.md`'s "Design decision" section → `docs-internal/adr/0003-distributed-tables-plain-names-and-sharding.md` (Phase 3)
- `db/clickhouse/README.md`'s "Design decisions" → `docs-internal/adr/0008` through `0011` (Phase 8); the file itself stays in place as schema reference, pointing to these instead of re-narrating them
- `docs/benchmark.md` → `docs-internal/investigations/benchmark-ingest-and-query.md` (Phase 7), wholesale move
- Planning v21's 3 field-tested Kubernetes bugs → `docs-internal/investigations/aspire-kubernetes-publish-and-resource-graph.md` (Phase 6)
- The CLI-verification bugs (BOM, enum-casing) and the Logs `VirtualList` deep-dive → `docs-internal/investigations/cli-verification-bugs.md` + `logs-virtuallist-hardening.md` (Phase 9)
- ~145 remaining checked-off diary entries in Planning.md → deleted; Git history keeps the narrative
- The 5 genuinely still-open items (RustFS retention, Kubernetes persistent-storage API, skip-index-effectiveness research, CLI `incident.zip` mode, timestamp/clock-skew handling) → `docs-internal/planning/roadmap.md` (Phase 10)
- Planning.md's Non-goals section → `docs/explanation/architecture.md#non-goals` (Phase 10), corrected in the process — "not a metrics platform" was dropped since Flare has shipped metrics since v6; carrying a now-false claim forward would have been worse than dropping it
- Planning.md's Tech stack summary → `docs/explanation/architecture.md#built-with` (Phase 10)
- Planning.md's 2-sentence, now-stale "Contributing" stub (still said "pre-alpha") → dropped, not carried forward; a real `CONTRIBUTING.md` is Phase 11's job

---

## 7. ADR strategy

**Create an ADR when a decision:** significantly shapes architecture; introduces a technology with an ongoing maintenance/license/ops cost; establishes a long-term constraint future work must respect; affects on-disk/wire data layout or compatibility; had genuine alternatives seriously weighed; or would leave a future maintainer asking "why is it built this way?" with no way to find out.

**Do NOT create an ADR for:** routine feature work with one obvious implementation; implementation-detail refactors; anything already fully explained by reading the code; bug fixes (those are investigations, unless the fix itself required an architectural trade-off); UI iteration.

**Format:**

```markdown
# ADR-NNNN: Decision title

Status: Accepted | Superseded by ADR-NNNN | Deprecated
Date: YYYY-MM-DD

## Context
## Decision
## Alternatives considered
## Consequences
## Related documentation
```

**When a decision changes:** never rewrite an accepted ADR's Decision/Context in place. Write a new ADR, set its Status to reference the old one, and change the old one's Status to `Superseded by ADR-NNNN`.

---

## 8. Investigation strategy

**Create an investigation when:** debugging produced a non-obvious root cause worth remembering; a benchmark or load test produced numbers worth citing later; a "we tried X, it didn't work" dead end that could otherwise get re-attempted.

**Contents:** problem statement, environment/setup, what was tried, observations/evidence, conclusion, and a line back to whichever ADR or doc entry the finding fed into.

```text
Investigation → Evidence/discovery → ADR (only if the fix implied an architectural choice) → Public documentation (once the resulting behavior is worth telling users about)
```

Not every investigation produces an ADR; not every ADR needs a preceding investigation. Never force the relationship.

---

## 9. AI/Diátaxis integration

The installed diataxis skill already owns the four public-doc types end-to-end. Use it as-is for tutorials/how-to/reference/explanation; don't reimplement its classification logic. `docs-internal/README.md` is the short, project-specific governance doc covering what Diátaxis doesn't know about (ADR/investigation/planning) — no new skill needed.

---

## 10. Documentation governance

A PR that changes user-visible behavior updates the relevant `docs/how-to/` or `docs/reference/` page in the same PR. A PR that adds a config option updates `docs/reference/` in the same PR. A PR that makes a significant architectural call adds an ADR in the same PR. A PR whose interesting content is "we investigated X and found Y" adds an investigation doc, only if the finding has future value.

`docs-internal/planning/roadmap.md` should contain only forward-looking, unchecked items; a completed item is deleted the same PR that ships it, not checked off and kept.

Enforcement: a PR template checkbox is enough at Flare's current size — no bot, no CI gate.

---

## 11. Validation/tooling

- Link checking via a lightweight tool (e.g. `lychee`) over `docs/`, `docs-internal/`, `README.md`, `CONTRIBUTING.md` on PRs touching `*.md`.
- Orphan check: every file under `docs/{tutorials,how-to,reference,explanation}/` linked from `docs/README.md`'s index.
- ADR numbering: `ls docs-internal/adr/ | sort` is validation enough at this volume.
- Stale-claims check: the update-with-the-change rule in §10, plus periodic `/diataxis audit`.
- No documentation platform recommended at this time.

Still not implemented as of Phase 10 — this is Phase 11's remaining scope.

---

## 12. Incremental migration plan

| Phase | Objective | Status |
|---|---|---|
| 0 | Inventory + taxonomy (this document) | **Done** |
| 1 | `docs-internal/` skeleton + governance doc + 2 ADRs (SvelteKit, Redis Streams) from Planning.md's Open Questions | **Done** |
| 2 | `/diataxis scaffold` for `docs/{tutorials,how-to,reference,explanation}/` + index READMEs | **Done** |
| 3 | Migrate `docs/clustering.md` (representative document, validates the whole taxonomy) | **Done** |
| 4 | Migrate `docs/cli.md` | **Done** |
| 5 | Migrate `docs/auth.md` | **Done** |
| 6 | Migrate `docs/standalone.md`, `docs/aspire-hosting.md`, `docs/getting-started.md` | **Done** |
| 7 | Move `docs/benchmark.md` → `docs-internal/investigations/` | **Done** |
| 8 | Extract remaining ADRs from Planning.md + `db/clickhouse/README.md` | **Done** |
| 9 | Extract remaining investigations from Planning.md | **Done** |
| 10 | Prune Planning.md to `docs-internal/planning/roadmap.md` (this file moves there too) | **Done** |
| 11 | `CONTRIBUTING.md` + validation tooling | Not started |

---

## 13. First implementation step

**Phase 1**, narrowly: create `docs-internal/README.md` (the governance doc) and `docs-internal/adr/` with exactly two ADRs extracted from Planning.md's "Open questions" section — the SvelteKit-over-Blazor decision and the Redis-Streams-buffering decision. Nothing else moves; nothing existing is edited or deleted; no links change.

This plan document has now executed through Phase 10. Phase 11 (`CONTRIBUTING.md` + lightweight validation tooling) is the only remaining item.