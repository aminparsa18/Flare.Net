package plugin

import (
	"sort"
	"strings"
	"time"

	"github.com/grafana/grafana-plugin-sdk-go/data"
)

const noMatch = "(none)"

const clampedNotice = "Flare answers at most 7 days; the range was cut to its last 7 days."

func uptr[T any](v T) *T { return &v }

func millis(ms int64) time.Time { return time.UnixMilli(ms).UTC() }

// parseTime reads an ISO 8601 timestamp; an unreadable one is the zero time.
func parseTime(s string) time.Time {
	t, err := time.Parse(time.RFC3339Nano, s)
	if err != nil {
		return time.Time{}
	}
	return t.UTC()
}

func withNotices(frame *data.Frame, texts ...string) *data.Frame {
	var notices []data.Notice
	for _, t := range texts {
		if t != "" {
			notices = append(notices, data.Notice{Severity: data.NoticeSeverityWarning, Text: t})
		}
	}
	if len(notices) > 0 {
		if frame.Meta == nil {
			frame.Meta = &data.FrameMeta{}
		}
		frame.Meta.Notices = notices
	}
	return frame
}

func notice(condition bool, text string) string {
	if condition {
		return text
	}
	return ""
}

// column is a field with its display name; the unit and decimals are optional.
func column(name, display string, values any) *data.Field {
	f := data.NewField(name, nil, values)
	f.Config = &data.FieldConfig{DisplayName: display}
	return f
}

func withUnit(f *data.Field, unit string, decimals uint16) *data.Field {
	f.Config.Unit = unit
	f.Config.Decimals = uptr(decimals)
	return f
}

func withLinks(f *data.Field, links []data.DataLink) *data.Field {
	if len(links) > 0 {
		f.Config.Links = links
	}
	return f
}

// dashboardLink links a cell back to the Flare dashboard; no link when no dashboard URL is configured.
func dashboardLink(dashboardURL, path, title string) []data.DataLink {
	base := strings.TrimRight(strings.TrimSpace(dashboardURL), "/")
	if base == "" {
		return nil
	}
	return []data.DataLink{{Title: title, URL: base + path, TargetBlank: true}}
}

func openLink(title string) []data.DataLink {
	return []data.DataLink{{Title: title, URL: "${__value.raw}", TargetBlank: true}}
}

// share is the percent of total that is part; nil when there was nothing to measure.
func share(total, part uint64) *float64 {
	if total == 0 {
		return nil
	}
	return uptr(float64(part) / float64(total) * 100)
}

// crashFree is the percent of total that did not crash; nil when there was nothing to measure.
func crashFree(total, crashed uint64) *float64 {
	if total == 0 {
		return nil
	}
	return uptr((1 - float64(crashed)/float64(total)) * 100)
}

func orNone(s string) string {
	if s == "" {
		return noMatch
	}
	return s
}

func sessionsFrame(refID string, rows []appSession, truncated, clamped bool, dashboardURL string) *data.Frame {
	frame := data.NewFrame("Sessions",
		column("firstSeen", "Started", mapSlice(rows, func(r appSession) time.Time { return millis(r.FirstSeenUnixMs) })),
		column("lastSeen", "Last seen", mapSlice(rows, func(r appSession) time.Time { return millis(r.LastSeenUnixMs) })),
		withLinks(column("sessionId", "Session", mapSlice(rows, func(r appSession) string { return r.SessionID })),
			dashboardLink(dashboardURL, "/sessions/${__value.raw}", "Open in Flare")),
		column("service", "Service", mapSlice(rows, func(r appSession) string { return r.ServiceName })),
		column("version", "Version", mapSlice(rows, func(r appSession) string { return r.Version })),
		column("os", "OS", mapSlice(rows, func(r appSession) string { return r.Os })),
		column("device", "Device", mapSlice(rows, func(r appSession) string { return r.Device })),
		column("spans", "Spans", mapSlice(rows, func(r appSession) uint64 { return r.SpanCount })),
		column("traces", "Traces", mapSlice(rows, func(r appSession) uint64 { return r.TraceCount })),
		column("errors", "Errors", mapSlice(rows, func(r appSession) uint64 { return r.ErrorCount })),
		column("screens", "Screens", mapSlice(rows, func(r appSession) string { return strings.Join(r.Screens, ", ") })),
	)
	frame.RefID = refID
	return withNotices(frame,
		notice(truncated, "Flare returned only the most recent sessions; narrow the service, version or time range."),
		notice(clamped, clampedNotice))
}

