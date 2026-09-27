# How to find traces by how their spans relate

A structural query lists the traces whose spans relate in a way you
describe. For example: "a `checkout` span that calls `payment`, where
that `payment` span errored", or "traces where `api` reaches the database
without going through the cache". Ordinary filters test one span at a time
and can't express this.

Structural queries work on the spans Flare already stores, including spans
stored before you wrote the query.

## Prerequisites

- A running Flare instance receiving traces from your applications.
- The spans you want to relate must be in **one trace**, with parent links
  between them. The OpenTelemetry HTTP, gRPC and messaging instrumentations
  propagate trace context between services by default.

## Define the conditions

1. Open **Traces** in the top nav and pick a time range.
2. Click **Structure** in the toolbar.
3. Each lettered card (**A**, **B**, ...) is a span condition. Set any of:
   - **Service**: the exact `service.name` of the span.
   - **Span name**: the exact span name (the operation).
   - **Status**: Error, OK or Unset.
   - **Min ms**: the span lasted at least this many milliseconds.
   - **Attribute** filters, with the same operators as the Traces
     explorer.

   A span matches the condition when all of its settings hold. Use
   **Condition** to add a card, up to six. The service and span-name boxes
   suggest values from the selected time range.

## Write the expression

Combine the letters in the **Expression** box:

| Write | Matches traces where |
|---|---|
| `A` | some span matches A |
| `A -> B` | a span matching B is a **direct child** of a span matching A |
| `A => B` | a span matching B is a **descendant** of a span matching A, at any depth |
| `X AND Y`, `X && Y` | both hold |
| `X OR Y`, `X \|\| Y` | either holds |
| `NOT X`, `!X` | X doesn't hold |

`->` and `=>` bind tightest, then `NOT`, then `AND`, then `OR`. Use
parentheses to group. Letters and keywords are case-insensitive.

Click **Apply** or press Enter. The list, the facet counts and the Service
filter now cover only the matching traces. The expression stays on the
**Structure** button after you close the editor. Click **Remove** in the
editor, or **Clear filters**, to drop it.

### Examples

With **A** = service `checkout`, **B** = service `payment` and status
Error:

- `A => B`: checkout led to a failed payment, directly or through other
  services.
- `A -> B`: checkout called the failing payment span itself.
- `A => B AND NOT A -> B`: the failure happened further down, not in a span
  checkout called directly.

With **A** = service `api`, **B** = service `postgres`, **C** = service
`redis`:

- `A => B AND NOT A => C`: requests that reached the database without
  touching the cache.

## Save and share

The structure is part of the Traces view state. **Views** → **Save current
view** keeps it, a saved view's link restores it, and **Pin to dashboard**
turns it into a panel.

## Use it from the CLI

`flare traces` takes the same conditions with `--span` and the expression
with `--where`:

```bash
flare traces --since 24h \
  --span "A:service=checkout" \
  --span "B:service=payment,status=error" \
  --where "A => B"
```

A `--span` value is a letter, a colon, then comma-separated `key=value`
pairs. The keys are `service`, `name`, `status` (`ok`, `error`, `unset`) and
`min-duration` (for example `500ms`). Attribute conditions are available in
the dashboard only. See [`flare traces`](../reference/cli-commands.md).

## Troubleshooting

**"The expression uses condition D, which isn't defined."** Every letter in
the expression needs a card. Cards the expression doesn't use are ignored.

**"The expression also matches traces with none of its spans."** An
expression like `NOT A` on its own would match every other trace in the
range. Combine it with a condition the trace must have, for example
`B AND NOT A`.

**"Chains like 'A -> B -> C' aren't supported."** Write each pair
separately: `A -> B AND B -> C`. That form checks the two pairs
independently, so the B in each pair can be a different span.

**A trace you expected is missing.** Only spans that start inside the time
range take part. A trace that crosses the range's edge can lose the span
that links the two conditions, so widen the range. `=>` also needs every
span in between. If a hop wasn't instrumented, or its span was sampled out,
the chain breaks there.

**The query is slow.** `=>` reads every span of each trace that could
match. Make conditions more specific (a service and a span name rather than
just a status), or shorten the time range.

For how the queries are evaluated and what they cost, see
[ADR-0069](../../docs-internal/adr/0069-structural-trace-queries.md).
