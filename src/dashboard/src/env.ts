import { defineEnvVars } from '@sveltejs/kit/env';

// Every variable is read from the process environment when the server starts (not baked in at build time):
// the published image is built once and configured per deployment, which is why the app uses adapter-node.
// All are optional - an unset value comes through as undefined and the caller applies its own default.
const optional = (value: string | undefined) => value;

export const variables = defineEnvVars({
	// Where the browser reaches Flare.Api (also read client-side, hence public).
	PUBLIC_API_URL: { public: true, schema: optional },
	// Where this server reaches Flare.Api when that differs from what the browser uses (status-page domain lookups).
	API_INTERNAL_URL: { schema: optional },
	// "Open in Grafana" on a trace: an http(s) URL with a {traceId} placeholder (ADR-0248). Unset hides the button.
	PUBLIC_GRAFANA_TRACE_URL: { public: true, schema: optional },
	// Baseline security headers (ADR-0200); see src/lib/server/security-headers.ts.
	FLARE_SECURITY_HEADERS: { schema: optional },
	FLARE_CSP: { schema: optional },
	FLARE_CSP_FRAME_ANCESTORS: { schema: optional },
	FLARE_CSP_CONNECT_SRC: { schema: optional },
	FLARE_CSP_IMG_SRC: { schema: optional },
	FLARE_HSTS_MAX_AGE: { schema: optional },
	FLARE_HSTS_INCLUDE_SUBDOMAINS: { schema: optional }
});
