# ADR-0100: LLM observability from `gen_ai.*` spans

Status: Accepted

Date: 2026-10-03

## Context

.NET apps that call a model through Microsoft.Extensions.AI
(`UseOpenTelemetry()`), Semantic Kernel or the provider SDKs emit spans
carrying OTel's GenAI semantic conventions: `gen_ai.operation.name`,
`gen_ai.request.model`, `gen_ai.usage.input_tokens` and
`gen_ai.usage.output_tokens`. Flare stores them (in `SpanAttributes`) but
nothing reads them, so the question every LLM-backed service ends up
asking, "which model is slow, failing, or burning tokens?", has no
answer short of hand-writing attribute filters in the trace explorer.
Prior art:
[signoz#10908](https://github.com/SigNoz/signoz/commit/755390c4b5b2456a7c5c44d98fe8fcb18671616b).

The work is split into phases so each one ships on its own:

1. **Usage page** (this ADR): calls, errors, latency and tokens per
   `(provider, model)`.
2. **Estimated cost**: an editable model → price-per-token table. It
   needs a config store (Identity SQLite, like unit overrides in ADR-0065),
   so it gets its own ADR.
3. Per-model drill-down and, if volumes warrant it, a pre-aggregated
   table (the ADR-0031 route).

## Decision

A new `/llm` dashboard page backed by `POST /api/llm/models`, which
returns one row per `(provider, model)` plus the list of services that
made model calls (for the toolbar's service picker). It takes the same
`WindowMinutes`/`EndUnixMs`/`Service` request shape as `/external-apis`.

- **Computed from `spans` at query time. No new table or migration.**
  Same reasoning as ADR-0056 and ADR-0071: model-call spans are a small
  share of all spans, every query requires a `gen_ai.*` key
  (`mapContains`), which `idx_span_attr_key` can use to skip granules, and
  the attribute names are still moving between semantic-convention
  versions, which a stored view would bake in.
- **Only model-call spans count.** A span is a model call when
  `gen_ai.operation.name` is `chat`, `text_completion`,
  `generate_content` or `embeddings`, or when no operation is set but a
  model is. `invoke_agent`, `create_agent` and `execute_tool` spans are
  excluded: agent spans carry the model and, with some instrumentations, a
  token total that their child `chat` spans already report, so counting
  both would double every token figure.
- **Old and new attribute names are both read.** Provider:
  `gen_ai.provider.name`, else the older `gen_ai.system`. Model:
  `gen_ai.request.model`, else `gen_ai.response.model`. Input tokens:
  `gen_ai.usage.input_tokens`, else `gen_ai.usage.prompt_tokens`; output
  tokens: `gen_ai.usage.output_tokens`, else
  `gen_ai.usage.completion_tokens`. The request model is preferred
  because it is what the code asked for; the response model is usually a
  dated snapshot name (`gpt-4o-2024-08-06`) that would split one model
  into several rows.
- **Token attributes are stored as strings** (`SpanAttributes` is a
  `Map(…, String)`), so they are read with `toUInt64OrZero`. A span
  without them contributes 0 rather than being dropped from the call
  count.
- **Errors are span status errors**, like every other page here.
  Latency is the span duration (p50/p95/p99, approximate quantiles).
  For streaming responses that is whatever the instrumentation chose to
  record, normally time to the end of the stream.
- **Drill-down to traces** goes through the existing trace explorer with
  an attribute filter on `gen_ai.request.model`, so this phase adds no
  detail endpoint.

## Consequences

- Any app already emitting GenAI spans gets a per-model view of traffic,
  failures, latency and token use with no ingest or schema change, on
  data that is already stored.
- Every page load scans spans in the window carrying a `gen_ai.*` key.
  Like `/errors` and `/messaging` it is an on-demand page, not a polled
  one.
- The operation filter and the attribute fallbacks live in one place,
  `LlmQueryBuilder`, and are unit-tested, so a convention change is a
  one-file edit.
- Not covered yet: cost, per-model drill-down, prompt/response content
  (those arrive as events or opt-in attributes and are left to the trace
  detail view), and alert rules on token usage.
