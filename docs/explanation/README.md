# Explanation

Explanation documentation is **understanding-oriented**: it clarifies why
Flare is built the way it is — architecture, the clustering model, the
authentication model, the data model.

## Guidelines for writing explanations

- Focus on **why**, not on step-by-step "how" (that's a how-to) or exact
  values (that's reference).
- Provide **context**: what problem the current design solves.
- It's fine to mention alternatives in passing for context, but the actual
  **decision record** — alternatives seriously weighed, why one was
  rejected, consequences — belongs in an ADR
  ([`../../docs-internal/adr/`](../../docs-internal/adr/)), not here.
  Explanation pages should *link* to the relevant ADR rather than
  re-narrating its reasoning.
- Connect to the **bigger picture** — how this piece fits with the rest of
  Flare's architecture.

## Topics suited for explanation, in Flare

- Overall architecture (ingest → buffer → ClickHouse → API → dashboard)
- The clustering/multi-node model
- The authentication model (shared concepts across the 5 auth methods)
- The data model (log/span/metric shape, how attributes are stored)

## Structure suggestions

- Start with the question being answered ("why is ingestion OTLP-only?")
- Provide background/context
- Explore from the angle a Flare user actually needs, not every angle that
  exists
- Link out to the ADR if the "why" is really a recorded decision, and to
  reference/how-to pages for the exact mechanics

## Anti-patterns to avoid

- Step-by-step instructions (that's a how-to)
- Exact config values or schemas (that's reference)
- Re-litigating a decision that already has an ADR — link to it instead
- Being abstract without grounding in what Flare actually does today
