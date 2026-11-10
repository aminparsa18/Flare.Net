#!/usr/bin/env bash
# Redeploys the public demo on its server: fast-forwards the repo clone, pulls the images named in
# deploy/demo/docker-compose.yml and recreates whatever changed. Run on the server, from anywhere;
# .github/workflows/deploy-demo.yml runs it over SSH. See deploy/demo/README.md.
#
# FLARE_DEMO_DIR: the repository clone (default ~/Flare.Net). Its untracked deploy/demo/.env is kept.
set -euo pipefail

cd "${FLARE_DEMO_DIR:-$HOME/Flare.Net}"

git fetch --quiet origin main
git reset --hard --quiet origin/main

compose=(docker compose -f deploy/demo/docker-compose.yml --env-file deploy/demo/.env)
"${compose[@]}" pull --quiet
if ! "${compose[@]}" up -d --remove-orphans --wait --wait-timeout 600; then
  # Print why, so the workflow log shows the cause without anyone needing a shell on the VM.
  "${compose[@]}" ps -a --format 'table {{.Service}}\t{{.Status}}'
  for svc in $("${compose[@]}" ps -a --format '{{.Service}} {{.Status}}' | awk '/unhealthy|Exited|Restarting/ {print $1}'); do
    echo "---- $svc (last 40 lines)"
    "${compose[@]}" logs --no-color --tail 40 "$svc" 2>&1 | cut -c1-400
  done
  exit 1
fi
docker image prune -f >/dev/null
"${compose[@]}" ps --format 'table {{.Service}}\t{{.Status}}'
