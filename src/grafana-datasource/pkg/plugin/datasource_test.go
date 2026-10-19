package plugin

import (
	"context"
	"encoding/json"
	"io"
	"net/http"
	"net/http/httptest"
	"strings"
	"sync"
	"testing"
	"time"

	"github.com/grafana/grafana-plugin-sdk-go/backend"
)

// fakeFlare answers Flare.Api's endpoints with canned bodies and records what it was called with.
type fakeFlare struct {
	mu       sync.Mutex
	requests []recorded
	status   int
	bodies   map[string]string
}

type recorded struct {
	Method, Path, Query, Auth, Body string
}

func (f *fakeFlare) ServeHTTP(w http.ResponseWriter, r *http.Request) {
	body, _ := io.ReadAll(r.Body)
	f.mu.Lock()
	f.requests = append(f.requests, recorded{r.Method, r.URL.Path, r.URL.RawQuery, r.Header.Get("Authorization"), string(body)})
	f.mu.Unlock()
	if f.status != 0 {
		w.WriteHeader(f.status)
		_, _ = w.Write([]byte(`{"detail":"nope"}`))
		return
	}
	resp, ok := f.bodies[r.URL.Path]
	if !ok {
		http.NotFound(w, r)
		return
	}
	_, _ = w.Write([]byte(resp))
}

func (f *fakeFlare) last() recorded {
	f.mu.Lock()
	defer f.mu.Unlock()
	return f.requests[len(f.requests)-1]
}

func (f *fakeFlare) count(path string) int {
	f.mu.Lock()
	defer f.mu.Unlock()
	n := 0
	for _, r := range f.requests {
		if r.Path == path {
			n++
		}
	}
	return n
}

var cannedBodies = map[string]string{
	"/api/app-sessions/list": `{"windowMinutes":60,"truncated":false,"services":["app","web"],"versions":["1.0","2.0"],"sessions":[
		{"sessionId":"old","serviceName":"app","version":"1.0","os":"ios","device":"x","firstSeenUnixMs":1000,"lastSeenUnixMs":2000,"spanCount":1,"traceCount":1,"errorCount":0,"screens":["A"]},
		{"sessionId":"new","serviceName":"app","version":"1.0","os":"ios","device":"x","firstSeenUnixMs":3000,"lastSeenUnixMs":9000,"spanCount":1,"traceCount":1,"errorCount":1,"screens":[]}]}`,
	"/api/app-sessions/timeline":             `{"sessionId":"s1","serviceName":"app","version":"2.0","os":"ios","device":"x","truncated":false,"events":[{"traceId":"t","spanId":"p","name":"nav","serviceName":"app","startUnixMs":1000,"durationMs":4,"screen":"A","isError":false,"statusMessage":"","exceptionType":"","exceptionMessage":"","breadcrumbCategory":"","breadcrumbMessage":"","hasScreenshot":false}]}`,
	"/api/app-sessions/release-health":       `{"windowMinutes":60,"versions":[{"version":"2.0","sessions":10,"crashedSessions":1,"erroredSessions":2,"users":5,"crashedUsers":1}]}`,
	"/api/app-sessions/release-health/trend": `{"windowMinutes":60,"bucketMinutes":5,"points":[{"bucketUnixMs":1000,"version":"2.0","sessions":10,"crashedSessions":1}]}`,
	"/api/app-sessions/performance":          `{"windowMinutes":60,"starts":[{"version":"2.0","type":"cold","count":4,"p50Ms":1,"p95Ms":2}],"screens":[{"screen":"Home","loads":1,"loadP50Ms":1,"loadP95Ms":2,"visits":1,"frames":10,"slowFrames":1,"frozenFrames":0}]}`,
	"/api/errors/groups":                     `{"groups":[{"exceptionType":"IOException","exceptionMessage":"disk","occurrenceCount":3,"firstSeen":"2026-10-18T20:00:00Z","lastSeen":"2026-10-18T21:00:00Z","affectedServices":["app"],"samplePaths":[],"owner":""}]}`,
	"/api/errors/issues":                     `{"issues":[{"id":"1","exceptionType":"IOException","exceptionMessage":"disk","status":"Open","assignee":"","statusChangedAt":"2026-10-18T20:00:00Z","issueUrl":"","occurrencesSinceChange":2,"regressedVersion":"","updatedAt":"2026-10-18T20:00:00Z"}]}`,
	"/api/errors/facet-values":               `{"values":[{"value":"checkout","count":3}]}`,
	"/api/releases": `{"releases":[
		{"id":"1","service":"app","version":"2.0","deployedAt":"2026-10-18T20:00:00Z","notes":"in range"},
		{"id":"2","service":"app","version":"1.0","deployedAt":"2025-01-01T00:00:00Z","notes":"too old"},
		{"id":"3","service":"app","version":"2.0","deployedAt":"2026-10-18T21:00:00Z","notes":"same version"}]}`,
}

