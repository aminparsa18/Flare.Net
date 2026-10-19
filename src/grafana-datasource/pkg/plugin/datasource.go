// Package plugin is the backend of the Flare data source: it turns Grafana queries into Flare.Api calls and the
// answers into data frames, so the queries also work in alert rules, public dashboards and reports.
package plugin

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"net/http"
	"sync"
	"time"

	"github.com/grafana/grafana-plugin-sdk-go/backend"
	"github.com/grafana/grafana-plugin-sdk-go/backend/instancemgmt"
	"github.com/grafana/grafana-plugin-sdk-go/backend/resource/httpadapter"
	"github.com/grafana/grafana-plugin-sdk-go/data"
)

var (
	_ backend.QueryDataHandler      = (*Datasource)(nil)
	_ backend.CheckHealthHandler    = (*Datasource)(nil)
	_ backend.CallResourceHandler   = (*Datasource)(nil)
	_ instancemgmt.InstanceDisposer = (*Datasource)(nil)
)

// options is the data source's non-secret settings (jsonData); the token is in the decrypted secure settings.
type options struct {
	URL          string `json:"url"`
	DashboardURL string `json:"dashboardUrl"`
}

// Datasource is one configured Flare data source.
type Datasource struct {
	client       *flareClient
	dashboardURL string
	resources    backend.CallResourceHandler
}

// NewDatasource is the instance factory Grafana's plugin SDK calls for each configured data source.
func NewDatasource(_ context.Context, settings backend.DataSourceInstanceSettings) (instancemgmt.Instance, error) {
	var opts options
	if len(settings.JSONData) > 0 {
		if err := json.Unmarshal(settings.JSONData, &opts); err != nil {
			return nil, fmt.Errorf("the data source settings are not valid JSON: %w", err)
		}
	}
	return newDatasource(opts, settings.DecryptedSecureJSONData["token"], nil), nil
}

func newDatasource(opts options, token string, httpClient *http.Client) *Datasource {
	ds := &Datasource{client: newFlareClient(opts.URL, token, httpClient), dashboardURL: opts.DashboardURL}
	mux := http.NewServeMux()
	mux.HandleFunc("POST /variables", ds.handleVariables)
	ds.resources = httpadapter.New(mux)
	return ds
}

// Dispose releases what the instance holds; it holds nothing that needs closing.
func (d *Datasource) Dispose() {}

func (d *Datasource) CallResource(ctx context.Context, req *backend.CallResourceRequest, sender backend.CallResourceResponseSender) error {
	return d.resources.CallResource(ctx, req, sender)
}

// QueryData answers every query of a request; a failing query reports its error without failing the others.
func (d *Datasource) QueryData(ctx context.Context, req *backend.QueryDataRequest) (*backend.QueryDataResponse, error) {
	response := backend.NewQueryDataResponse()
	for _, q := range req.Queries {
		response.Responses[q.RefID] = d.query(ctx, q)
	}
	return response, nil
}

func (d *Datasource) query(ctx context.Context, dq backend.DataQuery) backend.DataResponse {
	q, err := parseQuery(dq.JSON)
	if err != nil {
		return backend.ErrorResponseWithErrorSource(backend.PluginError(fmt.Errorf("the query is not valid: %w", err)))
	}
	frames, err := d.run(ctx, dq.RefID, q, timeWindow{From: dq.TimeRange.From, To: dq.TimeRange.To}, dq.Interval)
	if err != nil {
		return backend.ErrorResponseWithErrorSource(backend.DownstreamError(errors.New(describeError(err))))
	}
	return backend.DataResponse{Frames: frames}
}

// fanOut runs fn once per service, concurrently, keeping the services' order.
func fanOut[T any](services []string, fn func(service string) (T, error)) ([]serviceResponse[T], error) {
	out := make([]serviceResponse[T], len(services))
	errs := make([]error, len(services))
	var wg sync.WaitGroup
	for i, service := range services {
		wg.Add(1)
		go func() {
			defer wg.Done()
			out[i].Service = service
			out[i].Response, errs[i] = fn(service)
		}()
	}
	wg.Wait()
	return out, errors.Join(errs...)
}

// labelled drops the service name when only one service (or all of them) was asked for, so the frames only carry a
// Service column or label when several were merged.
func labelled[T any](responses []serviceResponse[T]) []serviceResponse[T] {
	if len(responses) > 1 {
		return responses
	}
	out := make([]serviceResponse[T], len(responses))
	for i, r := range responses {
		out[i] = serviceResponse[T]{Response: r.Response}
	}
	return out
}

