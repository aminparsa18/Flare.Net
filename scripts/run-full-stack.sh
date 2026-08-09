#!/usr/bin/env bash
# Brings up the whole Flare v1 stack (ClickHouse, Redis, Flare.Ingest, Flare.Api,
# dockerized dashboard) via docker compose, waits for it to be healthy, then runs
# the example log generator for a bit so there's actual data to see.
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

echo "==> building the log generator"
# Built once, then launched by invoking the DLL directly - not `dotnet run &`, which
# forks a child process for the actual app and leaves it orphaned (still generating
# logs) when the wrapper's own PID gets killed below.
generator_dll="$(mktemp -d)/loggen"
dotnet build examples/ExampleApp.LogGenerator -c Release -o "$generator_dll" --nologo -v quiet

echo "==> generating sample log traffic for 30s"
# ExampleApp.LogGenerator normally gets ConnectionStrings__flare injected by Aspire's
# .WithReference(flare) on the AppHost side; running it standalone (outside Aspire, against
# this docker-compose stack) needs that env var set by hand, pointing at Flare.Ingest's
# host-exposed OTLP/gRPC port - AddFlareOtlpExporter throws on startup without it.
# `timeout` is a GNU coreutils command, not available on macOS by default - backgrounding
# + sleep + kill is the portable equivalent.
ConnectionStrings__flare="http://localhost:${FLARE_INGEST_GRPC_PORT:-4317}" \
	dotnet "$generator_dll/ExampleApp.LogGenerator.dll" &
generator_pid=$!
sleep 30
kill "$generator_pid" 2>/dev/null || true
wait "$generator_pid" 2>/dev/null || true

cat <<EOF

==> Done. Open the dashboard:

    http://localhost:3000

To stop everything:

    docker compose down

To re-run the log generator any time for more traffic:

    ConnectionStrings__flare="http://localhost:${FLARE_INGEST_GRPC_PORT:-4317}" dotnet run --project examples/ExampleApp.LogGenerator
EOF
