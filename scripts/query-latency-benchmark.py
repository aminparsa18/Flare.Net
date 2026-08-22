#!/usr/bin/env python3
"""Query-latency benchmark for docs/benchmark.md - runs the 6 query-pattern categories
db/clickhouse/0001_logs.sql's own doc comments distinguish (ORDER BY-prefix filters vs.
skip-index lookups vs. the deliberately-unresolved all-services worst case) as real
HTTP calls against Flare.Api's actual /api/logs/search and /api/logs/aggregate
endpoints - not raw ClickHouse queries - so the numbers reflect what the dashboard
itself experiences (JSON serialization/HTTP overhead included, not just SQL execution
time).

Run scripts/seed-benchmark-logs.py first to have a realistic row count in place.

No third-party dependencies (stdlib only).

Usage:
    python3 scripts/query-latency-benchmark.py --trace-id <a real TraceId from the seed>
"""

from __future__ import annotations

import argparse
import json
import statistics
import sys
import time
import urllib.error
import urllib.request
from datetime import datetime, timedelta, timezone

ITERATIONS = 30
WARMUP = 3


def post_json(url: str, payload: dict) -> tuple[float, dict]:
    body = json.dumps(payload).encode("utf-8")
    req = urllib.request.Request(url, data=body, method="POST", headers={"Content-Type": "application/json"})
    start = time.perf_counter()
    try:
        with urllib.request.urlopen(req, timeout=35) as resp:
            data = json.loads(resp.read())
    except urllib.error.HTTPError as exc:
        detail = exc.read().decode("utf-8", errors="replace")
        raise RuntimeError(f"{url} failed ({exc.code}): {detail}") from exc
    elapsed_ms = (time.perf_counter() - start) * 1000
    return elapsed_ms, data


def run_pattern(name: str, url: str, payload: dict) -> None:
    # Warmup - first calls against a pattern pay ClickHouse's own cold-cache/JIT cost;
    # the benchmark wants steady-state latency, not that one-time cost.
    for _ in range(WARMUP):
        post_json(url, payload)

    samples = []
    for _ in range(ITERATIONS):
        elapsed_ms, _ = post_json(url, payload)
        samples.append(elapsed_ms)

    samples.sort()
    p50 = statistics.median(samples)
    p95_index = min(len(samples) - 1, int(round(0.95 * (len(samples) - 1))))
    p95 = samples[p95_index]
    print(f"{name:<45} p50={p50:8.1f}ms   p95={p95:8.1f}ms   (n={len(samples)})")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--api-url", default="http://localhost:8080", help="Flare.Api base URL.")
    parser.add_argument("--trace-id", required=True, help="A real TraceId present in the seeded data, for the exact-match pattern.")
    parser.add_argument("--service", default="benchmark-seed-svc-3", help="A seeded ServiceName for the service-scoped patterns.")
    args = parser.parse_args()

    search_url = f"{args.api_url}/api/logs/search"
    aggregate_url = f"{args.api_url}/api/logs/aggregate"

    now = datetime.now(timezone.utc)
    from_24h = (now - timedelta(hours=24)).isoformat()
    to_now = now.isoformat()

    print(f"Query-latency benchmark - {ITERATIONS} iterations/pattern after {WARMUP} warmup calls\n", file=sys.stderr)

    # (a) ServiceName + time filter - hits the ORDER BY prefix directly, the cheap case.
    run_pattern(
        "(a) service + time range",
        search_url,
        {"filter": {"from": from_24h, "to": to_now, "services": [args.service]}, "pageSize": 200},
    )

    # (b) + SeverityNumber refinement - still within the ORDER BY prefix.
    run_pattern(
        "(b) service + severity + time range",
        search_url,
        {
            "filter": {"from": from_24h, "to": to_now, "services": [args.service], "severityNumbers": [17, 18, 19, 20, 21]},
            "pageSize": 200,
        },
    )

    # (c) TraceId exact match - uses idx_trace_id's bloom_filter skip index, not the
    # ORDER BY prefix (TraceId is last in ORDER BY, so this isn't a locality win).
    run_pattern(
        "(c) TraceId exact match",
        search_url,
        {"filter": {"from": from_24h, "to": to_now, "traceId": args.trace_id}, "pageSize": 200},
    )

    # (d) LogAttributes key/value filter - uses the map skip indexes, no service scope.
    run_pattern(
        "(d) log attribute filter (no service scope)",
        search_url,
        {
            "filter": {
                "from": from_24h,
                "to": to_now,
                "attributes": [{"bag": "Log", "key": "endpoint", "value": "/api/orders"}],
            },
            "pageSize": 200,
        },
    )

    # (e) Body substring search - uses idx_body's tokenbf_v1 skip index.
    run_pattern(
        "(e) body substring search (no service scope)",
        search_url,
        {"filter": {"from": from_24h, "to": to_now, "search": "timed out"}, "pageSize": 200},
    )

    # (f) Unfiltered/all-services aggregate - the schema's own named worst case: no
    # ServiceName filter means no ORDER BY locality at all, over the full seeded window.
    run_pattern(
        "(f) unfiltered all-services aggregate",
        aggregate_url,
        {"filter": {"from": from_24h, "to": to_now}, "bucketWidthSeconds": 300, "groupBy": "Service"},
    )


if __name__ == "__main__":
    main()