func newTestDatasource(t *testing.T, f *fakeFlare) *Datasource {
	t.Helper()
	srv := httptest.NewServer(f)
	t.Cleanup(srv.Close)
	return newDatasource(options{URL: srv.URL + "/", DashboardURL: "http://flare"}, "flr_pat_secret", nil)
}

func runQuery(t *testing.T, ds *Datasource, q map[string]any) backend.DataResponse {
	t.Helper()
	raw, _ := json.Marshal(q)
	from := time.Date(2026, 10, 18, 0, 0, 0, 0, time.UTC)
	resp, err := ds.QueryData(context.Background(), &backend.QueryDataRequest{Queries: []backend.DataQuery{{
		RefID: "A", JSON: raw, TimeRange: backend.TimeRange{From: from, To: from.Add(48 * time.Hour)}, Interval: time.Minute,
	}}})
	if err != nil {
		t.Fatal(err)
	}
	return resp.Responses["A"]
}

func TestQueryDataEveryKind(t *testing.T) {
	f := &fakeFlare{bodies: cannedBodies}
	ds := newTestDatasource(t, f)

	cases := []struct {
		kind, path string
		frames     int
		rows       int
	}{
		{kindSessions, "/api/app-sessions/list", 1, 2},
		{kindSessionTimeline, "/api/app-sessions/timeline", 1, 1},
		{kindErrors, "/api/errors/groups", 1, 1},
		{kindIssues, "/api/errors/issues", 1, 1},
		{kindReleaseHealth, "/api/app-sessions/release-health", 1, 1},
		{kindReleaseHealthTrend, "/api/app-sessions/release-health/trend", 1, 1},
		{kindReleases, "/api/releases", 1, 2}, // the 2025 deploy is outside the range
		{kindAppStarts, "/api/app-sessions/performance", 1, 1},
		{kindScreenPerformance, "/api/app-sessions/performance", 1, 1},
	}
	for _, c := range cases {
		t.Run(c.kind, func(t *testing.T) {
			resp := runQuery(t, ds, map[string]any{"kind": c.kind, "sessionId": "s1"})
			if resp.Error != nil {
				t.Fatal(resp.Error)
			}
			if len(resp.Frames) != c.frames || resp.Frames[0].Rows() != c.rows {
				t.Fatalf("frames %d, rows %d", len(resp.Frames), resp.Frames[0].Rows())
			}
			if got := f.last(); got.Path != c.path || got.Auth != "Bearer flr_pat_secret" {
				t.Fatalf("last call %+v", got)
			}
		})
	}
}

func TestSessionsSortedNewestFirst(t *testing.T) {
	ds := newTestDatasource(t, &fakeFlare{bodies: cannedBodies})
	resp := runQuery(t, ds, map[string]any{"kind": "sessions"})
	if got := fieldNamed(t, resp.Frames[0], "sessionId").At(0); got != "new" {
		t.Fatalf("first session %v", got)
	}
}

func TestSeveralServicesAreOneCallEach(t *testing.T) {
	f := &fakeFlare{bodies: cannedBodies}
	ds := newTestDatasource(t, f)

	resp := runQuery(t, ds, map[string]any{"kind": "releaseHealthTrend", "service": "app,web"})
	if resp.Error != nil {
		t.Fatal(resp.Error)
	}
	if f.count("/api/app-sessions/release-health/trend") != 2 || len(resp.Frames) != 2 {
		t.Fatalf("calls %d, frames %d", f.count("/api/app-sessions/release-health/trend"), len(resp.Frames))
	}
	if resp.Frames[0].Fields[1].Labels["service"] != "app" || resp.Frames[1].Fields[1].Labels["service"] != "web" {
		t.Fatal("each series carries its service label, in order")
	}

	resp = runQuery(t, ds, map[string]any{"kind": "releaseHealth", "service": "app"})
	if resp.Frames[0].Fields[0].Name != "service" {
		t.Fatal("a named service shows the Service column on release health")
	}
	resp = runQuery(t, ds, map[string]any{"kind": "appStarts", "service": "app"})
	if resp.Frames[0].Fields[0].Name != "version" {
		t.Fatal("a single service does not add a Service column to the performance tables")
	}
}

