# Maintainer documentation

This tree holds documentation for **maintainers and contributors**, as distinct
from `docs/`, which is for **Flare users** and follows the
[Diátaxis](https://diataxis.fr/) framework (also installed as an AI skill at
`.agents/skills/diataxis` / `.claude/skills/diataxis` — use `/diataxis audit`
or `/diataxis write <type>` when authoring anything under `docs/`).

```text
docs-internal/
├── adr/            architecture decisions — what we decided, and why
├── investigations/ technical investigations — what we discovered, and the evidence
└── planning/       forward-looking roadmap — no diary, no history
```

See [`planning/roadmap.md`](planning/roadmap.md) for Flare's current
forward-looking roadmap.

## Where does new information belong?

Work through these in order:

1. **Does Git history already tell this story on its own?** ("we tried X,
   then Y, here's the PR sequence") → nothing to write. Stop.
2. **"How do I do X?" for an end user** → `docs/how-to/`, or `docs/tutorials/`
   only if it's the single onboarding path.
3. **"What are the exact values/options?"** → `docs/reference/`. A how-to
   *links* to reference instead of restating it.
4. **"Why does X work this way?" for an end user** → `docs/explanation/`.
5. **A decision with real alternatives, a long-term constraint, or something
   a future maintainer would be confused without knowing why** → `adr/`.
   See "ADR bar" below.
6. **A discovery from debugging, benchmarking, or field-testing** (evidence,
   not a decision) → `investigations/`. If it produced a decision, the
   decision still gets its own ADR that links back to the investigation.
7. **A still-open future intent** → `planning/roadmap.md`, one line, no
   narrative.
8. **Only relevant to building/testing one specific project** → that
   project's own `src/*/README.md`.
9. **None of the above** → it doesn't need to be written down. "We shipped
   it, tests passed, no decision or discovery worth keeping" is what a
   commit message and PR description are for.

**Ownership, to avoid duplication:** reference owns exact values; how-to and
tutorials link to reference rather than restating config tables; explanation
owns *why current behavior is what it is*; ADRs own *why a past decision was
made* and are never edited to reflect new context — a changed decision gets a
new ADR that supersedes the old one (see below); investigations own
*evidence*; `README.md` at the repo root only summarizes and links.

## When to write an ADR

Write one when a decision:

- significantly shapes architecture
- introduces a technology with an ongoing maintenance/license/ops cost
- establishes a long-term constraint future work must respect
- affects on-disk/wire data layout or compatibility
- had genuine alternatives that were seriously weighed
- would leave a future maintainer asking "why is it built this way?" with no
  way to find out

**Don't** write one for: routine feature work with one obvious
implementation, implementation-detail refactors, anything already fully
explained by reading the code, bug fixes (those are investigations, unless
the fix itself required an architectural trade-off), or UI iteration.

Format:

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

Numbering is sequential by creation order, zero-padded to 4 digits
(`0001`, `0002`, …) — check `ls docs-internal/adr/` for the next number.

**If a decision changes:** never rewrite an accepted ADR's Decision/Context
in place. Write a new ADR, and change the old one's `Status` line to
`Superseded by ADR-NNNN` — its reasoning stays intact as a record of why the
*previous* architecture existed.

## When to write an investigation

Write one when: debugging produced a non-obvious root cause worth
remembering; a benchmark or load test produced numbers worth citing later;
or a "we tried X, it didn't work" dead end could otherwise get re-attempted
by someone who didn't see it fail the first time.

Contents: problem statement, environment/setup, what was tried,
observations/evidence (real command output, real error text), conclusion,
and a line back to whichever ADR or `docs/` page the finding fed into.

```text
Investigation → Evidence/discovery
     → ADR   (only if the fix implied an architectural choice)
     → docs/ (once the resulting behavior is worth telling users about)
```

Not every investigation produces an ADR; not every ADR needs a preceding
investigation. Never force the relationship in either direction.

## Planning

`planning/roadmap.md` holds forward-looking, unchecked items only. A
completed item is **deleted** the same PR that ships it, not checked off and
kept — the diary belongs to Git history and, where it has future value, to
an ADR or investigation instead. This is the rule that keeps `Planning.md`'s
history (3,000+ lines of shipped-and-checked-off items) from recurring.

## Updating docs alongside code

- A PR that changes user-visible behavior updates the relevant
  `docs/how-to/` or `docs/reference/` page in the same PR.
- A PR that adds a config option updates `docs/reference/` in the same PR.
- A PR that makes a significant architectural call adds an ADR in the same
  PR, not after the fact.
- A PR whose interesting content is "we investigated X and found Y" adds an
  investigation doc, only if the finding clears the bar above.

## Validating changes

Run `python3 scripts/check-docs-links.py` after touching any Markdown
under `docs/`, `docs-internal/`, `README.md`, or `CONTRIBUTING.md` — it
checks every relative link (including `#heading` anchors) resolves, and
that every `docs/{tutorials,how-to,reference,explanation}/` page is
reachable from `docs/README.md`'s index. Also wired into CI
(`.github/workflows/docs-links.yml`) on any PR touching `*.md`.