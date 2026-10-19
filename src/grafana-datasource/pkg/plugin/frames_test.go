package plugin

import (
	"testing"

	"github.com/grafana/grafana-plugin-sdk-go/data"
)

func fieldNamed(t *testing.T, f *data.Frame, name string) *data.Field {
	t.Helper()
	for _, field := range f.Fields {
		if field.Name == name {
			return field
		}
	}
	t.Fatalf("frame %q has no field %q (has %v)", f.Name, name, fieldNames(f))
	return nil
}

func fieldNames(f *data.Frame) []string {
	names := make([]string, len(f.Fields))
	for i, field := range f.Fields {
		names[i] = field.Name
	}
	return names
}

func floatAt(t *testing.T, f *data.Field, i int) *float64 {
	t.Helper()
	v, ok := f.At(i).(*float64)
	if !ok {
		t.Fatalf("field %q is %T, not *float64", f.Name, f.At(i))
	}
	return v
}

func TestCrashFreeAndShare(t *testing.T) {
	if got := crashFree(200, 2); got == nil || *got < 98.999 || *got > 99.001 {
		t.Fatalf("crash-free of 200/2 = %v", got)
	}
	if crashFree(0, 0) != nil || share(0, 0) != nil {
		t.Fatal("nothing measured must be nil")
	}
	if got := share(200, 10); got == nil || *got != 5 {
		t.Fatalf("share = %v", got)
	}
}

func TestSessionsFrame(t *testing.T) {
	rows := []appSession{{SessionID: "s1", ServiceName: "app", Version: "2.0", FirstSeenUnixMs: 1000, LastSeenUnixMs: 2000,
		SpanCount: 5, TraceCount: 2, ErrorCount: 1, Screens: []string{"Home", "Cart"}}}
	f := sessionsFrame("A", rows, true, true, "http://flare/")
	if f.RefID != "A" || f.Rows() != 1 {
		t.Fatalf("refId %q rows %d", f.RefID, f.Rows())
	}
	if fieldNamed(t, f, "screens").At(0) != "Home, Cart" {
		t.Fatal("screens must be joined")
	}
	if links := fieldNamed(t, f, "sessionId").Config.Links; len(links) != 1 || links[0].URL != "http://flare/sessions/${__value.raw}" {
		t.Fatalf("links %+v", links)
	}
	if f.Meta == nil || len(f.Meta.Notices) != 2 {
		t.Fatalf("a truncated and clamped result needs two notices, got %+v", f.Meta)
	}
	if links := fieldNamed(t, sessionsFrame("A", rows, false, false, ""), "sessionId").Config.Links; len(links) != 0 {
		t.Fatal("no dashboard URL means no link")
	}
}

func TestReleaseHealthFrame(t *testing.T) {
	resp := releaseHealthResponse{Versions: []releaseHealthVersion{
		{Version: "2.0", Sessions: 100, CrashedSessions: 5, Users: 40, CrashedUsers: 2},
		{Version: "", Sessions: 0},
	}}
	f := releaseHealthFrame("A", []serviceResponse[releaseHealthResponse]{{Response: resp}}, false)
	if f.Fields[0].Name != "version" {
		t.Fatalf("no Service column without a service, got %v", fieldNames(f))
	}
	if got := floatAt(t, fieldNamed(t, f, "crashFreeSessions"), 0); got == nil || *got != 95 {
		t.Fatalf("crash-free sessions %v", got)
	}
	if floatAt(t, fieldNamed(t, f, "crashFreeSessions"), 1) != nil {
		t.Fatal("a version without sessions has no rate")
	}
	if fieldNamed(t, f, "version").At(1) != "(none)" {
		t.Fatal("an empty version reads (none)")
	}
	withSvc := releaseHealthFrame("A", []serviceResponse[releaseHealthResponse]{{Service: "app", Response: resp}}, false)
	if withSvc.Fields[0].Name != "service" {
		t.Fatalf("got %v", fieldNames(withSvc))
	}
}

func TestReleaseHealthTrendFrames(t *testing.T) {
	resp := releaseHealthTrendResponse{Points: []releaseHealthTrendPoint{
		{BucketUnixMs: 2000, Version: "2.0", Sessions: 10, CrashedSessions: 1},
		{BucketUnixMs: 1000, Version: "2.0", Sessions: 10, CrashedSessions: 0},
		{BucketUnixMs: 1000, Version: "1.0", Sessions: 0},
	}}
	frames := releaseHealthTrendFrames("A", []serviceResponse[releaseHealthTrendResponse]{{Service: "app", Response: resp}}, "crashFreeSessions", false)
	if len(frames) != 2 {
		t.Fatalf("one frame per version, got %d", len(frames))
	}
	if frames[0].Fields[1].Labels["version"] != "1.0" || frames[1].Fields[1].Labels["version"] != "2.0" {
		t.Fatal("versions must be sorted")
	}
	if frames[1].Fields[1].Labels["service"] != "app" || frames[1].Fields[1].Config.DisplayNameFromDS != "app 2.0" {
		t.Fatalf("labels %v display %q", frames[1].Fields[1].Labels, frames[1].Fields[1].Config.DisplayNameFromDS)
	}
	if frames[1].Meta.Type != data.FrameTypeTimeSeriesMulti {
		t.Fatalf("an alertable series needs the multi time series type, got %q", frames[1].Meta.Type)
	}
	values := frames[1].Fields[1]
	if got := floatAt(t, values, 0); got == nil || *got != 100 {
		t.Fatalf("points must be time sorted: first value %v", got)
	}
	if got := floatAt(t, values, 1); got == nil || *got != 90 {
		t.Fatalf("second value %v", got)
	}
	if floatAt(t, frames[0].Fields[1], 0) != nil {
		t.Fatal("a bucket with no sessions is a gap")
	}

	counts := releaseHealthTrendFrames("A", []serviceResponse[releaseHealthTrendResponse]{{Response: resp}}, "sessions", false)
	if got := floatAt(t, counts[1].Fields[1], 0); got == nil || *got != 10 || counts[1].Fields[1].Config.Unit != "" {
		t.Fatalf("sessions series %v", got)
	}
	if _, has := counts[1].Fields[1].Labels["service"]; has {
		t.Fatal("no service label without a service")
	}
	if unknown := releaseHealthTrendFrames("A", []serviceResponse[releaseHealthTrendResponse]{{Response: resp}}, "bogus", false); unknown[1].Fields[1].Name != "Crash-free sessions" {
		t.Fatal("an unknown metric falls back to crash-free sessions")
	}
}