func TestRequestBodiesAreSent(t *testing.T) {
	f := &fakeFlare{bodies: cannedBodies}
	ds := newTestDatasource(t, f)
	runQuery(t, ds, map[string]any{"kind": "sessions", "service": "app", "version": "2.0", "errorsOnly": true})
	var body map[string]any
	if err := json.Unmarshal([]byte(f.last().Body), &body); err != nil {
		t.Fatal(err)
	}
	if body["service"] != "app" || body["version"] != "2.0" || body["errorsOnly"] != true || body["windowMinutes"] != float64(2880) {
		t.Fatalf("body %v", body)
	}
}

func TestEmptyTimelineSessionAndUnknownKindAreEmpty(t *testing.T) {
	f := &fakeFlare{bodies: cannedBodies}
	ds := newTestDatasource(t, f)
	for _, q := range []map[string]any{{"kind": "sessionTimeline", "sessionId": "  "}, {"kind": "nope"}, {}} {
		resp := runQuery(t, ds, q)
		if resp.Error != nil || len(resp.Frames) != 0 {
			t.Fatalf("%v: %+v", q, resp)
		}
	}
	if len(f.requests) != 0 {
		t.Fatal("no call for an empty query")
	}
}

func TestErrorsBecomeReadableMessages(t *testing.T) {
	cases := map[int]string{
		401: "Flare rejected the token (401). Check that it is a valid personal access token (flr_pat_...).",
		403: "The token is not allowed to read this (403).",
		404: "Flare returned 404: is the URL the Flare API base, for example http://flare-api:8080?",
		502: "Grafana could not reach Flare. Check the URL, and that the Grafana server can resolve and connect to it.",
		500: "Flare returned 500: nope.",
	}
	for status, want := range cases {
		ds := newTestDatasource(t, &fakeFlare{status: status})
		resp := runQuery(t, ds, map[string]any{"kind": "sessions"})
		if resp.Error == nil || resp.Error.Error() != want {
			t.Fatalf("%d: got %v, want %q", status, resp.Error, want)
		}
	}
}

func TestUnreachableFlare(t *testing.T) {
	ds := newDatasource(options{URL: "http://127.0.0.1:1"}, "", nil)
	resp := runQuery(t, ds, map[string]any{"kind": "sessions"})
	if resp.Error == nil || resp.Error.Error() != describeError(&apiError{}) {
		t.Fatalf("got %v", resp.Error)
	}
}

func TestInvalidQueryModel(t *testing.T) {
	ds := newTestDatasource(t, &fakeFlare{bodies: cannedBodies})
	resp, _ := ds.QueryData(context.Background(), &backend.QueryDataRequest{Queries: []backend.DataQuery{{RefID: "A", JSON: []byte(`[1]`)}}})
	if resp.Responses["A"].Error == nil {
		t.Fatal("a query that is not an object must fail")
	}
}

func TestOneBadQueryDoesNotFailTheOthers(t *testing.T) {
	ds := newTestDatasource(t, &fakeFlare{bodies: map[string]string{"/api/errors/issues": cannedBodies["/api/errors/issues"]}})
	from := time.Now().Add(-time.Hour)
	resp, _ := ds.QueryData(context.Background(), &backend.QueryDataRequest{Queries: []backend.DataQuery{
		{RefID: "A", JSON: []byte(`{"kind":"sessions"}`), TimeRange: backend.TimeRange{From: from, To: time.Now()}},
		{RefID: "B", JSON: []byte(`{"kind":"issues"}`), TimeRange: backend.TimeRange{From: from, To: time.Now()}},
	}})
	if resp.Responses["A"].Error == nil || resp.Responses["B"].Error != nil || len(resp.Responses["B"].Frames) != 1 {
		t.Fatalf("A %v, B %+v", resp.Responses["A"].Error, resp.Responses["B"])
	}
}

