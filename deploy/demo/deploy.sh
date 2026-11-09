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
"${compose[@]}" up -d --remove-orphans --wait --wait-timeout 600
docker image prune -f >/dev/null
"${compose[@]}" ps --format 'table {{.Service}}\t{{.Status}}'