// sessionTimelineFrame is one session's spans in start order: a time-keyed table, so it also reads as a timeline in
// Grafana's state timeline and logs panels.
func sessionTimelineFrame(refID string, r timelineResponse, dashboardURL string) *data.Frame {
	rows := r.Events
	frame := data.NewFrame("Session "+r.SessionID,
		column("time", "Start", mapSlice(rows, func(e timelineEvent) time.Time { return millis(e.StartUnixMs) })),
		column("name", "Span", mapSlice(rows, func(e timelineEvent) string { return e.Name })),
		withUnit(column("duration", "Duration", mapSlice(rows, func(e timelineEvent) float64 { return e.DurationMs })), "ms", 1),
		column("screen", "Screen", mapSlice(rows, func(e timelineEvent) string { return e.Screen })),
		column("error", "Error", mapSlice(rows, func(e timelineEvent) bool { return e.IsError })),
		column("exceptionType", "Exception", mapSlice(rows, func(e timelineEvent) string { return e.ExceptionType })),
		column("exceptionMessage", "Exception message", mapSlice(rows, func(e timelineEvent) string { return e.ExceptionMessage })),
		column("statusMessage", "Status", mapSlice(rows, func(e timelineEvent) string { return e.StatusMessage })),
		column("breadcrumbCategory", "Breadcrumb category", mapSlice(rows, func(e timelineEvent) string { return e.BreadcrumbCategory })),
		column("breadcrumbMessage", "Breadcrumb", mapSlice(rows, func(e timelineEvent) string { return e.BreadcrumbMessage })),
		withLinks(column("traceId", "Trace", mapSlice(rows, func(e timelineEvent) string { return e.TraceID })),
			dashboardLink(dashboardURL, "/traces/${__value.raw}", "Open trace in Flare")),
		column("spanId", "Span ID", mapSlice(rows, func(e timelineEvent) string { return e.SpanID })),
		column("service", "Service", mapSlice(rows, func(e timelineEvent) string { return e.ServiceName })),
		column("hasScreenshot", "Screenshot", mapSlice(rows, func(e timelineEvent) bool { return e.HasScreenshot })),
	)
	frame.RefID = refID
	frame.Meta = &data.FrameMeta{Custom: map[string]any{
		"service": r.ServiceName, "version": r.Version, "os": r.Os, "device": r.Device,
	}}
	return withNotices(frame, notice(r.Truncated, "Flare returned only the first spans of this session; narrow the time range."))
}

func errorGroupsFrame(refID string, rows []exceptionGroup, dashboardURL string) *data.Frame {
	frame := data.NewFrame("Errors",
		withLinks(column("exceptionType", "Type", mapSlice(rows, func(r exceptionGroup) string { return r.ExceptionType })),
			dashboardLink(dashboardURL, "/errors", "Open errors in Flare")),
		column("message", "Message", mapSlice(rows, func(r exceptionGroup) string { return r.ExceptionMessage })),
		column("occurrences", "Occurrences", mapSlice(rows, func(r exceptionGroup) uint64 { return r.OccurrenceCount })),
		column("firstSeen", "First seen", mapSlice(rows, func(r exceptionGroup) time.Time { return parseTime(r.FirstSeen) })),
		column("lastSeen", "Last seen", mapSlice(rows, func(r exceptionGroup) time.Time { return parseTime(r.LastSeen) })),
		column("services", "Services", mapSlice(rows, func(r exceptionGroup) string { return strings.Join(r.AffectedServices, ", ") })),
		column("owner", "Owner", mapSlice(rows, func(r exceptionGroup) string { return r.Owner })),
	)
	frame.RefID = refID
	return frame
}

func errorIssuesFrame(refID string, rows []errorIssue, dashboardURL string) *data.Frame {
	frame := data.NewFrame("Error issues",
		withLinks(column("exceptionType", "Type", mapSlice(rows, func(r errorIssue) string { return r.ExceptionType })),
			dashboardLink(dashboardURL, "/errors", "Open errors in Flare")),
		column("message", "Message", mapSlice(rows, func(r errorIssue) string { return r.ExceptionMessage })),
		column("status", "Status", mapSlice(rows, func(r errorIssue) string { return r.Status })),
		column("assignee", "Assignee", mapSlice(rows, func(r errorIssue) string { return r.Assignee })),
		column("regressedVersion", "Regressed in", mapSlice(rows, func(r errorIssue) string { return r.RegressedVersion })),
		column("occurrences", "Since status change", mapSlice(rows, func(r errorIssue) uint64 { return r.OccurrencesSinceChange })),
		withLinks(column("issueUrl", "Issue", mapSlice(rows, func(r errorIssue) string { return r.IssueURL })), openLink("Open issue")),
		column("statusChanged", "Status changed", mapSlice(rows, func(r errorIssue) time.Time { return parseTime(r.StatusChangedAt) })),
		column("updated", "Updated", mapSlice(rows, func(r errorIssue) time.Time { return parseTime(r.UpdatedAt) })),
	)
	frame.RefID = refID
	return frame
}

