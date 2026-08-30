# Reference

Reference documentation is **information-oriented**: exact, dry, accurate
descriptions of Flare's machinery — CLI commands, configuration keys,
environment variables, ClickHouse schema/columns, auth config.

## Guidelines for writing reference docs

- Structure around the **system's own structure** (one page per command
  set, one page per config domain), not around a narrative.
- Be **consistent** in format across pages — tables for parameters, one row
  per option.
- Do nothing but **describe** — no setup instructions, no "why", no
  narrative. If a reader needs to be walked through using it, that's a
  [how-to guide](../how-to/) that should link here, not duplicate this.
- Be **accurate and current** — reference pages own the authoritative value
  for whatever they describe; every other doc type links here instead of
  restating it.
- Include short usage **examples** per entry where useful.

## Structure suggestions

- CLI commands and flags (`reference/cli-commands.md`)
- Configuration keys / environment variables, with type and default
- Auth configuration keys per method
- ClickHouse schema: table/column reference

## Format recommendations

| Element | Format |
|---|---|
| Command/flag | Code block + table of flags |
| Config key | Table: name, type, default, description |
| Schema column | Table: column, type, meaning |

## Anti-patterns to avoid

- Including step-by-step setup instructions (that's a how-to)
- Adding explanation or rationale (that's an explanation, or an ADR if it's
  a decision — see [`../../docs-internal/README.md`](../../docs-internal/README.md))
- Inconsistent formatting between pages
- Values that drift from what the code actually does — verify against
  source before writing a reference page, not from memory of what it used
  to do
