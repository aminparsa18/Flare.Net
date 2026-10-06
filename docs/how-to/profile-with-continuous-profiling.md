# How to explore continuous profiles

Profiles show which functions your code spends its CPU time or memory in. Flare accepts the OpenTelemetry **profiles** signal on the same OTLP ports as logs, traces and metrics, stores every sample with its call stack, and draws the merged result as a flame graph. Samples taken during a traced span carry that span's ids, so you can jump from a slow span to the code that was running.

OTLP profiles is **Alpha** in OpenTelemetry. The wire format can still change between releases, so expect to re-check your sender after upgrading it.

## Send profiles to Flare

Point an OTLP profiles sender at the endpoints you already use:

| Transport | Address |
| --- | --- |
| gRPC | `localhost:4317` (`ProfilesService/Export`) |
| HTTP | `POST http://localhost:4318/v1development/profiles` (protobuf or JSON) |

Profiles go through the same ingest-key authentication, request size cap and per-key limits as the other signals. The **Ingestion** and **Pipeline** pages list Profiles next to Logs, Traces and Metrics.

OTLP profiles is new, so few senders exist yet. The OpenTelemetry Collector can forward profiles it receives: start it with `--feature-gates=service.profilesSupport` and set `profiles_endpoint: http://localhost:4318/v1development/profiles` on its `otlp_http` exporter. If you've enabled [ingest API keys](configure-authentication.md#ingest-api-keys), add the key to the exporter's `headers`. Compressed (gzip) request bodies are accepted.

The Collector's `pprof` receiver (v0.162, Alpha) reads pprof files and Go `/debug/pprof` endpoints, but it currently emits samples without call stacks, so a flame graph built from it has only the root. Flare tells you when that happens.

### Try it with curl

This sends one profile with two stacks. Each sample points at a stack by index, and a stack lists its frames leaf first. Index 0 of every table is the empty entry.

```bash
curl -s -X POST http://localhost:4318/v1development/profiles \
  -H 'Content-Type: application/json' \
  -d '{
  "resourceProfiles": [{
    "resource": {"attributes": [{"key": "service.name", "value": {"stringValue": "checkout"}}]},
    "scopeProfiles": [{
      "profiles": [{
        "sampleType": {"typeStrindex": 1, "unitStrindex": 2},
        "timeUnixNano": "'"$(date +%s)"'000000000",
        "durationNano": "10000000000",
        "samples": [
          {"stackIndex": 1, "values": ["70000000"]},
          {"stackIndex": 2, "values": ["30000000"]}
        ]
      }]
    }]
  }],
  "dictionary": {
    "stringTable": ["", "cpu", "nanoseconds", "main", "handle", "db.Exec"],
    "functionTable": [{}, {"nameStrindex": 3}, {"nameStrindex": 4}, {"nameStrindex": 5}],
    "locationTable": [{}, {"lines": [{"functionIndex": 1}]}, {"lines": [{"functionIndex": 2}]}, {"lines": [{"functionIndex": 3}]}],
    "stackTable": [{}, {"locationIndices": [3, 2, 1]}, {"locationIndices": [2, 1]}]
  }
}'
```

Open **Profiles**, pick the `checkout` service and the `cpu` sample type, and you'll see `main` > `handle` > `db.Exec`.

## Open the Profiles page

1. Open **Profiles** from the **More** menu.
2. Pick a **service**, a **sample type** (for example `cpu` or `alloc_space`) and a time window. Series are listed per service and sample type because values of different types can't be added together.
3. The flame graph merges every sample in the window. A frame's width is its share of the total. Hover a frame to see its total, its percentage and its **self** value, the part spent in that frame and not in the functions it calls.
4. Click a frame to zoom into it. **Reset zoom** returns to the whole graph.

Flare merges up to 5,000 distinct stacks. When there are more, the page says **Truncated** and leaves out the lightest stacks.

## Profile one span

1. Open a trace and click a span.
2. Click **View profile** in the span panel.

The Profiles page opens with only the samples taken during that span. This needs a sender that records the active span on each sample (the profile's link to `trace_id` and `span_id`). Without it the graph is empty; click **Clear** to go back to the service-wide graph.

## Query the API

```bash
# Which series exist in the last hour?
curl -s -X POST http://localhost:8080/api/profiles/types \
  -H 'Content-Type: application/json' -d '{"windowMinutes":60}'

# The merged call tree for one series, optionally narrowed to a span
curl -s -X POST http://localhost:8080/api/profiles/flamegraph \
  -H 'Content-Type: application/json' \
  -d '{"service":"checkout","sampleType":"cpu","windowMinutes":60,"traceId":"<hex>","spanId":"<hex>"}'
```

The flame graph response is a tree of `{ name, total, self, children }` under a synthetic `all` root. `sampleUnit` tells you whether values are nanoseconds, bytes or a plain count.

## Limits

- Profile data has no retention policy of its own yet, the same as spans. Retention for all signals is tracked as one roadmap item.
- Native frames that aren't symbolized appear as `module+0xaddress`.
