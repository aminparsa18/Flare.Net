#!/usr/bin/env bash
# Runs the flows on a physical iPhone and asserts on the telemetry in ClickHouse, reusing run.sh's check_NN functions.
# Maestro cannot drive a physical iOS device (simulators and Android only), so the example app runs each scenario itself:
# MainPage.AutoRun.cs reads FLARE_AUTORUN (steps) / FLARE_ENDPOINT / FLARE_VERSION from the launch environment.
# Usage: ./run-ios-device.sh [flow-prefix ...]     e.g. ./run-ios-device.sh 01 12   (no arguments: every supported flow)
# Needs: the app installed and its Local Network permission allowed once, a CoreDevice id (`xcrun devicectl list devices`
# -> IOS_DEVICE), the Mac's LAN address (FLARE_HOST, default en0), and `docker compose up`.
# Not covered, because they need a tap or a system toggle: 16 (airplane mode), 18 (feedback prompt). 08 and 10 are partial.
set -u
export FLARE_RUN_LIB=1
# shellcheck source=run.sh
. "$(dirname "$0")/run.sh"
set +e

IOS_DEVICE=${IOS_DEVICE:?set IOS_DEVICE to the CoreDevice id from: xcrun devicectl list devices}
HOST=${FLARE_HOST:-$(ipconfig getifaddr en0)}
APP=com.flare.exampleapp
ENDPOINT="http://$HOST:4318"

# $1 = steps, $2 = service.version override (optional), $3 = seconds to let the scenario finish.
launch() {
  local env="{\"FLARE_ENDPOINT\":\"$ENDPOINT\",\"FLARE_AUTORUN\":\"$1\"${2:+,\"FLARE_VERSION\":\"$2\"}}"
  xcrun devicectl device process launch --device "$IOS_DEVICE" --terminate-existing --environment-variables "$env" "$APP" >/dev/null 2>&1 \
    || { echo "    FAIL launch (device locked or unreachable?)"; return 1; }
  sleep "$3"
}

# iOS variants of the checks that name Android-only details: a hang has no stack on iOS (ADR-0179) and the stack
# sampler is Android only (ADR-0181).
check_08() { expect "app.hang reported while the UI thread was frozen" "Name='app.hang' AND SpanAttributes['hang.threshold_ms'] != ''"; }
check_14() { expect "screen.load for SlowPage of at least 500 ms" "Name='screen.load' AND SpanAttributes['screen.name'] LIKE '%SlowPage' AND DurationNano >= 500000000"; }
# MetricKit delivers a crash report up to a day after the crash, so this cannot pass right after the run.
check_10() { expect "native crash reported (MetricKit; may arrive up to a day later)" "Name='app.unhandled_exception' AND SpanAttributes['crash.kind']='NativeCrash'"; }

run_flow() { # number
  case "$1" in
    01) launch "http,flush" "" 8 ;;
    02) launch "push,pop,flush" "" 9 ;;
    03) launch "custom,flush" "" 7 ;;
    04) launch "record,flush" "" 7 ;;
    05) launch "unhandled,flush" "" 8 ;;
    06) launch "custom,flush" "" 7 ;;
    07) launch "scrub,flush" "" 7 ;;
    08) launch "freeze,flush" "" 30 ;;
    09) launch "crash" "" 8 && launch "flush" "" 6 ;;
    10) launch "nativecrash" "" 8 && launch "flush" "" 6 ;;
    11) launch "custom,flush" "2.0.0" 7 ;;
    12) # cold start, then Settings over the app and the app again without terminating it: a warm start
      launch "flush" "" 6 && xcrun devicectl device process launch --device "$IOS_DEVICE" com.apple.Preferences >/dev/null 2>&1 && sleep 3 \
        && xcrun devicectl device process launch --device "$IOS_DEVICE" "$APP" >/dev/null 2>&1 && sleep 3 \
        && xcrun devicectl device process launch --device "$IOS_DEVICE" com.apple.Preferences >/dev/null 2>&1 && sleep 3 \
        && xcrun devicectl device process launch --device "$IOS_DEVICE" "$APP" >/dev/null 2>&1 && sleep 6 ;;
    13) launch "jank,push,pop,flush" "" 14 ;;
    14) launch "slow,pop,flush" "" 9 ;;
    15) launch "record,release,flush" "1.0.0" 8 && launch "record,release,flush" "2.0.0" 8 ;;
    17) launch "wait6,push,wait12,pop,wait6,record,flush" "" 40 ;;
  esac
}

for p in 01 02 03 04 05 06 07 08 09 10 11 12 13 14 15 17; do
  if [ $# -gt 0 ]; then case " $* " in *" $p "*) ;; *) continue ;; esac; fi
  echo "== $p"
  SINCE=$(q "SELECT formatDateTime(now64(), '%Y-%m-%d %H:%i:%S')")
  if ! run_flow "$p"; then FAIL=$((FAIL+1)); continue; fi
  if "check_$p"; then
    if expect_none "no POST /v1/* spans" "Name='POST' AND SpanAttributes['url.full'] LIKE '%/v1/%'"; then PASS=$((PASS+1)); else FAIL=$((FAIL+1)); fi
  else FAIL=$((FAIL+1)); fi
done
echo; echo "passed: $PASS  failed: $FAIL"
[ "$FAIL" -eq 0 ]
