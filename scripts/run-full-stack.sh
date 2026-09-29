#!/usr/bin/env bash
# Brings up the whole Flare v1 stack (ClickHouse, Redis, Flare.Ingest, Flare.Api,
# dockerized dashboard) via docker compose, waits for it to be healthy, then backfills
# an hour of sample data with examples/ExampleApp.Seeder so there's something to see.
#
# Usage: ./scripts/run-full-stack.sh
set -euo pipefail
cd "$(dirname "$0")/.."

echo "==> docker compose up -d (clickhouse, redis, ingest, api, dashboard)"
docker compose up -d --build

echo "==> waiting for api to report healthy..."
for i in $(seq 1 60); do
	status="$(docker compose ps --format '{{.Health}}' api 2>/dev/null || true)"
	if [ "$status" = "healthy" ]; then
		echo "    api is healthy"
		break
	fi
	if [ "$i" -eq 60 ]; then
		echo "    api did not become healthy in time - check 'docker compose logs api'"
		exit 1
	fi
	sleep 2
done

echo "==> backfilling an hour of sample data"
# Every seeder scenario except `pipeline`, whose rules persist and would rewrite everything
# ingested afterwards - see examples/README.md. The seeder's defaults match this compose
# stack's published ports; the flags just follow any port overrides.
scenarios="overview funnel structure external cardinality messaging hosts kubernetes"
dotnet run --project examples/ExampleApp.Seeder -- $scenarios \
	--otlp "http://localhost:${FLARE_INGEST_HTTP_PORT:-4318}" \
	--api "http://localhost:${FLARE_API_PORT:-8080}" \
	--clickhouse "http://localhost:${CLICKHOUSE_HTTP_PORT:-8123}"

cat <<EOF

==> Done. Open the dashboard:

    http://localhost:${FLARE_DASHBOARD_PORT:-7777}

To stop everything:

    docker compose down

To re-seed (replaces the previous run's data rather than adding to it):

    dotnet run --project examples/ExampleApp.Seeder -- $scenarios
EOF
