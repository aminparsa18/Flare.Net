#!/usr/bin/env python3
"""Bulk-seeds clickhousedb.logs directly (bypassing the ingest pipeline) with synthetic
rows for the query-latency benchmark in docs/benchmark.md.

Bypasses Flare.Ingest/Redis Streams entirely and talks straight to ClickHouse's HTTP
interface, on purpose: seeding millions of rows through the real ingest pipeline
one-by-one would conflate seeding cost with the thing the benchmark actually measures,
and would take far longer than necessary. This script is not a substitute for the
ingest-throughput benchmark (see examples/ExampleApp.LogGenerator's /generate-throughput
endpoint for that) - it's purely a way to get a realistic-sized, realistically-shaped
dataset in place fast so query latency can be measured against real row counts.

Every seeded row's ServiceName is prefixed "benchmark-seed-" specifically so cleanup is
unambiguous - see --cleanup below.

No third-party dependencies (stdlib only - urllib, not requests) so this runs anywhere
Python 3 is available without a pip install first.

Usage:
    python3 scripts/seed-benchmark-logs.py --rows 5000000
    python3 scripts/seed-benchmark-logs.py --cleanup   # removes only benchmark-seed-* rows
"""

from __future__ import annotations

import argparse
import json
import random
import string
import sys
import time
import urllib.error
import urllib.request
import uuid
from datetime import datetime, timedelta, timezone

SERVICE_COUNT = 10
BATCH_SIZE = 50_000
WINDOW_HOURS = 24

# (SeverityText, SeverityNumber, weight) - skewed toward Info/Warn with a realistic
# Error tail, matching what a real service's log volume actually looks like rather than
# a uniform distribution across all 6 OTel severity buckets.
SEVERITIES = [
    ("Debug", 5, 10),
    ("Information", 9, 60),
    ("Warning", 13, 20),
    ("Error", 17, 9),
    ("Fatal", 21, 1),
]

# A small, fixed set of templated messages - not fully random text - so idx_body's
# tokenbf_v1 skip index has real, repeated substrings to actually hit during the
# Body-substring-search query pattern, the same way real application logs cluster into
# a bounded number of message shapes (this is also exactly what v16's Drain clustering
# assumes about real log traffic).
BODY_TEMPLATES = [
    "Request to {endpoint} completed in {ms}ms",
    "Request to {endpoint} timed out after {ms}ms",
    "Cache miss for key {key}, fetching from origin",
    "Cache hit for key {key}",
    "Database query against {table} took {ms}ms",
    "Failed to connect to {table} after {ms}ms",
    "User {user} authenticated successfully",
    "Authentication failed for user {user}",
    "Processed {count} items in batch job {job}",
    "Batch job {job} failed with {count} errors",
]

ENDPOINTS = ["/api/orders", "/api/users", "/api/inventory", "/api/payments", "/api/search"]
TABLES = ["orders", "users", "inventory", "payments"]
JOBS = ["sync-inventory", "send-emails", "refresh-cache", "generate-invoices"]


def random_hex(length: int) -> str:
    return "".join(random.choices("0123456789abcdef", k=length))


def weighted_severity() -> tuple[str, int]:
    total = sum(w for _, _, w in SEVERITIES)
    pick = random.uniform(0, total)
    upto = 0.0
    for text, number, weight in SEVERITIES:
        upto += weight
        if pick <= upto:
            return text, number
    return SEVERITIES[-1][0], SEVERITIES[-1][1]


def random_body() -> str:
    template = random.choice(BODY_TEMPLATES)
    return template.format(
        endpoint=random.choice(ENDPOINTS),
        ms=random.randint(5, 3000),
        key=f"{random.choice(['user', 'order', 'product'])}:{random.randint(1, 5000)}",
        table=random.choice(TABLES),
        user=f"user{random.randint(1, 2000)}",
        count=random.randint(1, 500),
        job=random.choice(JOBS),
    )


