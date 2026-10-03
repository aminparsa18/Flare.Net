# ADR-0101: Estimated cost on the `/llm` page

Status: Accepted

Date: 2026-10-03

## Context

[ADR-0100](0100-llm-observability-genai-spans.md) shows tokens per
`(provider, model)` but not what they cost, which is the number people watch
LLM-backed services for. Spans carry token counts, never prices, so the price
has to come from Flare. Prices differ per model, change over time and are
often negotiated, so a fixed table in code is not enough on its own. The
per-model drill-down to traces already shipped in ADR-0100 as the **View
traces** link.

## Decision

Each `/api/llm/models` row gains `InputPricePerMillion`,
`OutputPricePerMillion`, `PriceIsCustom` and `EstimatedCost`
(`(input × inputPrice + output × outputPrice) / 1,000,000`, USD).

- **Built-in defaults live in code, overrides in Identity SQLite.**
  `LlmPricing` carries list prices for common OpenAI, Anthropic and Gemini
  models; the new `LlmModelPrices` table (Identity migration 0025) holds only
  admin overrides. Config, not telemetry, so SQLite rather than ClickHouse,
  as with ADR-0065. Keeping the defaults out of the table means a release
  can refresh them without a migration and without clobbering anything an
  admin set.
- **Defaults match by longest prefix, overrides match exactly.** Providers
  report dated snapshots (`gpt-4o-mini-2024-07-18`), so `gpt-4o-mini` must
  win over `gpt-4o`. Matching ignores case and any routing prefix before the
  last `/` (`openai/gpt-4o`). An override is for the exact model string an
  admin typed (case-insensitively), because a prefix override could silently
  reprice models the admin never looked at.
- **No price means no cost, not $0.** A model with neither an override nor a
  default shows "No price" and a null cost. A zero would read as free.
- **Applied after the ClickHouse read, never in SQL.** Same as ADR-0065: an
  edited price shows on the next load, and nothing about prices touches the
  query or any cache.
- **Admin-only writes.** `PUT /api/llm/prices` and `DELETE /api/llm/prices?model=`
  sit in `adminRoutes` (a price is a global setting for every user). The
  model travels in the body or query string because names can contain `/`.
  There is no read route: the models response already carries the effective
  price. Prices are validated to 0 – 1,000,000 USD per million tokens.
- **Token counts only.** Flare cannot see cached-input discounts, batch
  pricing or per-contract rates, so the figure is an estimate and the UI and
  docs say so.

## Consequences

- Any app already emitting GenAI spans gets a cost column with no ingest or
  schema change to ClickHouse.
- Default prices go stale as providers change them. They are one table in
  `LlmPricing.cs`, and an admin can override any model in the meantime.
- A model an admin hasn't priced and that no default covers (a local or
  self-hosted model) can only be priced by editing it from its row, so it
  must have been seen first.
- Not covered: cost per service or per trace, cost alert rules, and
  pre-aggregation (still on the roadmap if volumes warrant it).
