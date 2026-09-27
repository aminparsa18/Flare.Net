# ADR-0070: `ParseJson` pipeline-rule action

Status: Accepted

Date: 2026-09-27

## Context

Many apps log a JSON string as the log body: Serilog's JSON formatter,
the .NET console JSON formatter, and most Node and Python structured
loggers. Flare can already *query* such bodies through body-JSON filters.
Pipeline rules ([ADR-0033](0033-pipeline-rules-extraction-redaction.md))
only had `ExtractRegex` and `RedactRegex`, though, so those fields never
became real log attributes. That left them out of facets, group-by,
pinned attributes and attribute actions. Pulling every field out with
one regex per key isn't practical.

## Decision

**Add a third `RuleActionKind`, `ParseJson`. It parses a source field
(`Body` by default, or a `Log` attribute) as a JSON object and flattens
it into `LogAttributes`.** Like the other kinds, it lives in both
`Flare.Ingest`'s `PipelineRuleExecutor` and `Flare.Api`'s
`PipelineRuleActionExecutor` preview mirror
([ADR-0034](0034-pipeline-rules-preview.md)).

- **Flattening rules.** Nested object keys are joined with `.`, so
  `{"user":{"id":7}}` becomes `user.id=7`. That matches the OTel
  dotted-attribute convention. Strings are written unquoted. Numbers and
  booleans are written as their JSON text. `null` is skipped, not
  written as `"null"`.
- **Arrays aren't index-flattened.** An array is written whole, as its
  raw JSON text (`tags=["a","b"]`). Flattening `items.0.id`,
  `items.1.id` and so on would turn one array into an unbounded number
  of keys, and the resulting keys aren't useful to facet or group by.
  The raw text can still be queried like any other attribute value.
- **Bounded by design.** `MaxDepth` (default 5, at most 10) caps how many
  object levels are flattened; a deeper object is kept as raw JSON text
  under its parent's key. `MaxKeys` (default 100, at most 500) stops
  writing after that many attributes, in document order. Together they
  stop one huge body from blowing up the attribute map (and the
  `LogAttributes` `Map` column behind it). Both are nullable on the wire,
  and the executors resolve null to the default. `PipelineRuleRequest.Validate`
  rejects out-of-range values. The executors also clamp, so a
  hand-edited `ActionsJson` row can't get past the limits.
- **Optional `KeyPrefix`.** It's prepended verbatim (e.g. `json.`) so
  parsed keys don't collide with attributes the app already sets.
  Without a prefix, a parsed key overwrites an existing attribute of the
  same name, the same as an `ExtractRegex` named group already does.
- **Fail-closed, cheap on non-JSON.** A source that doesn't start
  (after whitespace) with `{` is skipped before any parse attempt, since
  most bodies are plain text. Malformed JSON, a non-object root, or
  nesting past `JsonDocument`'s own 64-level limit is a no-op for that
  event, the same posture a regex timeout already has.
- **Source is never mutated.** `Body` stays as it was, so Drain
  clustering and body-JSON filters keep working on it.

No migration: `pipeline_rules.ActionsJson` is opaque JSON. The MemoryPack
wire type `PipelineRuleAction` gets `ParseJson` as a new *last* member,
so a 3-member payload from an older dashboard still deserializes.

## Consequences

- JSON-logging apps get first-class attributes with one rule and no
  regex.
- Each `ParseJson` rule adds a `JsonDocument` parse per matching event
  at flush time. Scope the rule to the services that actually log JSON
  (the form's "matches all logs" warning still applies).
- Array elements aren't individually addressable as attributes. If that
  turns out to matter, a later option can add index flattening with its
  own cap.
