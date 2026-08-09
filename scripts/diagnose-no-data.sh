#!/usr/bin/env bash
# Diagnoses "dashboard shows no log records" by checking each hop in the pipeline:
# containers healthy -> ClickHouse has rows -> ingest/api logs show no errors.
#
# Usage: ./scripts/diagnose-no-data.sh
set -uo pipefail
cd "$(dirname "$0")/.."

echo "===== docker compose ps ====="
docker compose ps

echo
echo "===== row count in clickhousedb.logs ====="
docker compose exec -T clickhouse clickhouse-client \
	--user default --password "${CLICKHOUSE_PASSWORD:-flare}" \
	--query "SELECT count() FROM clickhousedb.logs" \
	|| echo "(query failed - clickhouse container may not be up/healthy)"

echo
echo "===== last 5 rows (if any) ====="
docker compose exec -T clickhouse clickhouse-client \
	--user default --password "${CLICKHOUSE_PASSWORD:-flare}" \
	--query "SELECT timestamp, service_name, severity_text, body FROM clickhousedb.logs ORDER BY timestamp DESC LIMIT 5 FORMAT Vertical" \
	2>/dev/null

echo
echo "===== flare.ingest last 40 log lines ====="
docker compose logs --tail=40 ingest

echo
echo "===== flare.api last 40 log lines ====="
docker compose logs --tail=40 api

echo
echo "===== done - paste all of the above back ====="
