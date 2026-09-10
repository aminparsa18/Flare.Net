// Custom adapter-node entrypoint (Dockerfile's ENTRYPOINT points here instead of the
// auto-generated build/index.js) - exists for exactly one reason: adapter-node's own
// handler (build/handler.js) already sets `Cache-Control: public,max-age=31536000,
// immutable` on everything under /_app/immutable/ (the hashed JS/CSS bundles), but sets
// nothing at all on the handful of un-hashed files copied verbatim from static/
// (favicon*, logo.png, no_log.json) - those fall back to sirv's default of ETag-only, so
// a repeat visit still pays a conditional-GET round trip for them. See
// docs-internal/planning/roadmap.md's former "Cache-control headers on the dashboard's
// static assets" item and SigNoz's equivalent nginx-layer fix (signoz#1104/#1057) - this
// repo has no nginx in front of the dashboard (adapter-node, not adapter-static, serves
// it directly per vite.config.ts's comment), so the fix has to live in the Node server
// itself rather than in an nginx config.
//
// This intentionally re-implements only the subset of the generated build/index.js that
// this deployment actually uses (PORT/HOST + SIGTERM/SIGINT graceful shutdown) - none of
// its other env knobs (SOCKET_PATH, LISTEN_FDS systemd socket activation,
// KEEP_ALIVE_TIMEOUT, HEADERS_TIMEOUT, IDLE_TIMEOUT/SHUTDOWN_TIMEOUT) are set anywhere in
// docker-compose.yml, so nothing is lost by not wiring them here. If that changes, either
// wire the missing knob here too or drop this file and go back to build/index.js.
import { createServer } from 'node:http';
import { handler } from './build/handler.js';

// Deliberately NOT content-hashed like the immutable bundle, so a short-ish max-age (not
// `immutable`) - a stale favicon/logo for up to a day after a redeploy is a non-issue,
// unlike stale JS would be.
const STATIC_ASSET_MAX_AGE_SECONDS = 86400; // 1 day

// Matches exactly the current contents of src/dashboard/static/ - deliberately an
// allowlist rather than a blanket extension match (e.g. `.json`) so this can never
// accidentally cache something SvelteKit-managed and non-immutable, like _app/version.json
// (polled by the client to detect a new deploy - caching that would break update
// detection). Extend this list if new files are added under static/.
const CACHEABLE_STATIC_ASSET = /^\/(?:favicon(?:-16x16|-32x32)?\.(?:ico|png|svg)|logo\.png|no_log\.json)$/;

const port = process.env.PORT ?? 3000;
const host = process.env.HOST ?? '0.0.0.0';

const server = createServer((req, res) => {
	const path = (req.url ?? '').split('?')[0];
	if (CACHEABLE_STATIC_ASSET.test(path)) {
		res.setHeader('cache-control', `public, max-age=${STATIC_ASSET_MAX_AGE_SECONDS}`);
	}

	handler(req, res);
});

server.listen(port, host, () => {
	console.log(`Listening on ${host}:${port}`);
});

// Same idea as adapter-node's own generated shutdown handling (build/index.js): stop
// accepting new connections and close idle ones immediately on `docker stop`'s SIGTERM,
// rather than hanging until keep-alive connections time out on their own.
function gracefulShutdown() {
	server.closeIdleConnections();
	server.close();
}

process.on('SIGTERM', gracefulShutdown);
process.on('SIGINT', gracefulShutdown);
