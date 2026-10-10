#!/usr/bin/env bash
# Runs the Maestro flows against a connected Android device and asserts on the telemetry in ClickHouse.
# Usage: ./run.sh [flow-prefix ...]     e.g. ./run.sh 03 08   (no arguments: every flow)
# Needs: maestro, adb with one device (or ANDROID_SERIAL), and `docker compose up` (ClickHouse container below).
set -u
cd "$(dirname "$0")"
CH_CONTAINER=${CH_CONTAINER:-flarenet-clickhouse-1}
DB=${CH_DB:-clickhousedb}
SERVICE=example-maui-app
LOGDIR=${LOGDIR:-${TMPDIR:-/tmp}}
PASS=0; FAIL=0

q() { docker exec "$CH_CONTAINER" clickhouse-client --database "$DB" -q "$1"; }

# $1 = clickhouse "since" timestamp. Each check prints a count; a failed check returns 1.
spans() { q "SELECT count() FROM spans WHERE ServiceName='$SERVICE' AND IngestedAt >= toDateTime64('$SINCE', 9) AND $1"; }

expect() { # description, where-clause (waits up to 40 s for ingest)
  local n=0 i
  for i in $(seq 1 20); do
    n=$(spans "$2"); [ "${n:-0}" -ge 1 ] && { echo "    ok   $1 ($n)"; return 0; }
    sleep 2
  done
  echo "    FAIL $1 (0 rows)"; return 1
}
expect_none() {
  local n; n=$(spans "$2")
  [ "${n:-0}" -eq 0 ] && { echo "    ok   $1"; return 0; }
  echo "    FAIL $1 ($n rows)"; return 1
}

# Freeze scenario: Maestro's own taps block until the app recovers, so tap the screen mid-freeze over adb in the background.
# On the test device (Samsung S918B, Android 16) this raised no ANR dialog within 9 s, and uiautomator cannot dump the
# screen while Maestro holds UiAutomation, so the dialog / "Wait" step is not automated. The app.hang span is asserted.
freeze_helper() {
  until grep -q "Scrolling DOWN.*COMPLETED" "$1" 2>/dev/null; do sleep 0.3; done
  sleep 2
  adb shell input tap 700 1000 >/dev/null 2>&1
}

check_01() { expect "GET /health span" "Name='GET' AND SpanAttributes['url.full'] LIKE '%/health'"; }
check_02() { expect "navigation Push to DetailPage" "Name='navigation' AND SpanAttributes['navigation.source']='Push' AND SpanAttributes['screen.name'] LIKE '%DetailPage'"; }
check_03() { expect "example.custom_work span" "Name='example.custom_work'"; }
check_04() { expect "handled exception event" "has(\`Events.Name\`,'exception') AND arrayExists(m -> m['exception.message']='Handled example exception', \`Events.Attributes\`)"; }
check_05() { expect "unobserved task exception" "Name='app.unhandled_exception' AND arrayExists(m -> m['exception.message'] LIKE '%Unobserved task exception%', \`Events.Attributes\`)"; }
check_06() { expect "example.custom_work span after Flush" "Name='example.custom_work'"; }
check_07() {
  expect "example.scrubbed email = [redacted]" "Name='example.scrubbed' AND SpanAttributes['example.email']='[redacted]'" &&
  expect_none "example.noisy dropped" "Name='example.noisy'"
}
check_08() {
  expect "app.hang with a Java stack" "Name='app.hang' AND (SpanAttributes['hang.stacktrace'] LIKE '%java.%' OR SpanAttributes['hang.stacktrace'] LIKE '%android.%')"; }
check_09() {
  # The crash flush blocks the UI thread; the watchdog must not report that as a hang.
  expect "managed crash: app.unhandled_exception escaped=true" "Name='app.unhandled_exception' AND SpanAttributes['exception.escaped']='true' AND SpanAttributes['crash.native']!='true' AND arrayExists(m -> m['exception.message']='Deliberate crash from ExampleApp.Maui', \`Events.Attributes\`)" &&
  expect_none "no app.hang after the crash" "Name='app.hang'"
}
check_10() {
  expect "native crash reported on relaunch" "Name='app.unhandled_exception' AND SpanAttributes['crash.kind']='NativeCrash'" &&
  # Android 12+ tombstones are protobuf: this proves NativeStackRenderer decoded a real one (signal + backtrace frames).
  expect "native crash stack is a decoded tombstone" "Name='app.unhandled_exception' AND SpanAttributes['crash.kind']='NativeCrash' AND arrayExists(m -> m['exception.stacktrace'] LIKE '%Signal: SIGABRT%' AND m['exception.stacktrace'] LIKE '%backtrace (tid%' AND m['exception.stacktrace'] LIKE '%#00 pc %', \`Events.Attributes\`)"; }
