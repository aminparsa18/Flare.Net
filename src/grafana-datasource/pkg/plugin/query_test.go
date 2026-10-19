package plugin

import (
	"encoding/json"
	"testing"
	"time"
)

func ms(n int64) time.Time { return time.UnixMilli(n) }

func TestResolveWindow(t *testing.T) {
	t.Run("rounds the range up to whole minutes and ends where the range ends", func(t *testing.T) {
		end := int64(90*60000 + 1)
		w := resolveWindow(timeWindow{From: ms(0), To: ms(end)})
		if w.WindowMinutes != 91 || w.EndUnixMs != end || w.Clamped {
			t.Fatalf("got %+v", w)
		}
	})
	t.Run("raises a tiny range to the 5 minute minimum", func(t *testing.T) {
		if got := resolveWindow(timeWindow{From: ms(1000), To: ms(2000)}).WindowMinutes; got != 5 {
			t.Fatalf("got %d", got)
		}
	})
	t.Run("cuts a range over 7 days and says so", func(t *testing.T) {
		w := resolveWindow(timeWindow{From: ms(0), To: ms(30 * 24 * 3600_000)})
		if w.WindowMinutes != 10080 || !w.Clamped {
			t.Fatalf("got %+v", w)
		}
	})
	t.Run("exactly 7 days is not clamped", func(t *testing.T) {
		w := resolveWindow(timeWindow{From: ms(0), To: ms(10080 * 60000)})
		if w.WindowMinutes != 10080 || w.Clamped {
			t.Fatalf("got %+v", w)
		}
	})
}

func TestSplitServicesAndCalls(t *testing.T) {
	cases := map[string][]string{
		"":            nil,
		" a , b ,,c ": {"a", "b", "c"},
		"{api,web}":   {"api", "web"},
		"api":         {"api"},
	}
	for in, want := range cases {
		got := splitServices(in)
		if len(got) != len(want) {
			t.Fatalf("%q: got %v want %v", in, got, want)
		}
		for i := range got {
			if got[i] != want[i] {
				t.Fatalf("%q: got %v want %v", in, got, want)
			}
		}
	}
	if got := serviceCalls(""); len(got) != 1 || got[0] != "" {
		t.Fatalf("no selection is one call for every service, got %v", got)
	}
	if got := serviceCalls("a,b"); len(got) != 2 {
		t.Fatalf("several are one call each, got %v", got)
	}
}

func mustJSON(t *testing.T, v any) string {
	t.Helper()
	b, err := json.Marshal(v)
	if err != nil {
		t.Fatal(err)
	}
	return string(b)
}

func TestRequests(t *testing.T) {
	w := resolvedWindow{WindowMinutes: 60, EndUnixMs: 3600_000}

	t.Run("sessions request omits unset service and version", func(t *testing.T) {
		got := mustJSON(t, newSessionsRequest(flareQuery{}, w, ""))
		if got != `{"windowMinutes":60,"endUnixMs":3600000,"errorsOnly":false}` {
			t.Fatal(got)
		}
		got = mustJSON(t, newSessionsRequest(flareQuery{Version: " 2.1 ", ErrorsOnly: true}, w, "app"))
		if got != `{"windowMinutes":60,"endUnixMs":3600000,"service":"app","version":"2.1","errorsOnly":true}` {
			t.Fatal(got)
		}
	})

	t.Run("errors request sends ISO bounds, the services list and topN only when set", func(t *testing.T) {
		win := timeWindow{From: ms(0), To: ms(1000)}
		got := mustJSON(t, newErrorsRequest(flareQuery{Service: "api,web", TopN: 5}, win))
		want := `{"filter":{"from":"1970-01-01T00:00:00.000Z","to":"1970-01-01T00:00:01.000Z","services":["api","web"]},"topN":5}`
		if got != want {
			t.Fatal(got)
		}
		got = mustJSON(t, newErrorsRequest(flareQuery{}, win))
		if got != `{"filter":{"from":"1970-01-01T00:00:00.000Z","to":"1970-01-01T00:00:01.000Z"}}` {
			t.Fatal(got)
		}
	})

	t.Run("trend bucket: the query wins, else the interval in whole minutes, else left to Flare", func(t *testing.T) {
		if got := trendBucketMinutes(flareQuery{BucketMinutes: 15}, time.Minute); got != 15 {
			t.Fatal(got)
		}
		if got := trendBucketMinutes(flareQuery{}, 90*time.Second); got != 2 {
			t.Fatal(got)
		}
		if got := trendBucketMinutes(flareQuery{}, time.Millisecond); got != 1 {
			t.Fatal(got)
		}
		if got := trendBucketMinutes(flareQuery{}, 0); got != 0 {
			t.Fatal(got)
		}
		got := mustJSON(t, newTrendRequest(flareQuery{}, w, "api", 0))
		if got != `{"windowMinutes":60,"endUnixMs":3600000,"service":"api"}` {
			t.Fatal(got)
		}
	})

	t.Run("timeline request scans the dashboard range for the trimmed session id", func(t *testing.T) {
		got := mustJSON(t, newTimelineRequest(flareQuery{SessionID: " abc "}, timeWindow{From: ms(10), To: ms(20)}))
		if got != `{"sessionId":"abc","fromUnixMs":10,"toUnixMs":20}` {
			t.Fatal(got)
		}
	})

	t.Run("performance request carries service and version only when set", func(t *testing.T) {
		if got := mustJSON(t, newPerformanceRequest(flareQuery{}, w, "")); got != `{"windowMinutes":60,"endUnixMs":3600000}` {
			t.Fatal(got)
		}
		got := mustJSON(t, newPerformanceRequest(flareQuery{Version: " 2.1 "}, w, "app"))
		if got != `{"windowMinutes":60,"endUnixMs":3600000,"service":"app","version":"2.1"}` {
			t.Fatal(got)
		}
	})
}