type serviceResponse[T any] struct {
	Service  string
	Response T
}

// releaseHealthFrame is one row per app version in the window, with the crash-free rates Flare's Releases view shows.
func releaseHealthFrame(refID string, responses []serviceResponse[releaseHealthResponse], clamped bool) *data.Frame {
	type row struct {
		service string
		v       releaseHealthVersion
	}
	var rows []row
	withService := false
	for _, r := range responses {
		withService = withService || r.Service != ""
		for _, v := range r.Response.Versions {
			rows = append(rows, row{r.Service, v})
		}
	}

	var fields []*data.Field
	if withService {
		fields = append(fields, column("service", "Service", mapSlice(rows, func(r row) string { return r.service })))
	}
	fields = append(fields,
		column("version", "Version", mapSlice(rows, func(r row) string { return orNone(r.v.Version) })),
		column("sessions", "Sessions", mapSlice(rows, func(r row) uint64 { return r.v.Sessions })),
		column("crashedSessions", "Crashed sessions", mapSlice(rows, func(r row) uint64 { return r.v.CrashedSessions })),
		column("erroredSessions", "Errored sessions", mapSlice(rows, func(r row) uint64 { return r.v.ErroredSessions })),
		withUnit(column("crashFreeSessions", "Crash-free sessions", mapSlice(rows, func(r row) *float64 { return crashFree(r.v.Sessions, r.v.CrashedSessions) })), "percent", 2),
		column("users", "Users", mapSlice(rows, func(r row) uint64 { return r.v.Users })),
		column("crashedUsers", "Crashed users", mapSlice(rows, func(r row) uint64 { return r.v.CrashedUsers })),
		withUnit(column("crashFreeUsers", "Crash-free users", mapSlice(rows, func(r row) *float64 { return crashFree(r.v.Users, r.v.CrashedUsers) })), "percent", 2),
	)
	frame := data.NewFrame("Release health", fields...)
	frame.RefID = refID
	return withNotices(frame, notice(clamped, clampedNotice))
}

var trendLabel = map[string]string{
	trendCrashFreeSessions: "Crash-free sessions",
	trendSessions:          "Sessions",
	trendCrashedSessions:   "Crashed sessions",
}

// releaseHealthTrendFrames is one time series frame per version (and per service when several were asked for), ready
// to draw, and numeric, so a Grafana alert rule can evaluate it.
func releaseHealthTrendFrames(refID string, responses []serviceResponse[releaseHealthTrendResponse], metric string, clamped bool) data.Frames {
	if _, ok := trendLabel[metric]; !ok {
		metric = trendCrashFreeSessions
	}
	frames := data.Frames{}
	for _, r := range responses {
		byVersion := map[string][]releaseHealthTrendPoint{}
		for _, p := range r.Response.Points {
			byVersion[p.Version] = append(byVersion[p.Version], p)
		}
		versions := make([]string, 0, len(byVersion))
		for v := range byVersion {
			versions = append(versions, v)
		}
		sort.Strings(versions)

		for _, version := range versions {
			points := byVersion[version]
			sort.Slice(points, func(i, j int) bool { return points[i].BucketUnixMs < points[j].BucketUnixMs })

			labels := data.Labels{"version": orNone(version)}
			if r.Service != "" {
				labels["service"] = r.Service
			}
			// The legend reads "2.1.0" (or "app 2.1.0"), not "Crash-free sessions {version=...}": the panel title says what is drawn.
			display := orNone(version)
			if r.Service != "" {
				display = r.Service + " " + display
			}

			times := mapSlice(points, func(p releaseHealthTrendPoint) time.Time { return millis(p.BucketUnixMs) })
			value := data.NewField(trendLabel[metric], labels, trendValues(points, metric))
			value.Config = &data.FieldConfig{DisplayNameFromDS: display}
			if metric == trendCrashFreeSessions {
				value.Config.Unit = "percent"
				value.Config.Decimals = uptr(uint16(2))
				value.Config.Min = uptr(data.ConfFloat64(0))
				value.Config.Max = uptr(data.ConfFloat64(100))
			} else {
				value.Config.Decimals = uptr(uint16(0))
			}

			frame := data.NewFrame("", data.NewField("Time", nil, times), value)
			frame.RefID = refID
			frame.Meta = &data.FrameMeta{Type: data.FrameTypeTimeSeriesMulti}
			frames = append(frames, withNotices(frame, notice(clamped, clampedNotice)))
		}
	}
	return frames
}