func (d *Datasource) run(ctx context.Context, refID string, q flareQuery, window timeWindow, interval time.Duration) (data.Frames, error) {
	resolved := resolveWindow(window)
	services := serviceCalls(q.Service)

	switch q.Kind {
	case kindSessions:
		responses, err := fanOut(services, func(service string) (appSessionsResponse, error) {
			var r appSessionsResponse
			return r, d.client.post(ctx, "/api/app-sessions/list", newSessionsRequest(q, resolved, service), &r)
		})
		if err != nil {
			return nil, err
		}
		// Several services are merged into one table, which is capped per call rather than overall.
		var sessions []appSession
		truncated := false
		for _, r := range responses {
			sessions = append(sessions, r.Response.Sessions...)
			truncated = truncated || r.Response.Truncated
		}
		sortSessionsNewestFirst(sessions)
		return data.Frames{sessionsFrame(refID, sessions, truncated, resolved.Clamped, d.dashboardURL)}, nil

	case kindSessionTimeline:
		if newTimelineRequest(q, window).SessionID == "" {
			return data.Frames{}, nil
		}
		var r timelineResponse
		if err := d.client.post(ctx, "/api/app-sessions/timeline", newTimelineRequest(q, window), &r); err != nil {
			return nil, err
		}
		return data.Frames{sessionTimelineFrame(refID, r, d.dashboardURL)}, nil

	case kindErrors:
		var r exceptionGroupsResponse
		if err := d.client.post(ctx, "/api/errors/groups", newErrorsRequest(q, window), &r); err != nil {
			return nil, err
		}
		return data.Frames{errorGroupsFrame(refID, r.Groups, d.dashboardURL)}, nil

	case kindIssues:
		var r errorIssueListResponse
		if err := d.client.get(ctx, "/api/errors/issues", nil, &r); err != nil {
			return nil, err
		}
		return data.Frames{errorIssuesFrame(refID, r.Issues, d.dashboardURL)}, nil

	case kindReleaseHealth:
		responses, err := fanOut(services, func(service string) (releaseHealthResponse, error) {
			var r releaseHealthResponse
			return r, d.client.post(ctx, "/api/app-sessions/release-health", newWindowRequest(resolved, service), &r)
		})
		if err != nil {
			return nil, err
		}
		// A Service column appears whenever the query named a service.
		return data.Frames{releaseHealthFrame(refID, responses, resolved.Clamped)}, nil

	case kindReleaseHealthTrend:
		responses, err := fanOut(services, func(service string) (releaseHealthTrendResponse, error) {
			var r releaseHealthTrendResponse
			return r, d.client.post(ctx, "/api/app-sessions/release-health/trend", newTrendRequest(q, resolved, service, interval), &r)
		})
		if err != nil {
			return nil, err
		}
		return releaseHealthTrendFrames(refID, labelled(responses), q.TrendMetric, resolved.Clamped), nil

	case kindReleaseHealthValue:
		responses, err := fanOut(services, func(service string) (releaseHealthResponse, error) {
			var r releaseHealthResponse
			return r, d.client.post(ctx, "/api/app-sessions/release-health", newWindowRequest(resolved, service), &r)
		})
		if err != nil {
			return nil, err
		}
		return releaseHealthValueFrames(refID, labelled(responses), q.ValueMetric, q.MinSessions, resolved.Clamped), nil

	case kindReleases:
		responses, err := fanOut(services, func(service string) (releaseListResponse, error) {
			var r releaseListResponse
			params := map[string]string{}
			if service != "" {
				params["service"] = service
			}
			return r, d.client.get(ctx, "/api/releases", params, &r)
		})
		if err != nil {
			return nil, err
		}
		var releases []release
		for _, r := range responses {
			for _, rel := range r.Response.Releases {
				if deployed := parseTime(rel.DeployedAt); !deployed.Before(window.From) && !deployed.After(window.To) {
					releases = append(releases, rel)
				}
			}
		}
		return data.Frames{releasesFrame(refID, releases)}, nil

	case kindAppStarts, kindScreenPerformance:
		responses, err := fanOut(services, func(service string) (performanceResponse, error) {
			var r performanceResponse
			return r, d.client.post(ctx, "/api/app-sessions/performance", newPerformanceRequest(q, resolved, service), &r)
		})
		if err != nil {
			return nil, err
		}
		if q.Kind == kindAppStarts {
			return data.Frames{appStartsFrame(refID, labelled(responses), resolved.Clamped)}, nil
		}
		return data.Frames{screenPerformanceFrame(refID, labelled(responses), resolved.Clamped)}, nil
	}
	return data.Frames{}, nil
}

// CheckHealth reads the cheapest authenticated endpoint: it fails with 401 for a missing or wrong token.
func (d *Datasource) CheckHealth(ctx context.Context, _ *backend.CheckHealthRequest) (*backend.CheckHealthResult, error) {
	if d.client.baseURL == "" {
		return &backend.CheckHealthResult{Status: backend.HealthStatusError, Message: "Set the Flare API URL."}, nil
	}
	var r releaseListResponse
	if err := d.client.get(ctx, "/api/releases", nil, &r); err != nil {
		return &backend.CheckHealthResult{Status: backend.HealthStatusError, Message: describeError(err)}, nil
	}
	return &backend.CheckHealthResult{Status: backend.HealthStatusOk, Message: "Connected to Flare."}, nil
}