func TestCheckHealth(t *testing.T) {
	ds := newTestDatasource(t, &fakeFlare{bodies: cannedBodies})
	if res, _ := ds.CheckHealth(context.Background(), nil); res.Status != backend.HealthStatusOk {
		t.Fatalf("%+v", res)
	}
	bad := newTestDatasource(t, &fakeFlare{status: 401})
	if res, _ := bad.CheckHealth(context.Background(), nil); res.Status != backend.HealthStatusError || res.Message != describeError(&apiError{Status: 401}) {
		t.Fatalf("%+v", res)
	}
	none := newDatasource(options{}, "", nil)
	if res, _ := none.CheckHealth(context.Background(), nil); res.Status != backend.HealthStatusError || res.Message != "Set the Flare API URL." {
		t.Fatalf("%+v", res)
	}
}

func TestNoTokenSendsNoAuthorization(t *testing.T) {
	f := &fakeFlare{bodies: cannedBodies}
	srv := httptest.NewServer(f)
	defer srv.Close()
	ds := newDatasource(options{URL: srv.URL}, "", nil)
	runQuery(t, ds, map[string]any{"kind": "issues"})
	if f.last().Auth != "" {
		t.Fatal("an empty token must not send an Authorization header")
	}
}

type resourceSender struct{ resp *backend.CallResourceResponse }

func (s *resourceSender) Send(r *backend.CallResourceResponse) error { s.resp = r; return nil }

func callVariables(t *testing.T, ds *Datasource, body map[string]any) (int, variablesResponse) {
	t.Helper()
	raw, _ := json.Marshal(body)
	s := &resourceSender{}
	err := ds.CallResource(context.Background(), &backend.CallResourceRequest{Method: "POST", Path: "variables", URL: "variables", Body: raw}, s)
	if err != nil {
		t.Fatal(err)
	}
	var out variablesResponse
	_ = json.Unmarshal(s.resp.Body, &out)
	return s.resp.Status, out
}

func TestVariables(t *testing.T) {
	f := &fakeFlare{bodies: cannedBodies}
	ds := newTestDatasource(t, f)
	window := map[string]any{"from": 1792000000000, "to": 1792003600000}
	merge := func(kind string, extra map[string]any) map[string]any {
		m := map[string]any{"kind": kind}
		for k, v := range window {
			m[k] = v
		}
		for k, v := range extra {
			m[k] = v
		}
		return m
	}

	cases := []struct {
		name string
		body map[string]any
		want []string
	}{
		{"session services", merge("sessionServices", nil), []string{"app", "web"}},
		{"session versions", merge("sessionVersions", map[string]any{"service": "app"}), []string{"1.0", "2.0"}},
		{"error services", merge("errorServices", nil), []string{"checkout"}},
		{"release versions are distinct", merge("releaseVersions", nil), []string{"2.0", "1.0"}},
		{"unknown kind", merge("bogus", nil), []string{}},
	}
	for _, c := range cases {
		t.Run(c.name, func(t *testing.T) {
			status, out := callVariables(t, ds, c.body)
			if status != 200 || len(out.Values) != len(c.want) {
				t.Fatalf("status %d values %v", status, out.Values)
			}
			for i := range c.want {
				if out.Values[i] != c.want[i] {
					t.Fatalf("values %v want %v", out.Values, c.want)
				}
			}
		})
	}

	// sessionVersions narrows to the first named service; sessionServices must not.
	callVariables(t, ds, merge("sessionVersions", map[string]any{"service": "{app,web}"}))
	if body := f.last().Body; !strings.Contains(body, `"service":"app"`) {
		t.Fatalf("versions body %s", body)
	}
	callVariables(t, ds, merge("sessionServices", map[string]any{"service": "app"}))
	if body := f.last().Body; strings.Contains(body, "service") {
		t.Fatalf("services body %s", body)
	}
}

func TestVariablesError(t *testing.T) {
	ds := newTestDatasource(t, &fakeFlare{status: 401})
	status, _ := callVariables(t, ds, map[string]any{"kind": "sessionServices", "from": 1, "to": 2})
	if status != http.StatusBadGateway {
		t.Fatalf("status %d", status)
	}
}