func trendValues(points []releaseHealthTrendPoint, metric string) []*float64 {
	return mapSlice(points, func(p releaseHealthTrendPoint) *float64 {
		switch metric {
		case trendSessions:
			return uptr(float64(p.Sessions))
		case trendCrashedSessions:
			return uptr(float64(p.CrashedSessions))
		default:
			return crashFree(p.Sessions, p.CrashedSessions)
		}
	})
}

var valueLabel = map[string]string{
	valueCrashFreeSessions: "Crash-free sessions",
	valueCrashFreeUsers:    "Crash-free users",
	valueSessions:          "Sessions",
	valueCrashedSessions:   "Crashed sessions",
	valueErroredSessions:   "Errored sessions",
	valueUsers:             "Users",
	valueCrashedUsers:      "Crashed users",
}

// versionValue is the number a release health value query reports for one version; nil when there was nothing to
// measure (a crash-free rate over zero sessions or users).
func versionValue(v releaseHealthVersion, metric string) *float64 {
	count := func(n uint64) *float64 { return uptr(float64(n)) }
	switch metric {
	case valueCrashFreeUsers:
		return crashFree(v.Users, v.CrashedUsers)
	case valueSessions:
		return count(v.Sessions)
	case valueCrashedSessions:
		return count(v.CrashedSessions)
	case valueErroredSessions:
		return count(v.ErroredSessions)
	case valueUsers:
		return count(v.Users)
	case valueCrashedUsers:
		return count(v.CrashedUsers)
	default:
		return crashFree(v.Sessions, v.CrashedSessions)
	}
}

// releaseHealthValueFrames is one number per version (and per service when several were asked for) over the whole
// window: a numeric-multi frame without a time field, which Grafana alert rules and server-side expressions take as a
// number directly, so a rule needs no Reduce step. Versions with fewer than minSessions sessions are left out, so a
// release a handful of sessions old cannot fire a rule on a rate that means nothing yet.
func releaseHealthValueFrames(refID string, responses []serviceResponse[releaseHealthResponse], metric string, minSessions int, clamped bool) data.Frames {
	if _, ok := valueLabel[metric]; !ok {
		metric = valueCrashFreeSessions
	}
	frames := data.Frames{}
	for _, r := range responses {
		versions := append([]releaseHealthVersion(nil), r.Response.Versions...)
		sort.Slice(versions, func(i, j int) bool { return versions[i].Version < versions[j].Version })

		for _, v := range versions {
			if v.Sessions < uint64(max(minSessions, 0)) {
				continue
			}
			labels := data.Labels{"version": orNone(v.Version)}
			display := orNone(v.Version)
			if r.Service != "" {
				labels["service"] = r.Service
				display = r.Service + " " + display
			}

			value := data.NewField(valueLabel[metric], labels, []*float64{versionValue(v, metric)})
			value.Config = &data.FieldConfig{DisplayNameFromDS: display}
			if metric == valueCrashFreeSessions || metric == valueCrashFreeUsers {
				value.Config.Unit = "percent"
				value.Config.Decimals = uptr(uint16(2))
				value.Config.Min = uptr(data.ConfFloat64(0))
				value.Config.Max = uptr(data.ConfFloat64(100))
			} else {
				value.Config.Decimals = uptr(uint16(0))
			}

			frame := data.NewFrame("", value)
			frame.RefID = refID
			// Without a dataplane version (above 0.0) Grafana reads a frame with no time field as a legacy table, not a number.
			frame.Meta = &data.FrameMeta{Type: data.FrameTypeNumericMulti, TypeVersion: data.FrameTypeVersion{0, 1}}
			frames = append(frames, withNotices(frame, notice(clamped, clampedNotice)))
		}
	}
	return frames
}

