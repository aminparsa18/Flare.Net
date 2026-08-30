# Investigation: bugs found verifying `flare tail` and `flare export`

Dates: 2026-08-16 (`flare tail`) and 2026-08-22 (`flare export`)
Related: `docs/reference/cli-commands.md`, `docs/how-to/run-with-cli.md`

## Problem statement

Both `flare tail` (live-tail streaming) and `flare export` (bulk log
export) were built as thin HTTP/WebSocket clients against endpoints
`Flare.Api` already exposed — "no backend work needed, just a CLI client"
was the working assumption. Real end-to-end verification against a live
stack (not just reading the endpoint's own docs) surfaced two real bugs in
existing server/doc behavior, neither of which unit tests against the CLI
alone would have caught.

## Finding 1: live-tail's JSON `type` discriminator doesn't match its own documented example

**Environment**: real live-tail session over `GET /api/logs/tail`'s
WebSocket, real OTLP traffic via `ExampleApp.LogGenerator`.

`Flare.Api/README.md`'s own live-tail example shows the event envelope as
`{"type":"event",...}`, but the server actually emits
`{"type":"Event",...}`. `LogTailJsonContext`'s camelCase
`PropertyNamingPolicy` only rewrites property *names*, not this
enum-typed `type` discriminator's *value* — `UseStringEnumConverter`
serializes it as the raw PascalCase C# member name, unaffected by the
naming policy.

**Evidence**: a hand-rolled client built against the doc's literal example
silently received zero events — no error, just nothing arriving, because
its type-discriminator check never matched. Caught only by reproducing
the raw wire traffic directly with a Python `websockets` probe and reading
what actually came back, rather than trusting the documented shape.

**Fix**: `flare tail`'s own client parses the `type` field
case-insensitively. `Flare.Api/README.md`'s example was corrected
separately (`docs-internal/`-migration phase 9's own follow-up) to show
the real `Event`/`Dropped`/`Error` casing.

## Finding 2: `flare export -o` wrote a UTF-8 BOM, corrupting the CSV header

**Environment**: `flare export ... -o <path>` against a real stack,
output validated with Python's `csv` module.

The file-output path used `Encoding.UTF8`, whose .NET default preamble
writes a UTF-8 byte-order-mark at the start of the file. This corrupted
the CSV header's first cell to `"﻿EventId"` for any reader that
doesn't know to strip a leading BOM (confirmed live via Python's `csv`
module without `utf-8-sig`) — and would have been even less forgiving for
an NDJSON parser expecting `{` as the literal first byte of the file.

**Fix**: explicit `new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)`
instead of the bare `Encoding.UTF8` default. Re-verified against real
data afterward: NDJSON validated as real JSON per line, CSV validated as
real RFC4180 (correct quoting on a message containing a comma, header/row
column counts match, no BOM).

## Conclusion

Both bugs share a common shape worth naming explicitly: **the CLI's own
correctness wasn't in question — the server-side contract it was built
against (a documented wire example, a default .NET encoding) was quietly
wrong**, and only surfaced by driving a real client against a real running
stack and inspecting the literal bytes on the wire, not by reading source
or docs and trusting them. Both fixes are narrow and already shipped;
nothing else in the codebase was found sharing either root cause during
this pass.

## Unresolved / follow-ups

None — `Flare.Api/README.md`'s live-tail example inaccuracy (flagged
above) was corrected alongside this investigation being written.