package plugin

import (
	"encoding/json"
	"strings"
	"time"
)

// The query kinds, each one Flare.Api endpoint (or two halves of one).
const (
	kindSessions           = "sessions"
	kindSessionTimeline    = "sessionTimeline"
	kindErrors             = "errors"
	kindIssues             = "issues"
	kindReleaseHealth      = "releaseHealth"
	kindReleaseHealthTrend = "releaseHealthTrend"
	kindReleases           = "releases"
	kindAppStarts          = "appStarts"
	kindScreenPerformance  = "screenPerformance"
)

const (
	trendCrashFreeSessions = "crashFreeSessions"
	trendSessions          = "sessions"
	trendCrashedSessions   = "crashedSessions"
)

// flareQuery is the query model the frontend editor saves (field names match src/types.ts).
type flareQuery struct {
	Kind          string `json:"kind"`
	Service       string `json:"service"`
	Version       string `json:"version"`
	SessionID     string `json:"sessionId"`
	ErrorsOnly    bool   `json:"errorsOnly"`
	TopN          int    `json:"topN"`
	TrendMetric   string `json:"trendMetric"`
	BucketMinutes int    `json:"bucketMinutes"`
}

func parseQuery(raw json.RawMessage) (flareQuery, error) {
	var q flareQuery
	err := json.Unmarshal(raw, &q)
	return q, err
}

// timeWindow is the dashboard's time range.
type timeWindow struct {
	From time.Time
	To   time.Time
}

// Flare.Api's session endpoints take a window ending at endUnixMs, clamped server-side to this range.
const (
	minWindowMinutes = 5
	maxWindowMinutes = 10080
)

type resolvedWindow struct {
	WindowMinutes int
	EndUnixMs     int64
	// Clamped is set when the dashboard range was wider than Flare answers, so the window ends where the range does but starts later.
	Clamped bool
}

func resolveWindow(w timeWindow) resolvedWindow {
	requested := int(w.To.Sub(w.From).Minutes())
	if w.To.Sub(w.From) > time.Duration(requested)*time.Minute {
		requested++ // round up to whole minutes
	}
	minutes := requested
	if minutes > maxWindowMinutes {
		minutes = maxWindowMinutes
	}
	if minutes < minWindowMinutes {
		minutes = minWindowMinutes
	}
	return resolvedWindow{WindowMinutes: minutes, EndUnixMs: w.To.UnixMilli(), Clamped: requested > maxWindowMinutes}
}

// splitServices reads a comma separated service field (also what a multi-value variable expands to) as a clean list.
func splitServices(raw string) []string {
	raw = strings.TrimPrefix(raw, "{")
	raw = strings.TrimSuffix(raw, "}")
	var out []string
	for _, s := range strings.Split(raw, ",") {
		if s = strings.TrimSpace(s); s != "" {
			out = append(out, s)
		}
	}
	return out
}

// serviceCalls are the services a single-service endpoint is called for: no selection is one call for every
// service (the empty string), several are one call each, because those endpoints take one service.
func serviceCalls(raw string) []string {
	services := splitServices(raw)
	if len(services) == 0 {
		return []string{""}
	}
	return services
}

type sessionsRequest struct {
	WindowMinutes int    `json:"windowMinutes"`
	EndUnixMs     int64  `json:"endUnixMs"`
	Service       string `json:"service,omitempty"`
	Version       string `json:"version,omitempty"`
	ErrorsOnly    bool   `json:"errorsOnly"`
}

func newSessionsRequest(q flareQuery, w resolvedWindow, service string) sessionsRequest {
	return sessionsRequest{
		WindowMinutes: w.WindowMinutes,
		EndUnixMs:     w.EndUnixMs,
		Service:       service,
		Version:       strings.TrimSpace(q.Version),
		ErrorsOnly:    q.ErrorsOnly,
	}
}

type windowRequest struct {
	WindowMinutes int    `json:"windowMinutes"`
	EndUnixMs     int64  `json:"endUnixMs"`
	Service       string `json:"service,omitempty"`
}

func newWindowRequest(w resolvedWindow, service string) windowRequest {
	return windowRequest{WindowMinutes: w.WindowMinutes, EndUnixMs: w.EndUnixMs, Service: service}
}

type trendRequest struct {
	windowRequest
	BucketMinutes int `json:"bucketMinutes,omitempty"`
}

// trendBucketMinutes is bucketMinutes from the query, else the dashboard interval rounded up to whole minutes
// (Flare falls back to a nice width if it is too fine); 0 leaves it to Flare.
func trendBucketMinutes(q flareQuery, interval time.Duration) int {
	if q.BucketMinutes > 0 {
		return q.BucketMinutes
	}
	if interval > 0 {
		minutes := int((interval + time.Minute - 1) / time.Minute)
		if minutes < 1 {
			minutes = 1
		}
		return minutes
	}
	return 0
}

func newTrendRequest(q flareQuery, w resolvedWindow, service string, interval time.Duration) trendRequest {
	return trendRequest{windowRequest: newWindowRequest(w, service), BucketMinutes: trendBucketMinutes(q, interval)}
}

type performanceRequest struct {
	windowRequest
	Version string `json:"version,omitempty"`
}

func newPerformanceRequest(q flareQuery, w resolvedWindow, service string) performanceRequest {
	return performanceRequest{windowRequest: newWindowRequest(w, service), Version: strings.TrimSpace(q.Version)}
}

type timelineRequest struct {
	SessionID  string `json:"sessionId"`
	FromUnixMs int64  `json:"fromUnixMs"`
	ToUnixMs   int64  `json:"toUnixMs"`
}

// The timeline scan window is the dashboard range, so a session older than the range needs the range moved to it.
func newTimelineRequest(q flareQuery, w timeWindow) timelineRequest {
	return timelineRequest{SessionID: strings.TrimSpace(q.SessionID), FromUnixMs: w.From.UnixMilli(), ToUnixMs: w.To.UnixMilli()}
}

type errorsFilter struct {
	From     string   `json:"from"`
	To       string   `json:"to"`
	Services []string `json:"services,omitempty"`
}

type errorsRequest struct {
	Filter errorsFilter `json:"filter"`
	TopN   int          `json:"topN,omitempty"`
}

func newErrorsRequest(q flareQuery, w timeWindow) errorsRequest {
	return errorsRequest{
		Filter: errorsFilter{
			From:     isoTime(w.From),
			To:       isoTime(w.To),
			Services: splitServices(q.Service),
		},
		TopN: q.TopN,
	}
}

// isoTime is an instant as the ISO 8601 UTC string Flare.Api's filters take, with millisecond precision.
func isoTime(t time.Time) string { return t.UTC().Format("2006-01-02T15:04:05.000Z") }