func TestReleasesFrame(t *testing.T) {
	n := int64(3)
	f := releasesFrame("A", []release{{Service: "app", Version: "2.0", Notes: "fix", DeployedAt: "2026-10-18T20:00:00Z", NewErrorCount: &n}})
	if f.Fields[0].Name != "time" {
		t.Fatal("the first field is the annotation time")
	}
	if got := fieldNamed(t, f, "time").At(0).(interface{ UnixMilli() int64 }).UnixMilli(); got != 1792353600000 {
		t.Fatalf("deployedAt parsed to %d", got)
	}
	if fieldNamed(t, f, "title").At(0) != "app 2.0" || fieldNamed(t, f, "text").At(0) != "fix" {
		t.Fatal("annotation title and text")
	}
	if got := fieldNamed(t, f, "newErrors").At(0).(*int64); got == nil || *got != 3 {
		t.Fatal("new errors")
	}
	if fieldNamed(t, f, "regressions").At(0).(*int64) != nil {
		t.Fatal("an unknown count is null")
	}
}

func TestSessionTimelineFrame(t *testing.T) {
	resp := timelineResponse{SessionID: "s1", ServiceName: "app", Version: "2.0", Os: "iOS", Device: "iPhone", Truncated: true,
		Events: []timelineEvent{{TraceID: "t1", SpanID: "p1", Name: "screen.load", StartUnixMs: 1000, DurationMs: 12.5, IsError: true, ExceptionType: "IOException"}}}
	f := sessionTimelineFrame("A", resp, "http://flare/")
	if f.Name != "Session s1" || f.Fields[0].Name != "time" {
		t.Fatalf("name %q first %q", f.Name, f.Fields[0].Name)
	}
	if fieldNamed(t, f, "duration").Config.Unit != "ms" || fieldNamed(t, f, "error").At(0) != true {
		t.Fatal("duration unit and error flag")
	}
	if links := fieldNamed(t, f, "traceId").Config.Links; len(links) != 1 || links[0].URL != "http://flare/traces/${__value.raw}" {
		t.Fatalf("links %+v", links)
	}
	if len(f.Meta.Notices) != 1 || f.Meta.Custom.(map[string]any)["version"] != "2.0" {
		t.Fatalf("meta %+v", f.Meta)
	}
}

func TestPerformanceFrames(t *testing.T) {
	perf := performanceResponse{
		Starts: []appStartStat{{Version: "2.0", Type: "cold", Count: 4, P50Ms: 800, P95Ms: 1500}},
		Screens: []screenPerformance{
			{Screen: "Home", Loads: 3, Frames: 200, SlowFrames: 10, FrozenFrames: 2},
			{Screen: "Idle"},
		},
	}
	starts := appStartsFrame("A", []serviceResponse[performanceResponse]{{Response: perf}}, false)
	if starts.Rows() != 1 || fieldNamed(t, starts, "p95").Config.Unit != "ms" {
		t.Fatalf("starts rows %d", starts.Rows())
	}
	screens := screenPerformanceFrame("A", []serviceResponse[performanceResponse]{{Response: perf}}, false)
	slow := fieldNamed(t, screens, "slowFramePercent")
	if got := floatAt(t, slow, 0); got == nil || *got != 5 || floatAt(t, slow, 1) != nil {
		t.Fatal("slow share is 5 for Home and null for a screen without frames")
	}
	if got := floatAt(t, fieldNamed(t, screens, "frozenFramePercent"), 0); got == nil || *got != 1 {
		t.Fatal("frozen share")
	}
	two := screenPerformanceFrame("A", []serviceResponse[performanceResponse]{{Service: "a", Response: perf}, {Service: "b", Response: perf}}, false)
	if two.Fields[0].Name != "service" {
		t.Fatal("several services add a Service column")
	}
}

func TestErrorFrames(t *testing.T) {
	groups := errorGroupsFrame("A", []exceptionGroup{{ExceptionType: "IOException", OccurrenceCount: 7, FirstSeen: "2026-10-18T20:00:00Z", AffectedServices: []string{"a", "b"}}}, "")
	if fieldNamed(t, groups, "services").At(0) != "a, b" || fieldNamed(t, groups, "occurrences").At(0) != uint64(7) {
		t.Fatal("error group row")
	}
	issues := errorIssuesFrame("A", []errorIssue{{ExceptionType: "IOException", Status: "Regressed", StatusChangedAt: "garbage"}}, "")
	if fieldNamed(t, issues, "status").At(0) != "Regressed" {
		t.Fatal("status")
	}
	if !fieldNamed(t, issues, "statusChanged").At(0).(interface{ IsZero() bool }).IsZero() {
		t.Fatal("an unreadable timestamp is the zero time")
	}
}
