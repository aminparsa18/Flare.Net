package plugin

import (
	"context"
	"encoding/json"
	"net/http"
	"sort"
	"time"
)

// Template variable kinds: what a variable query lists.
const (
	varSessionServices = "sessionServices"
	varSessionVersions = "sessionVersions"
	varErrorServices   = "errorServices"
	varReleaseVersions = "releaseVersions"
)

type variablesRequest struct {
	Kind    string `json:"kind"`
	Service string `json:"service"`
	// From and To are the dashboard range in Unix milliseconds.
	From int64 `json:"from"`
	To   int64 `json:"to"`
}

type variablesResponse struct {
	Values []string `json:"values"`
}

func sortSessionsNewestFirst(sessions []appSession) {
	sort.SliceStable(sessions, func(i, j int) bool { return sessions[i].LastSeenUnixMs > sessions[j].LastSeenUnixMs })
}

// handleVariables is the resource behind the template variable editor: POST /variables.
func (d *Datasource) handleVariables(w http.ResponseWriter, r *http.Request) {
	var req variablesRequest
	if err := json.NewDecoder(r.Body).Decode(&req); err != nil {
		writeJSON(w, http.StatusBadRequest, map[string]string{"message": "the request is not valid JSON"})
		return
	}
	values, err := d.variableValues(r.Context(), req)
	if err != nil {
		status := http.StatusBadGateway
		writeJSON(w, status, map[string]string{"message": describeError(err)})
		return
	}
	writeJSON(w, http.StatusOK, variablesResponse{Values: values})
}

func writeJSON(w http.ResponseWriter, status int, body any) {
	w.Header().Set("Content-Type", "application/json")
	w.WriteHeader(status)
	_ = json.NewEncoder(w).Encode(body)
}

// variableValues are the values a variable of req.Kind offers, for the dashboard's time range.
func (d *Datasource) variableValues(ctx context.Context, req variablesRequest) ([]string, error) {
	window := timeWindow{From: time.UnixMilli(req.From), To: time.UnixMilli(req.To)}
	resolved := resolveWindow(window)
	service := ""
	if services := splitServices(req.Service); len(services) > 0 {
		service = services[0]
	}

	switch req.Kind {
	case varSessionServices, varSessionVersions:
		body := sessionsRequest{WindowMinutes: resolved.WindowMinutes, EndUnixMs: resolved.EndUnixMs}
		if req.Kind == varSessionVersions {
			body.Service = service
		}
		var r appSessionsResponse
		if err := d.client.post(ctx, "/api/app-sessions/list", body, &r); err != nil {
			return nil, err
		}
		if req.Kind == varSessionServices {
			return nonNil(r.Services), nil
		}
		return nonNil(r.Versions), nil

	case varErrorServices:
		body := map[string]any{
			"filter": errorsFilter{From: isoTime(window.From), To: isoTime(window.To)},
			"field":  "Service",
		}
		var r exceptionFacetValuesResponse
		if err := d.client.post(ctx, "/api/errors/facet-values", body, &r); err != nil {
			return nil, err
		}
		values := make([]string, 0, len(r.Values))
		for _, v := range r.Values {
			values = append(values, v.Value)
		}
		return values, nil

	case varReleaseVersions:
		params := map[string]string{}
		if service != "" {
			params["service"] = service
		}
		var r releaseListResponse
		if err := d.client.get(ctx, "/api/releases", params, &r); err != nil {
			return nil, err
		}
		seen := map[string]bool{}
		values := []string{}
		for _, rel := range r.Releases {
			if !seen[rel.Version] {
				seen[rel.Version] = true
				values = append(values, rel.Version)
			}
		}
		return values, nil
	}
	return []string{}, nil
}

func nonNil(s []string) []string {
	if s == nil {
		return []string{}
	}
	return s
}
