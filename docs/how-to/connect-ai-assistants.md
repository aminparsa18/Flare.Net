# How to let an AI assistant query Flare

Run `flare mcp` to give Claude Code, Cursor, VS Code and other
[Model Context Protocol](https://modelcontextprotocol.io) clients **read-only**
access to your logs, traces, metrics, exceptions and alerts. The assistant
launches `flare mcp` itself and talks to it over stdin/stdout; nothing new
listens on a port. This closes the debugging loop for a coding agent: run the
app, reproduce the bug, read the telemetry, change the code, run it again and
compare.

## Prerequisites

- The [`flare` CLI](run-with-cli.md) installed (`dotnet tool install -g Flare.Cli`).
- A Flare instance with data in it: the standing one from `flare start`, or
  any reachable Flare.Api (see [Query a remote Flare](#query-a-remote-flare)).

## Register the server with your client

**Claude Code**

```bash
claude mcp add flare -- flare mcp
```

**Cursor** — add to `.cursor/mcp.json` (or `~/.cursor/mcp.json`):

```json
{ "mcpServers": { "flare": { "command": "flare", "args": ["mcp"] } } }
```

**VS Code** — add to `.vscode/mcp.json`:

```json
{ "servers": { "flare": { "type": "stdio", "command": "flare", "args": ["mcp"] } } }
```

Target a named instance with `flare mcp -n <name>` (see `flare instances list`).
With no instance flag it uses the same instance every other `flare` command
does.

## Tools

All tools are read-only and return compact text, capped so a busy system can't
flood the assistant's context.

| Tool | What it answers |
|---|---|
| `search_logs` | Log events by service, level, text, trace id, attributes. Max 100 rows. |
| `search_traces` | Recent traces (one line each), optionally errors only or slower than N ms. |
| `get_trace` | One trace as an indented span tree. Max 200 spans. |
| `list_metrics` | Which metrics exist (name, type, unit, service). |
| `query_metric` | One metric summarized per series: first/last/min/avg/max, or percentiles for histograms. |
| `list_exceptions` | Top exception groups with counts and affected services. |
| `list_firing_alerts` | Alert rules firing right now. |
| `list_runs` | When a service was last started, and the starts before it. |
| `diff_traces` | Two traces compared: spans added/removed, duration and error changes. |
| `compare_runs` | The same endpoint in the service's previous run versus its latest run, diffed. |

## Scope to the last run

While developing, you usually care about telemetry from the process you just
started, not the last hour. Pass `lastRun: true` (with `services`) to
`search_logs`, `search_traces` or `list_exceptions` and the range starts when
that service last started.

A **run** is one process start, detected from the `service.instance.id`
resource attribute, which .NET Aspire and the OpenTelemetry .NET SDK set to a
fresh value on every start. Replicas that start within a minute of each other
count as one run. Run detection reads spans, so a service that emits logs but
no traces has no detectable run; use `since` for those.

## Compare before and after a fix

1. Exercise the endpoint (say `POST /checkout`) and note that it's slow or
   failing.
2. Let the assistant change the code and restart the app.
3. Exercise the endpoint again.
4. Ask for `compare_runs` on that service and operation name.

The diff matches spans by service and name, and lists spans added or removed,
mean duration changes of at least 20% and 5 ms, and per-span error count
changes. `diff_traces` does the same for two trace ids you pick.

## Query a remote Flare

```bash
flare mcp --api-url https://flare.example.com --token flr_pat_...
```

`--token` falls back to the `FLARE_API_TOKEN` environment variable, which keeps
the token out of your client's config file. Create the token as described in
[Personal access tokens](configure-authentication.md#personal-access-tokens); it
inherits your own role, so a `Viewer` token can't change anything even though
the tools are read-only anyway. The standing local instance has authentication
off by default and listens on loopback, so it needs no token.

## Limits

- stdio only: there's no streamable-HTTP endpoint on Flare.Api yet.
- No write tools (creating alerts, silencing, etc.) by design.
