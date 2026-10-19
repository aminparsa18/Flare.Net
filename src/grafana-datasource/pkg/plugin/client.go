package plugin

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net/http"
	"net/url"
	"strings"
	"time"
)

// flareClient calls Flare.Api with the personal access token as a bearer token.
type flareClient struct {
	baseURL string
	token   string
	http    *http.Client
}

func newFlareClient(baseURL, token string, client *http.Client) *flareClient {
	if client == nil {
		client = &http.Client{Timeout: 60 * time.Second}
	}
	return &flareClient{baseURL: strings.TrimRight(baseURL, "/"), token: token, http: client}
}

// apiError is a non-2xx answer from Flare.Api, or a failure to reach it (Status 0).
type apiError struct {
	Status int
	Detail string
	Cause  error
}

func (e *apiError) Error() string { return describeError(e) }
func (e *apiError) Unwrap() error { return e.Cause }

// describeError is a message a person can act on, from whatever Flare.Api or the network returned.
func describeError(err error) string {
	var api *apiError
	if !errors.As(err, &api) {
		return err.Error()
	}
	switch api.Status {
	case http.StatusUnauthorized:
		return "Flare rejected the token (401). Check that it is a valid personal access token (flr_pat_...)."
	case http.StatusForbidden:
		return "The token is not allowed to read this (403)."
	case http.StatusNotFound:
		return "Flare returned 404: is the URL the Flare API base, for example http://flare-api:8080?"
	case http.StatusBadGateway, http.StatusGatewayTimeout, 0:
		return "Grafana could not reach Flare. Check the URL, and that the Grafana server can resolve and connect to it."
	}
	if api.Detail != "" {
		return fmt.Sprintf("Flare returned %d: %s.", api.Status, api.Detail)
	}
	return fmt.Sprintf("Flare returned %d.", api.Status)
}

func (c *flareClient) post(ctx context.Context, path string, body, out any) error {
	data, err := json.Marshal(body)
	if err != nil {
		return err
	}
	return c.do(ctx, http.MethodPost, path, bytes.NewReader(data), out)
}

func (c *flareClient) get(ctx context.Context, path string, params map[string]string, out any) error {
	if len(params) > 0 {
		values := url.Values{}
		for k, v := range params {
			values.Set(k, v)
		}
		path += "?" + values.Encode()
	}
	return c.do(ctx, http.MethodGet, path, nil, out)
}

func (c *flareClient) do(ctx context.Context, method, path string, body io.Reader, out any) error {
	req, err := http.NewRequestWithContext(ctx, method, c.baseURL+path, body)
	if err != nil {
		return &apiError{Cause: err}
	}
	req.Header.Set("Accept", "application/json")
	if body != nil {
		req.Header.Set("Content-Type", "application/json")
	}
	if c.token != "" {
		req.Header.Set("Authorization", "Bearer "+c.token)
	}

	resp, err := c.http.Do(req)
	if err != nil {
		return &apiError{Cause: err}
	}
	defer resp.Body.Close()

	// A response is at most a few MB (500 sessions, a capped list of groups); cap the read anyway.
	raw, err := io.ReadAll(io.LimitReader(resp.Body, 64<<20))
	if err != nil {
		return &apiError{Status: resp.StatusCode, Cause: err}
	}
	if resp.StatusCode < 200 || resp.StatusCode > 299 {
		return &apiError{Status: resp.StatusCode, Detail: problemDetail(raw)}
	}
	if err := json.Unmarshal(raw, out); err != nil {
		return fmt.Errorf("Flare returned a response the plugin could not read: %w", err)
	}
	return nil
}

// problemDetail reads the `detail` (RFC 9457) or `message` of an error body, else nothing.
func problemDetail(raw []byte) string {
	var body struct {
		Detail  string `json:"detail"`
		Message string `json:"message"`
	}
	if json.Unmarshal(raw, &body) != nil {
		return ""
	}
	if body.Detail != "" {
		return body.Detail
	}
	return body.Message
}