// releasesFrame is deploys as a time-keyed table. Used as an annotation query it needs only its time, title and text
// fields mapped in Grafana's annotation settings.
func releasesFrame(refID string, rows []release) *data.Frame {
	frame := data.NewFrame("Releases",
		column("time", "Deployed", mapSlice(rows, func(r release) time.Time { return parseTime(r.DeployedAt) })),
		column("service", "Service", mapSlice(rows, func(r release) string { return r.Service })),
		column("version", "Version", mapSlice(rows, func(r release) string { return r.Version })),
		column("title", "Title", mapSlice(rows, func(r release) string { return r.Service + " " + r.Version })),
		column("text", "Notes", mapSlice(rows, func(r release) string { return r.Notes })),
		column("commit", "Commit", mapSlice(rows, func(r release) string { return r.Commit })),
		withLinks(column("url", "URL", mapSlice(rows, func(r release) string { return r.URL })), openLink("Open")),
		column("newErrors", "New errors", mapSlice(rows, func(r release) *int64 { return r.NewErrorCount })),
		column("regressions", "Regressions", mapSlice(rows, func(r release) *int64 { return r.RegressionCount })),
	)
	frame.RefID = refID
	return frame
}

// appStartsFrame is cold and warm app start durations (the app.start spans) per version.
func appStartsFrame(refID string, responses []serviceResponse[performanceResponse], clamped bool) *data.Frame {
	type row struct {
		service string
		v       appStartStat
	}
	var rows []row
	withService := false
	for _, r := range responses {
		withService = withService || r.Service != ""
		for _, v := range r.Response.Starts {
			rows = append(rows, row{r.Service, v})
		}
	}

	var fields []*data.Field
	if withService {
		fields = append(fields, column("service", "Service", mapSlice(rows, func(r row) string { return r.service })))
	}
	fields = append(fields,
		column("version", "Version", mapSlice(rows, func(r row) string { return orNone(r.v.Version) })),
		column("type", "Start type", mapSlice(rows, func(r row) string { return r.v.Type })),
		column("count", "Starts", mapSlice(rows, func(r row) uint64 { return r.v.Count })),
		withUnit(column("p50", "p50", mapSlice(rows, func(r row) float64 { return r.v.P50Ms })), "ms", 1),
		withUnit(column("p95", "p95", mapSlice(rows, func(r row) float64 { return r.v.P95Ms })), "ms", 1),
	)
	frame := data.NewFrame("App starts", fields...)
	frame.RefID = refID
	return withNotices(frame, notice(clamped, clampedNotice))
}

// screenPerformanceFrame is per-screen load time (screen.load spans) and frame health (screen.frames spans).
func screenPerformanceFrame(refID string, responses []serviceResponse[performanceResponse], clamped bool) *data.Frame {
	type row struct {
		service string
		v       screenPerformance
	}
	var rows []row
	withService := false
	for _, r := range responses {
		withService = withService || r.Service != ""
		for _, v := range r.Response.Screens {
			rows = append(rows, row{r.Service, v})
		}
	}

	var fields []*data.Field
	if withService {
		fields = append(fields, column("service", "Service", mapSlice(rows, func(r row) string { return r.service })))
	}
	fields = append(fields,
		column("screen", "Screen", mapSlice(rows, func(r row) string { return r.v.Screen })),
		column("loads", "Loads", mapSlice(rows, func(r row) uint64 { return r.v.Loads })),
		withUnit(column("loadP50", "Load p50", mapSlice(rows, func(r row) float64 { return r.v.LoadP50Ms })), "ms", 1),
		withUnit(column("loadP95", "Load p95", mapSlice(rows, func(r row) float64 { return r.v.LoadP95Ms })), "ms", 1),
		column("visits", "Visits with frames", mapSlice(rows, func(r row) uint64 { return r.v.Visits })),
		column("frames", "Frames", mapSlice(rows, func(r row) uint64 { return r.v.Frames })),
		column("slowFrames", "Slow frames", mapSlice(rows, func(r row) uint64 { return r.v.SlowFrames })),
		column("frozenFrames", "Frozen frames", mapSlice(rows, func(r row) uint64 { return r.v.FrozenFrames })),
		withUnit(column("slowFramePercent", "Slow frames %", mapSlice(rows, func(r row) *float64 { return share(r.v.Frames, r.v.SlowFrames) })), "percent", 2),
		withUnit(column("frozenFramePercent", "Frozen frames %", mapSlice(rows, func(r row) *float64 { return share(r.v.Frames, r.v.FrozenFrames) })), "percent", 2),
	)
	frame := data.NewFrame("Screen performance", fields...)
	frame.RefID = refID
	return withNotices(frame, notice(clamped, clampedNotice))
}

func mapSlice[T, U any](in []T, f func(T) U) []U {
	out := make([]U, len(in))
	for i, v := range in {
		out[i] = f(v)
	}
	return out
}
