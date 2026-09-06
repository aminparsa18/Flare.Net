#!/usr/bin/env bash
#
# kill-build-processes.sh
#
# Terminates every build/NuGet-related process on this machine:
#   - dotnet build / dotnet restore / dotnet publish
#   - MSBuild / VBCSCompiler
#   - nuget.exe / dotnet-nuget
#   - Aspire CLI's "aspire-managed nuget search" helper processes
#     (these can get orphaned in a stopped state after the known
#     aspire-CLI hang on this machine and won't respond to SIGTERM)
#
# Deliberately leaves IDE/editor tooling alone (VS Code C# Dev Kit's
# Roslyn language server, ServiceHost, etc.) since those are needed
# for editing even when nothing is building.
#
# Usage:
#   ./scripts/kill-build-processes.sh          # graceful TERM, then KILL survivors
#   ./scripts/kill-build-processes.sh --dry-run # just list matching PIDs, kill nothing

set -euo pipefail

DRY_RUN=false
if [[ "${1:-}" == "--dry-run" ]]; then
  DRY_RUN=true
fi

# Patterns for processes to target. Extend this list if new tooling shows up.
PATTERNS=(
  'dotnet build'
  'dotnet restore'
  'dotnet publish'
  'MSBuild\.dll'
  'VBCSCompiler'
  'nuget\.exe'
  'dotnet-nuget'
  'aspire-managed nuget'
)

# Build a single extended-regex alternation from the patterns.
regex=$(IFS='|'; echo "${PATTERNS[*]}")

# Find matching PIDs, excluding this script's own grep/process.
mapfile -t pids < <(
  ps aux | grep -E "$regex" | grep -v grep | grep -v "$0" | awk '{print $2}'
)

if [[ ${#pids[@]} -eq 0 ]]; then
  echo "No build/nuget related processes found."
  exit 0
fi

echo "Found ${#pids[@]} matching process(es):"
# -p wants a single comma-separated list, not one -p per pid / space-separated args -
# BSD ps (macOS) errors out on the latter ("illegal argument: -o") where GNU ps tolerates
# it, so join explicitly rather than relying on "${pids[@]}" splitting to keep this
# portable across both.
ps -p "$(IFS=,; echo "${pids[*]}")" -o pid,state,command | sed 's/^/  /'

if $DRY_RUN; then
  echo
  echo "(dry run — nothing killed)"
  exit 0
fi

echo
echo "Sending SIGTERM..."
kill -TERM "${pids[@]}" 2>/dev/null || true
sleep 1

# Anything still alive (e.g. stuck in a stopped/traced state) gets SIGKILL.
survivors=()
for pid in "${pids[@]}"; do
  if kill -0 "$pid" 2>/dev/null; then
    survivors+=("$pid")
  fi
done

if [[ ${#survivors[@]} -gt 0 ]]; then
  echo "Force-killing ${#survivors[@]} survivor(s): ${survivors[*]}"
  kill -KILL "${survivors[@]}" 2>/dev/null || true
fi

echo "Done."