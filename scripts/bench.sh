#!/usr/bin/env bash
# Runs Flare's serialization benchmarks and builds the HTML report.
#
#   scripts/bench.sh                     all suites, full accuracy
#   scripts/bench.sh component           component suites only (single codec / operation / payload)
#   scripts/bench.sh workflow            end-to-end suites only (server stage + client stage)
#   scripts/bench.sh api-codec decode    named suites (any mix, see SUITES below)
#   scripts/bench.sh --quick             BenchmarkDotNet short job + 150 ms per tinybench case (smoke run)
#   scripts/bench.sh --out DIR           write the run somewhere other than docs-internal/benchmarks/<date>/
#   scripts/bench.sh --capture [URL]     re-record the captured fixtures from a running, seeded Flare.Api, then exit
#   scripts/bench.sh --report DIR        rebuild DIR/report.html from DIR/raw without running anything
#
# A run directory holds what gets committed: report.html, results.json (normalized), env.json and
# raw/ (BenchmarkDotNet brief JSON + CSV, tinybench JSON, fixture sizes). Bulky intermediate output
# (BenchmarkDotNet artifacts, exported fixture bytes) goes to artifacts/bench/, which is gitignored.
# See src/Flare.Benchmarks/README.md.
set -euo pipefail
cd "$(dirname "$0")/.."
ROOT="$(pwd)"

# suite name -> where it runs and what it is
DOTNET_COMPONENT="redis-buffer api-codec request-codec compression request-decompression"
DOTNET_WORKFLOW="server-stage"
NODE_COMPONENT="decode decompress request-encode"
NODE_WORKFLOW="client-stage"

bdn_class() {
	case "$1" in
		redis-buffer) echo "*RedisBufferCodecBenchmarks*" ;;
		api-codec) echo "*ApiCodecBenchmarks*" ;;
		request-codec) echo "*RequestCodecBenchmarks*" ;;
		compression) echo "*Component.CompressionBenchmarks*" ;;
		request-decompression) echo "*RequestDecompressionBenchmarks*" ;;
		server-stage) echo "*ServerStageBenchmarks*" ;;
	esac
}
node_file() {
	case "$1" in
		client-stage) echo "bench/workflow/$1.bench.ts" ;;
		*) echo "bench/component/$1.bench.ts" ;;
	esac
}

quick=false
out=""
selected=()
while [ $# -gt 0 ]; do
	case "$1" in
		--quick) quick=true ;;
		--out) out="$2"; shift ;;
		--capture)
			node scripts/bench-report/capture.mjs "${2:-http://localhost:8080}"
			exit 0 ;;
		--report)
			node scripts/bench-report/build-report.mjs "$2"
			exit 0 ;;
		-h|--help) sed -n '2,17p' "$0"; exit 0 ;;
		all) selected+=($DOTNET_COMPONENT $DOTNET_WORKFLOW $NODE_COMPONENT $NODE_WORKFLOW) ;;
		component) selected+=($DOTNET_COMPONENT $NODE_COMPONENT) ;;
		workflow) selected+=($DOTNET_WORKFLOW $NODE_WORKFLOW) ;;
		*)
			if [[ " $DOTNET_COMPONENT $DOTNET_WORKFLOW $NODE_COMPONENT $NODE_WORKFLOW " == *" $1 "* ]]; then
				selected+=("$1")
			else
				echo "Unknown suite or option '$1'. Suites: $DOTNET_COMPONENT $DOTNET_WORKFLOW $NODE_COMPONENT $NODE_WORKFLOW" >&2
				exit 2
			fi ;;
	esac
	shift
done
[ ${#selected[@]} -eq 0 ] && selected=($DOTNET_COMPONENT $DOTNET_WORKFLOW $NODE_COMPONENT $NODE_WORKFLOW)

if [ -z "$out" ]; then
	out="docs-internal/benchmarks/$(date +%F)"
	n=2
	while [ -e "$out" ]; do out="docs-internal/benchmarks/$(date +%F)-$n"; n=$((n + 1)); done
fi
ART="$ROOT/artifacts/bench"
mkdir -p "$out/raw/dotnet" "$out/raw/node" "$ART"
out="$(cd "$out" && pwd)"

dotnet_filters=()
node_suites=()
for s in "${selected[@]}"; do
	if [[ " $DOTNET_COMPONENT $DOTNET_WORKFLOW " == *" $s "* ]]; then dotnet_filters+=("$(bdn_class "$s")"); else node_suites+=("$s"); fi
done

echo "==> Run: ${selected[*]} (quick=$quick) -> $out"

# Release build also regenerates the dashboard's MemoryPack TypeScript decoders (Flare.Api codegen).
echo "==> Building Flare.Benchmarks (Release)"
dotnet build -c Release src/Flare.Benchmarks -clp:ErrorsOnly -v quiet
echo "==> Exporting fixture bytes"
rm -rf "$ART/fixtures"
dotnet run -c Release --no-build --project src/Flare.Benchmarks -- export "$ART/fixtures"
cp "$ART/fixtures/manifest.json" "$out/raw/sizes.json"

if [ ${#dotnet_filters[@]} -gt 0 ]; then
	echo "==> BenchmarkDotNet: ${dotnet_filters[*]}"
	rm -rf "$ART/bdn"
	job=()
	$quick && job=(--job short)
	dotnet run -c Release --no-build --project src/Flare.Benchmarks -- \
		--filter "${dotnet_filters[@]}" --artifacts "$ART/bdn" "${job[@]}"
	cp "$ART"/bdn/results/*-report-brief.json "$ART"/bdn/results/*-report.csv "$out/raw/dotnet/"
fi

if [ ${#node_suites[@]} -gt 0 ]; then
	time_ms=500
	$quick && time_ms=150
	for s in "${node_suites[@]}"; do
		echo "==> tinybench: $s"
		(cd src/dashboard && FLARE_BENCH_FIXTURES="$ART/fixtures" npx tsx --import ./bench/support/register.mjs \
			"$(node_file "$s")" --json "$out/raw/node/$s.json" --time "$time_ms")
	done
fi

node scripts/bench-report/env.mjs "$out/env.json" "suites=${selected[*]}" "quick=$quick"
node scripts/bench-report/build-report.mjs "$out"