check_11() { expect "service.version = 2.0.0" "ResourceAttributes['service.version']='2.0.0' AND Name='example.custom_work'"; }
check_12() {
  expect "cold app.start" "Name='app.start' AND SpanAttributes['app.start.type']='cold'" &&
  expect "warm app.start" "Name='app.start' AND SpanAttributes['app.start.type']='warm'"
}
check_13() {
  expect "screen.frames with slow and frozen frames on MainPage" "Name='screen.frames' AND SpanAttributes['screen.name'] LIKE '%MainPage' AND toUInt64OrZero(SpanAttributes['frames.slow']) >= 50 AND toUInt64OrZero(SpanAttributes['frames.frozen']) >= 1"; }
check_14() {
  expect "screen.load for SlowPage of at least 500 ms" "Name='screen.load' AND SpanAttributes['screen.name'] LIKE '%SlowPage' AND DurationNano >= 500000000" &&
  expect "that screen.load carries sampled UI-thread stacks" "Name='screen.load' AND SpanAttributes['screen.name'] LIKE '%SlowPage' AND toUInt32OrZero(SpanAttributes['profile.samples']) >= 3 AND SpanAttributes['profile.stacks'] LIKE '%;%'"
}

# Releases demo: mark both versions through the API (Member/Admin token in FLARE_TOKEN when auth is on), then ask which
# errors 2.0.0 introduced. The shared handled error was already seen under 1.0.0 (or earlier), so it must not be listed.
API=${FLARE_API:-http://localhost:8080}
mark() { curl -s -o /dev/null -XPUT "$API/api/releases" -H 'Content-Type: application/json' ${FLARE_TOKEN:+-H "Authorization: Bearer $FLARE_TOKEN"} -d "{\"service\":\"$SERVICE\",\"version\":\"$1\"}"; }
check_15() {
  expect "2.0.0 release error recorded" "ResourceAttributes['service.version']='2.0.0' AND arrayExists(m -> m['exception.message'] = 'Release demo error 2.0.0', \`Events.Attributes\`)" || return 1
  mark 1.0.0; mark 2.0.0
  local out; out=$(curl -s ${FLARE_TOKEN:+-H "Authorization: Bearer $FLARE_TOKEN"} "$API/api/releases/errors?service=$SERVICE&version=2.0.0")
  case "$out" in *"Release demo error 2.0.0"*) ;; *) echo "    FAIL 2.0.0 should introduce its own error: $out"; return 1 ;; esac
  case "$out" in *"Handled example exception"*) echo "    FAIL the shared error was seen before 2.0.0: $out"; return 1 ;; esac
  echo "    ok   Releases lists only the 2.0.0 error as new in 2.0.0"
}

for f in 01-http 02-navigate 03-custom 04-record-exception 05-unhandled-task 06-flush 07-scrubbing 08-freeze 09-managed-crash 10-native-crash 11-service-version 12-warm-start 13-jank 14-slow-screen 15-releases; do
  p=${f%%-*}
  if [ $# -gt 0 ]; then case " $* " in *" $p "*) ;; *) continue ;; esac; fi
  echo "== $f"
  adb reverse tcp:4318 tcp:4318 >/dev/null
  SINCE=$(q "SELECT formatDateTime(now64(), '%Y-%m-%d %H:%i:%S')")
  [ "$p" = 08 ] && { rm -f "$LOGDIR/maestro-$f.log"; freeze_helper "$LOGDIR/maestro-$f.log" & HELPER=$!; }
  if ! maestro test "$f.yaml" >"$LOGDIR/maestro-$f.log" 2>&1; then
    [ "$p" = 08 ] && kill $HELPER 2>/dev/null
    echo "    FAIL maestro flow (see $LOGDIR/maestro-$f.log)"; FAIL=$((FAIL+1)); continue
  fi
  [ "$p" = 08 ] && wait $HELPER
  echo "    ok   maestro flow"
  if "check_$p"; then
    # Every flow: the SDK must not trace its own OTLP export requests (PR #606).
    if expect_none "no POST /v1/* spans" "Name='POST' AND SpanAttributes['url.full'] LIKE '%/v1/%'"; then PASS=$((PASS+1)); else FAIL=$((FAIL+1)); fi
  else FAIL=$((FAIL+1)); fi
done
echo; echo "passed: $PASS  failed: $FAIL"
[ "$FAIL" -eq 0 ]
