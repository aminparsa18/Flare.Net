#!/usr/bin/env node
// Records real Flare.Api responses as benchmark fixtures:
//   node scripts/bench-report/capture.mjs [apiUrl]      (or: scripts/bench.sh --capture [apiUrl])
//
// Point it at a stack seeded by examples/ExampleApp.Seeder (see src/Flare.Benchmarks/README.md).
// FLARE_TOKEN (a PAT) is sent as a bearer token when the API requires auth. Writes, under
// src/Flare.Benchmarks/Fixtures/Captured/:
//   logs-page.json  POST /api/logs/search, one 200-row page (the dashboard's default page)
//   trace.json      GET /api/traces/{id}, the largest trace in the window
//   span-pool.json  200 spans from ~all traces in the window, re-parented into one large
//                   trace by the trace-large.synthetic scenario (seeded traces are tiny)
//   metrics.json    POST /api/metrics/query, a latency-by-route panel (p50..p99, top 10, 30s buckets)
//   capture-info.json  when/where/what was requested
// Responses are stored as the API's JSON, indented for review; the .NET side re-encodes them.
import { mkdirSync, writeFileSync } from 'node:fs';
import path from 'node:path';

const api = (process.argv[2] ?? process.env.FLARE_API_URL ?? 'http://localhost:8080').replace(/\/$/, '');
const outDir = path.resolve(import.meta.dirname, '../../src/Flare.Benchmarks/Fixtures/Captured');
const headers = { 'Content-Type': 'application/json', Accept: 'application/json' };
if (process.env.FLARE_TOKEN) headers.Authorization = `Bearer ${process.env.FLARE_TOKEN}`;

async function call(method, route, body) {
	const res = await fetch(api + route, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
	if (!res.ok) throw new Error(`${method} ${route} -> ${res.status} ${await res.text()}`);
	return res.json();
}

const to = new Date();
const from = new Date(to.getTime() - 60 * 60 * 1000);
const window = { from: from.toISOString(), to: to.toISOString() };

const requests = {
	logs: { filter: window, pageSize: 200 },
	roots: { filter: { ...window, rootSpansOnly: true }, pageSize: 200, sortBy: 'SpanCount' },
	metrics: {
		metricName: 'http.server.request.duration',
		type: 'Histogram',
		filter: window,
		bucketWidthSeconds: 30,
		groupByAttributeKey: 'url.path',
		topN: 10
	}
};

const logs = await call('POST', '/api/logs/search', requests.logs);
if (logs.events.length < 200) throw new Error(`Only ${logs.events.length} log rows in the last hour - seed more data first.`);

const roots = (await call('POST', '/api/spans/search', requests.roots)).spans;
if (roots.length === 0) throw new Error('No traces in the last hour - seed more data first.');
const largest = roots.reduce((a, b) => ((b.spanCount ?? 0) > (a.spanCount ?? 0) ? b : a));
const trace = await call('GET', `/api/traces/${largest.traceId}`);

// Span pool: walk the root list (spread across the window and every seeded scenario) and
// collect whole traces until 200 spans.
const pool = [];
const stride = Math.max(1, Math.floor(roots.length / 80));
for (let i = 0; i < roots.length && pool.length < 200; i += stride) {
	pool.push(...(await call('GET', `/api/traces/${roots[i].traceId}`)).spans);
}
const spanPool = { traceId: '', spans: pool.slice(0, 200), truncated: false, partialSpanIds: [] };

const metrics = await call('POST', '/api/metrics/query', requests.metrics);
if (metrics.series.length === 0) throw new Error(`No series for ${requests.metrics.metricName} - seed the 'cardinality' scenario.`);

mkdirSync(outDir, { recursive: true });
const write = (name, value) => writeFileSync(path.join(outDir, name), JSON.stringify(value, null, 1) + '\n');
write('logs-page.json', logs);
write('trace.json', trace);
write('span-pool.json', spanPool);
write('metrics.json', metrics);
write('capture-info.json', {
	capturedAt: to.toISOString(),
	api,
	dataSource: 'examples/ExampleApp.Seeder scenarios: overview structure cardinality funnel external messaging',
	requests: { ...requests, trace: `GET /api/traces/${largest.traceId}` }
});

console.log(
	`Captured: ${logs.events.length} log rows, trace of ${trace.spans.length} spans, span pool of ${spanPool.spans.length}, ` +
		`${metrics.series.length} metric series (${metrics.series.reduce((n, s) => n + s.points.length, 0)} points) -> ${outDir}`
);