def random_timestamp(now: datetime) -> datetime:
    offset_seconds = random.uniform(0, WINDOW_HOURS * 3600)
    return now - timedelta(seconds=offset_seconds)


def make_row(now: datetime) -> dict:
    ts = random_timestamp(now)
    severity_text, severity_number = weighted_severity()
    service_index = random.randint(0, SERVICE_COUNT - 1)
    endpoint = random.choice(ENDPOINTS)

    return {
        "Timestamp": ts.strftime("%Y-%m-%d %H:%M:%S.%f"),
        "ObservedTimestamp": ts.strftime("%Y-%m-%d %H:%M:%S.%f"),
        "TraceId": random_hex(32),
        "SpanId": random_hex(16),
        "TraceFlags": 1,
        "SeverityText": severity_text,
        "SeverityNumber": severity_number,
        "ServiceName": f"benchmark-seed-svc-{service_index}",
        "Body": random_body(),
        "ResourceSchemaUrl": "",
        "ResourceAttributes": {
            "service.name": f"benchmark-seed-svc-{service_index}",
            "host.name": f"host-{random.randint(1, 20)}",
        },
        "ScopeSchemaUrl": "",
        "ScopeName": "benchmark-seed",
        "ScopeVersion": "",
        "ScopeAttributes": {},
        "LogAttributes": {
            "endpoint": endpoint,
            "request.id": "".join(random.choices(string.hexdigits.lower(), k=12)),
        },
        "EventName": "",
        "EventId": str(uuid.uuid4()),
    }


def post(url: str, body: bytes) -> None:
    req = urllib.request.Request(url, data=body, method="POST")
    try:
        with urllib.request.urlopen(req, timeout=120) as resp:
            resp.read()
    except urllib.error.HTTPError as exc:
        detail = exc.read().decode("utf-8", errors="replace")
        raise RuntimeError(f"ClickHouse insert failed ({exc.code}): {detail}") from exc


def seed(args: argparse.Namespace) -> None:
    insert_url = (
        f"{args.clickhouse_url}/?password={args.password}"
        f"&query=INSERT+INTO+{args.database}.logs+FORMAT+JSONEachRow"
    )
    now = datetime.now(timezone.utc)
    total = args.rows
    written = 0
    start = time.time()

    while written < total:
        batch_size = min(BATCH_SIZE, total - written)
        lines = [json.dumps(make_row(now)) for _ in range(batch_size)]
        body = ("\n".join(lines) + "\n").encode("utf-8")
        post(insert_url, body)
        written += batch_size
        elapsed = time.time() - start
        rate = written / elapsed if elapsed > 0 else 0
        print(f"  seeded {written:,}/{total:,} rows ({rate:,.0f} rows/sec)", file=sys.stderr)

    print(f"Done: {written:,} rows in {time.time() - start:.1f}s.", file=sys.stderr)


def cleanup(args: argparse.Namespace) -> None:
    delete_url = (
        f"{args.clickhouse_url}/?password={args.password}"
        f"&query=ALTER+TABLE+{args.database}.logs+"
        f"DELETE+WHERE+ServiceName+LIKE+%27benchmark-seed-%25%27"
    )
    post(delete_url, b"")
    print("Cleanup mutation submitted (ALTER TABLE ... DELETE is async in ClickHouse - "
          "poll system.mutations if you need to confirm completion).", file=sys.stderr)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--rows", type=int, default=5_000_000, help="Rows to seed (default: 5,000,000).")
    parser.add_argument("--clickhouse-url", default="http://localhost:8123", help="ClickHouse HTTP interface base URL.")
    parser.add_argument("--password", default="flare", help="ClickHouse password (matches docker-compose.yml's default).")
    parser.add_argument("--database", default="clickhousedb", help="Target database name.")
    parser.add_argument("--cleanup", action="store_true", help="Remove previously-seeded benchmark-seed-* rows instead of seeding.")
    args = parser.parse_args()

    if args.cleanup:
        cleanup(args)
    else:
        seed(args)


if __name__ == "__main__":
    main()
