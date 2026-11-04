#!/usr/bin/env node
// Builds one benchmark run's results.json + report.html from its raw/ folder:
//   node scripts/bench-report/build-report.mjs docs-internal/benchmarks/<date>
//
// Inputs (written by scripts/bench.sh): raw/sizes.json (fixture manifest), raw/dotnet/*-report-brief.json
// (BenchmarkDotNet), raw/node/*.json (tinybench), env.json. Output is self-contained: inline CSS, inline
// SVG charts, a few lines of inline JS for tooltips - no CDN, works offline and from a file:// URL.
// The previous run (the latest sibling directory with a results.json) is compared automatically.
import { existsSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';

const runDir = path.resolve(process.argv[2] ?? '');
if (!process.argv[2] || !existsSync(path.join(runDir, 'raw'))) {
	console.error('usage: build-report.mjs <run dir containing raw/>');
	process.exit(2);
}

// ---- model --------------------------------------------------------------------------------

/** Link speeds the transfer stage is modeled at (bytes * 8 / bps). */
const LINKS = [
	{ id: '1gbps', label: '1 Gbps', bps: 1e9 },
	{ id: '100mbps', label: '100 Mbps', bps: 100e6 },
	{ id: '20mbps', label: '20 Mbps', bps: 20e6 },
	{ id: '5mbps', label: '5 Mbps', bps: 5e6 }
];
const FORMATS = ['json', 'memorypack'];
const FORMAT_LABEL = { json: 'JSON', memorypack: 'MemoryPack' };
const STAGES = [
	{ id: 'encode', label: 'Server encode' },
	{ id: 'compress', label: 'Server compress' },
	{ id: 'transfer', label: 'Transfer (modeled)' },
	{ id: 'decompress', label: 'Client decompress' },
	{ id: 'decodeMap', label: 'Client decode + map' }
];
const SUITE_OF_TYPE = {
	RedisBufferCodecBenchmarks: 'redis-buffer',
	ApiCodecBenchmarks: 'api-codec',
	RequestCodecBenchmarks: 'request-codec',
	CompressionBenchmarks: 'compression',
	ServerStageBenchmarks: 'server-stage'
};

const readJson = (p) => JSON.parse(readFileSync(p, 'utf8'));
const listJson = (dir) => (existsSync(dir) ? readdirSync(dir).filter((f) => f.endsWith('.json')).sort() : []);

const sizes = readJson(path.join(runDir, 'raw/sizes.json'));
const env = existsSync(path.join(runDir, 'env.json')) ? readJson(path.join(runDir, 'env.json')) : {};

let bdnHost = null;
const dotnet = [];
for (const f of listJson(path.join(runDir, 'raw/dotnet'))) {
	const report = readJson(path.join(runDir, 'raw/dotnet', f));
	bdnHost ??= report.HostEnvironmentInfo ?? null;
	for (const b of report.Benchmarks ?? []) {
		const params = Object.fromEntries(
			(b.Parameters ?? '')
				.split('&')
				.filter(Boolean)
				.map((kv) => kv.split('='))
		);
		dotnet.push({
			suite: SUITE_OF_TYPE[b.Type] ?? b.Type,
			method: b.Method,
			params,
			key: `${SUITE_OF_TYPE[b.Type] ?? b.Type}/${b.Method}${b.Parameters ? `?${b.Parameters}` : ''}`,
			medianMs: b.Statistics.Median / 1e6,
			meanMs: b.Statistics.Mean / 1e6,
			ciMarginMs: (b.Statistics.ConfidenceInterval?.Margin ?? 0) / 1e6,
			allocatedBytes: b.Memory?.BytesAllocatedPerOperation ?? null
		});
	}
}

const node = [];
let nodeMeta = null;
for (const f of listJson(path.join(runDir, 'raw/node'))) {
	const r = readJson(path.join(runDir, 'raw/node', f));
	nodeMeta ??= { node: r.node, v8: r.v8, tinybench: r.tinybench, time: r.time };
	for (const row of r.rows) node.push({ ...row, key: `${row.suite}/${row.name}`, medianMs: row.p50Ms });
}

const findDotnet = (suite, method, params = {}) =>
	dotnet.find((d) => d.suite === suite && d.method === method && Object.entries(params).every(([k, v]) => d.params[k] === v));
const findNode = (suite, tags) => node.find((n) => n.suite === suite && Object.entries(tags).every(([k, v]) => n[k] === v));

// Workflow: server stage (ServerStageBenchmarks) + modeled transfer + client stage (client-stage suite).
const workflow = [];
for (const s of sizes.scenarios) {
	for (const format of FORMATS) {
		const serverNone = findDotnet('server-stage', 'Respond', { Scenario: s.name, Format: format, Codec: 'none' });
		const clientNone = findNode('client-stage', { scenario: s.name, format, codec: 'none' });
		if (!serverNone || !clientNone) continue;
		for (const codec of sizes.codecs) {
			const server = findDotnet('server-stage', 'Respond', { Scenario: s.name, Format: format, Codec: codec });
			const client = findNode('client-stage', { scenario: s.name, format, codec });
			if (!server || !client) continue;
			const bytes = s.sizes[format][codec];
			for (const link of LINKS) {
				const stages = {
					encode: serverNone.medianMs,
					compress: Math.max(0, server.medianMs - serverNone.medianMs),
					transfer: ((bytes * 8) / link.bps) * 1000,
					decompress: Math.max(0, client.medianMs - clientNone.medianMs),
					decodeMap: clientNone.medianMs
				};
				const totalMs = Object.values(stages).reduce((a, b) => a + b, 0);
				workflow.push({ scenario: s.name, format, codec, link: link.id, bytes, stages, totalMs });
			}
		}
	}
}

// ---- previous run --------------------------------------------------------------------------

const parent = path.dirname(runDir);
const previousName = readdirSync(parent)
	.filter((d) => d < path.basename(runDir) && existsSync(path.join(parent, d, 'results.json')))
	.sort()
	.at(-1);
const previous = previousName ? readJson(path.join(parent, previousName, 'results.json')) : null;

const results = {
	schema: 1,
	run: path.basename(runDir),
	env: { ...env, benchmarkDotNet: bdnHost ? { version: bdnHost.BenchmarkDotNetVersion, runtime: bdnHost.RuntimeVersion, cpu: bdnHost.ProcessorName } : null, tinybench: nodeMeta },
	links: LINKS,
	scenarios: sizes.scenarios,
	request: sizes.request,
	dotnet,
	node,
	workflow,
	previousRun: previousName ?? null
};
writeFileSync(path.join(runDir, 'results.json'), JSON.stringify(results, null, 1) + '\n');

// ---- formatting ----------------------------------------------------------------------------

const esc = (s) => String(s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
function fmtMs(ms) {
	if (ms == null || Number.isNaN(ms)) return '-';
	if (ms < 0.001) return `${(ms * 1e6).toFixed(0)} ns`;
	if (ms < 1) return `${(ms * 1000).toFixed(ms < 0.01 ? 2 : 1)} µs`;
	if (ms < 100) return `${ms.toFixed(2)} ms`;
	return `${ms.toFixed(0)} ms`;
}
function fmtBytes(b) {
	if (b < 1000) return `${b} B`;
	if (b < 1e6) return `${(b / 1000).toFixed(1)} kB`;
	return `${(b / 1e6).toFixed(2)} MB`;
}
const fmtX = (x) => `${x.toFixed(x < 10 ? 2 : 1)}×`;
const pct = (x) => `${x > 0 ? '+' : ''}${(x * 100).toFixed(0)}%`;
const label = (format, codec) => `${FORMAT_LABEL[format]} · ${codec}`;
const sourceBadge = (src) => `<span class="badge badge-${esc(src)}">${esc(src)}</span>`;

// ---- SVG charts ----------------------------------------------------------------------------

const W = 760;
const LABEL_W = 210;
const VALUE_W = 86;
const ROW_H = 18;
const ROW_GAP = 6;
const GROUP_H = 24;

/** A bar with a square baseline end and 4px rounded data end. */
function bar(x, y, w, h, fill, tip) {
	const r = Math.min(4, h / 2, w);
	const d =
		w <= 0.5
			? `M${x},${y}h0.5v${h}h-0.5z`
			: `M${x},${y}h${w - r}a${r},${r} 0 0 1 ${r},${r}v${h - 2 * r}a${r},${r} 0 0 1 ${-r},${r}h${-(w - r)}z`;
	return `<path d="${d}" fill="${fill}" data-tip="${esc(tip)}"><title>${esc(tip)}</title></path>`;
}

/** Horizontal bars, optionally grouped. rows: [{group?, label, value, color, tip}] */
function barChart(rows, fmt) {
	const max = Math.max(...rows.map((r) => r.value), 1e-12);
	const plotW = W - LABEL_W - VALUE_W;
	let y = 4;
	let lastGroup;
	const parts = [];
	for (const r of rows) {
		if (r.group !== undefined && r.group !== lastGroup) {
			y += lastGroup === undefined ? 0 : 6;
			parts.push(`<text x="0" y="${y + 15}" class="g">${esc(r.group)}</text>`);
			y += GROUP_H;
			lastGroup = r.group;
		}
		const w = (r.value / max) * plotW;
		parts.push(`<text x="${LABEL_W - 8}" y="${y + ROW_H / 2 + 4}" class="l" text-anchor="end">${esc(r.label)}</text>`);
		parts.push(bar(LABEL_W, y, w, ROW_H, r.color, r.tip ?? `${r.label}: ${fmt(r.value)}`));
		parts.push(`<text x="${LABEL_W + w + 6}" y="${y + ROW_H / 2 + 4}" class="v">${esc(fmt(r.value))}</text>`);
		y += ROW_H + ROW_GAP;
	}
	parts.unshift(`<line x1="${LABEL_W}" x2="${LABEL_W}" y1="0" y2="${y}" class="axis"/>`);
	return `<svg viewBox="0 0 ${W} ${y}" role="img" class="chart">${parts.join('')}</svg>`;
}

/** Horizontal stacked bars. rows: [{label, segments: [{id, value}], total}] ; colors by segment id. */
function stackedChart(rows, colors, labels) {
	const max = Math.max(...rows.map((r) => r.total), 1e-12);
	const plotW = W - LABEL_W - VALUE_W;
	let y = 4;
	const parts = [];
	for (const r of rows) {
		parts.push(`<text x="${LABEL_W - 8}" y="${y + ROW_H / 2 + 4}" class="l${r.best ? ' best' : ''}" text-anchor="end">${esc(r.label)}</text>`);
		let x = LABEL_W;
		const visible = r.segments.filter((s) => (s.value / max) * plotW >= 0.5);
		visible.forEach((s, i) => {
			const full = (s.value / max) * plotW;
			const gap = i < visible.length - 1 && full > 3 ? 2 : 0;
			const tip = `${r.label} - ${labels[s.id]}: ${fmtMs(s.value)} (total ${fmtMs(r.total)})`;
			if (i === visible.length - 1) parts.push(bar(x, y, full, ROW_H, colors[s.id], tip));
			else parts.push(`<rect x="${x}" y="${y}" width="${Math.max(0.5, full - gap)}" height="${ROW_H}" fill="${colors[s.id]}" data-tip="${esc(tip)}"><title>${esc(tip)}</title></rect>`);
			x += full;
		});
		parts.push(`<text x="${x + 6}" y="${y + ROW_H / 2 + 4}" class="v${r.best ? ' best' : ''}">${esc(fmtMs(r.total))}</text>`);
		y += ROW_H + ROW_GAP;
	}
	parts.unshift(`<line x1="${LABEL_W}" x2="${LABEL_W}" y1="0" y2="${y}" class="axis"/>`);
	return `<svg viewBox="0 0 ${W} ${y}" role="img" class="chart">${parts.join('')}</svg>`;
}

function table(head, rows) {
	return `<div class="tw"><table><thead><tr>${head.map((h) => `<th>${h}</th>`).join('')}</tr></thead><tbody>${rows
		.map((r) => `<tr>${r.map((c, i) => `<td${i > 0 ? ' class="n"' : ''}>${c}</td>`).join('')}</tr>`)
		.join('')}</tbody></table></div>`;
}
const tableView = (head, rows) => `<details class="tv"><summary>Table view</summary>${table(head, rows)}</details>`;
const legend = (items) => `<div class="legend">${items.map(([c, t]) => `<span><i style="background:${c}"></i>${esc(t)}</span>`).join('')}</div>`;

const FORMAT_COLOR = { json: 'var(--series-1)', memorypack: 'var(--series-2)' };
const STAGE_COLOR = { encode: 'var(--series-1)', compress: 'var(--series-2)', transfer: 'var(--series-3)', decompress: 'var(--series-4)', decodeMap: 'var(--series-5)' };
const STAGE_LABEL = Object.fromEntries(STAGES.map((s) => [s.id, s.label]));
const formatLegend = legend(FORMATS.map((f) => [FORMAT_COLOR[f], FORMAT_LABEL[f]]));

// ---- summary -------------------------------------------------------------------------------

const scenarioNames = sizes.scenarios.map((s) => s.name);
const range = (xs) => (xs.length === 0 ? null : [Math.min(...xs), Math.max(...xs)]);
const fmtRange = (r, f) => (r == null ? 'n/a' : r[0] === r[1] ? f(r[0]) : `${f(r[0])} – ${f(r[1])}`);
const bullets = [];

const dotnetRatios = (methodA, methodB) =>
	scenarioNames
		.map((s) => [findDotnet('api-codec', methodA, { Scenario: s }), findDotnet('api-codec', methodB, { Scenario: s })])
		.filter(([a, b]) => a && b)
		.map(([a, b]) => a.medianMs / b.medianMs);
const enc = range(dotnetRatios('Json_Encode', 'MemoryPack_Encode'));
const dec = range(dotnetRatios('Json_Decode', 'MemoryPack_Decode'));
if (enc && dec) {
	bullets.push(
		`<b>.NET (Flare.Api) codec:</b> MemoryPack encodes ${fmtRange(enc, fmtX)} and decodes ${fmtRange(dec, fmtX)} faster than System.Text.Json across the response scenarios (ratio = JSON time ÷ MemoryPack time).`
	);
}

const clientRatios = scenarioNames
	.map((s) => [findNode('decode', { scenario: s, format: 'memorypack', op: 'decode+map' }), findNode('decode', { scenario: s, format: 'json', op: 'decode+map' })])
	.filter(([a, b]) => a && b)
	.map(([mp, js]) => mp.medianMs / js.medianMs);
if (clientRatios.length) {
	const r = range(clientRatios);
	bullets.push(
		`<b>Dashboard decode (Node ${esc(nodeMeta?.node ?? '')}, V8 ${esc(nodeMeta?.v8 ?? '')}):</b> bytes → UI types takes ${fmtRange(r, fmtX)} as long with MemoryPack as with <code>TextDecoder</code> + <code>JSON.parse</code> + mapping (ratio = MemoryPack time ÷ JSON time; above 1 means JSON is faster).`
	);
}

const sizeRatio = (codec) => sizes.scenarios.map((s) => s.sizes.memorypack[codec] / s.sizes.json[codec] - 1);
const sizeBullet = ['none', 'gzip-6', 'brotli-4']
	.filter((c) => sizes.codecs.includes(c))
	.map((c) => `${c === 'none' ? 'uncompressed' : c}: ${fmtRange(range(sizeRatio(c)), pct)}`)
	.join('; ');
bullets.push(`<b>Payload size, MemoryPack vs JSON</b> (negative = MemoryPack smaller): ${sizeBullet}.`);

if (workflow.length) {
	const best = (scenario, link, format) =>
		workflow.filter((w) => w.scenario === scenario && w.link === link && (!format || w.format === format)).sort((a, b) => a.totalMs - b.totalMs)[0];
	let jsonWins = 0;
	let cells = 0;
	for (const s of scenarioNames)
		for (const l of LINKS) {
			const j = best(s, l.id, 'json');
			const m = best(s, l.id, 'memorypack');
			if (j && m) {
				cells++;
				if (j.totalMs <= m.totalMs) jsonWins++;
			}
		}
	bullets.push(
		`<b>End to end</b> (server encode + compression + modeled transfer + client decompress/decode/map), comparing each format at its own best codec: JSON is faster in ${jsonWins} of ${cells} scenario × link-speed cells, MemoryPack in ${cells - jsonWins}. See the matrix below.`
	);
	const perLink = LINKS.map((l) => {
		const winners = scenarioNames.map((s) => best(s, l.id)).filter(Boolean);
		const compressed = winners.filter((w) => w.codec !== 'none').length;
		return `${l.label}: ${compressed}/${winners.length}`;
	});
	bullets.push(`<b>Compression</b> - scenarios where the fastest overall combination is a compressed one, per link: ${perLink.join(', ')}.`);
}

function verdictMatrix() {
	if (!workflow.length) return '';
	const head = ['Scenario', ...LINKS.map((l) => l.label)];
	const rows = scenarioNames
		.filter((s) => workflow.some((w) => w.scenario === s))
		.map((s) => {
			const scen = sizes.scenarios.find((x) => x.name === s);
			return [
				`${esc(s)} ${sourceBadge(scen.source)}`,
				...LINKS.map((l) => {
					const ws = workflow.filter((w) => w.scenario === s && w.link === l.id).sort((a, b) => a.totalMs - b.totalMs);
					const j = ws.find((w) => w.format === 'json');
					const m = ws.find((w) => w.format === 'memorypack');
					const winner = ws[0];
					const delta = j && m ? (m.totalMs - j.totalMs) / j.totalMs : 0;
					return `<b>${esc(label(winner.format, winner.codec))}</b><br><span class="muted">${fmtMs(winner.totalMs)} · best MP vs best JSON ${pct(delta)}</span>`;
				})
			];
		});
	return `<p class="muted">Fastest combination per cell, and how the best MemoryPack combination compares with the best JSON one (positive = MemoryPack slower).</p>${table(head, rows)}`;
}

// ---- sections ------------------------------------------------------------------------------

const sections = [];

sections.push(`<section><h2>Scenarios</h2>${table(
	['Scenario', 'Endpoint', 'Shape', 'JSON', 'MemoryPack'],
	sizes.scenarios.map((s) => [`<b>${esc(s.name)}</b> ${sourceBadge(s.source)}<br><span class="muted">${esc(s.description)}</span>`, esc(s.endpoint), esc(s.shape), fmtBytes(s.sizes.json.none), fmtBytes(s.sizes.memorypack.none)])
)}<p class="muted"><b>captured</b>: recorded from a running stack seeded by <code>examples/ExampleApp.Seeder</code>. <b>synthetic</b>: generated in code (see each description). Request body measured separately: <code>${esc(sizes.request.name)}</code>, ${fmtBytes(sizes.request.sizes.json.none)} JSON / ${fmtBytes(sizes.request.sizes.memorypack.none)} MemoryPack.</p></section>`);

if (workflow.length) {
	const blocks = scenarioNames
		.filter((s) => workflow.some((w) => w.scenario === s))
		.map((s, i) => {
			const scen = sizes.scenarios.find((x) => x.name === s);
			const perLink = LINKS.map((l) => {
				const ws = workflow.filter((w) => w.scenario === s && w.link === l.id);
				const bestTotal = Math.min(...ws.map((w) => w.totalMs));
				const rows = ws.map((w) => ({
					label: label(w.format, w.codec),
					best: w.totalMs === bestTotal,
					total: w.totalMs,
					segments: STAGES.map((st) => ({ id: st.id, value: w.stages[st.id] }))
				}));
				return `<h4>${esc(l.label)}</h4>${stackedChart(rows, STAGE_COLOR, STAGE_LABEL)}${tableView(
					['Format · codec', 'Bytes', ...STAGES.map((st) => st.label), 'Total'],
					ws.map((w) => [esc(label(w.format, w.codec)), fmtBytes(w.bytes), ...STAGES.map((st) => fmtMs(w.stages[st.id])), `<b>${fmtMs(w.totalMs)}</b>`])
				)}`;
			}).join('');
			return `<details class="scenario"${i === 0 ? ' open' : ''}><summary>${esc(s)} ${sourceBadge(scen.source)} <span class="muted">${esc(scen.shape)} · ${esc(scen.endpoint)}</span></summary>${perLink}</details>`;
		})
		.join('');
	sections.push(`<section><h2>End to end, by stage</h2><p class="muted">Per link speed, each format × codec as a stack of its stages. Each link has its own scale; the fastest row is in bold. Transfer is <code>bytes × 8 ÷ bandwidth</code> (no RTT, TCP slow start or HTTP overhead, which are the same for both formats).</p>${legend(STAGES.map((s) => [STAGE_COLOR[s.id], s.label]))}${blocks}</section>`);
}

{
	const blocks = sizes.scenarios
		.map((s) => {
			const rows = FORMATS.flatMap((f) =>
				sizes.codecs.map((c) => ({ label: label(f, c), value: s.sizes[f][c], color: FORMAT_COLOR[f], tip: `${label(f, c)}: ${fmtBytes(s.sizes[f][c])} (${((s.sizes[f][c] / s.sizes[f].none) * 100).toFixed(0)}% of raw)` }))
			);
			return `<h4>${esc(s.name)} ${sourceBadge(s.source)}</h4>${barChart(rows, fmtBytes)}${tableView(
				['Codec', 'JSON', 'MemoryPack', 'MP vs JSON'],
				sizes.codecs.map((c) => [c, fmtBytes(s.sizes.json[c]), fmtBytes(s.sizes.memorypack[c]), pct(s.sizes.memorypack[c] / s.sizes.json[c] - 1)])
			)}`;
		})
		.join('');
	sections.push(`<section><h2>Payload size, raw vs compressed</h2>${formatLegend}${blocks}</section>`);
}

function componentChart(title, note, rows, head, tableRows) {
	if (!rows.length) return '';
	return `<h3>${title}</h3>${note ? `<p class="muted">${note}</p>` : ''}${barChart(rows, fmtMs)}${tableView(head, tableRows)}`;
}

{
	const parts = [];
	const api = dotnet.filter((d) => d.suite === 'api-codec');
	parts.push(
		componentChart(
			'Flare.Api response codec (ApiCodecBenchmarks)',
			'System.Text.Json (source-generated context) vs MemoryPack, in memory, no HTTP or compression.',
			api.map((d) => ({ group: d.params.Scenario, label: d.method.replace('_', ' '), value: d.medianMs, color: d.method.startsWith('Json') ? FORMAT_COLOR.json : FORMAT_COLOR.memorypack })),
			['Scenario', 'Method', 'Median', '± CI', 'Allocated'],
			api.map((d) => [esc(d.params.Scenario), esc(d.method), fmtMs(d.medianMs), fmtMs(d.ciMarginMs), d.allocatedBytes == null ? '-' : fmtBytes(d.allocatedBytes)])
		)
	);
	const comp = dotnet.filter((d) => d.suite === 'compression');
	parts.push(
		componentChart(
			'Server compression cost (CompressionBenchmarks)',
			'Time to compress an already-encoded body. Hover for the resulting size.',
			comp.map((d) => {
				const s = sizes.scenarios.find((x) => x.name === d.params.Scenario);
				const ratio = s ? s.sizes[d.params.Format][d.params.Codec] / s.sizes[d.params.Format].none : NaN;
				return { group: d.params.Scenario, label: label(d.params.Format, d.params.Codec), value: d.medianMs, color: FORMAT_COLOR[d.params.Format], tip: `${label(d.params.Format, d.params.Codec)}: ${fmtMs(d.medianMs)}, output ${(ratio * 100).toFixed(0)}% of input` };
			}),
			['Scenario', 'Format · codec', 'Median', 'Output / input'],
			comp.map((d) => {
				const s = sizes.scenarios.find((x) => x.name === d.params.Scenario);
				return [esc(d.params.Scenario), esc(label(d.params.Format, d.params.Codec)), fmtMs(d.medianMs), s ? `${((s.sizes[d.params.Format][d.params.Codec] / s.sizes[d.params.Format].none) * 100).toFixed(1)}%` : '-'];
			})
		)
	);
	const req = dotnet.filter((d) => d.suite === 'request-codec');
	parts.push(
		componentChart(
			'Flare.Api request-body decode (RequestCodecBenchmarks)',
			`<code>${esc(sizes.request.name)}</code>: the ADR-0015 request direction.`,
			req.map((d) => ({ label: d.method.replace('_', ' '), value: d.medianMs, color: d.method.startsWith('Json') ? FORMAT_COLOR.json : FORMAT_COLOR.memorypack })),
			['Method', 'Median', 'Allocated'],
			req.map((d) => [esc(d.method), fmtMs(d.medianMs), d.allocatedBytes == null ? '-' : fmtBytes(d.allocatedBytes)])
		)
	);
	const redis = dotnet.filter((d) => d.suite === 'redis-buffer');
	parts.push(
		componentChart(
			'Ingest Redis buffer codec (RedisBufferCodecBenchmarks)',
			'.NET ↔ .NET, one <code>LogEvent</code> per Redis Stream entry (ADR-0017). Synthetic Typical / AttributeHeavy events.',
			redis.map((d) => {
				const [fmt, op, shape] = d.method.split('_');
				return { group: shape, label: `${fmt} ${op}`, value: d.medianMs, color: fmt === 'Json' ? FORMAT_COLOR.json : FORMAT_COLOR.memorypack };
			}),
			['Method', 'Median', 'Allocated'],
			redis.map((d) => [esc(d.method), fmtMs(d.medianMs), d.allocatedBytes == null ? '-' : fmtBytes(d.allocatedBytes)])
		)
	);
	if (parts.some(Boolean)) sections.push(`<section><h2>Server components (.NET, BenchmarkDotNet)</h2>${formatLegend}${parts.join('')}</section>`);
}

{
	const parts = [];
	const dec = node.filter((n) => n.suite === 'decode');
	parts.push(
		componentChart(
			'Dashboard response decode (decode suite)',
			'<i>decode</i>: bytes → parsed object. <i>decode+map</i>: bytes → the UI types <code>src/lib/*-api.ts</code> return. JSON includes the UTF-8 decode that <code>response.json()</code> does.',
			dec.map((n) => ({ group: n.scenario, label: `${FORMAT_LABEL[n.format]} ${n.op}`, value: n.medianMs, color: FORMAT_COLOR[n.format] })),
			['Scenario', 'Format', 'Op', 'Median', 'Mean', '± RME', 'Samples'],
			dec.map((n) => [esc(n.scenario), FORMAT_LABEL[n.format], esc(n.op), fmtMs(n.medianMs), fmtMs(n.meanMs), `${n.rmePercent.toFixed(1)}%`, n.samples])
		)
	);
	const dz = node.filter((n) => n.suite === 'decompress');
	parts.push(
		componentChart(
			'Client decompression (decompress suite)',
			"Node's native zlib, standing in for the browser network stack's native Content-Encoding decoder.",
			dz.map((n) => ({ group: n.scenario, label: label(n.format, n.codec), value: n.medianMs, color: FORMAT_COLOR[n.format] })),
			['Scenario', 'Format · codec', 'Median', '± RME'],
			dz.map((n) => [esc(n.scenario), esc(label(n.format, n.codec)), fmtMs(n.medianMs), `${n.rmePercent.toFixed(1)}%`])
		)
	);
	const re = node.filter((n) => n.suite === 'request-encode');
	parts.push(
		componentChart(
			'Dashboard request-body encode (request-encode suite)',
			'',
			re.map((n) => ({ label: `${FORMAT_LABEL[n.format]} encode`, value: n.medianMs, color: FORMAT_COLOR[n.format] })),
			['Format', 'Median', '± RME'],
			re.map((n) => [FORMAT_LABEL[n.format], fmtMs(n.medianMs), `${n.rmePercent.toFixed(1)}%`])
		)
	);
	if (parts.some(Boolean)) sections.push(`<section><h2>Client components (Node / V8, tinybench)</h2>${formatLegend}${parts.join('')}</section>`);
}

if (previous) {
	const prevByKey = new Map([...previous.dotnet, ...previous.node].map((r) => [r.key, r]));
	const changes = [...dotnet, ...node]
		.filter((r) => prevByKey.has(r.key))
		.map((r) => ({ key: r.key, before: prevByKey.get(r.key).medianMs, after: r.medianMs }))
		.map((c) => ({ ...c, delta: c.after / c.before - 1 }))
		.sort((a, b) => Math.abs(b.delta) - Math.abs(a.delta));
	const big = changes.filter((c) => Math.abs(c.delta) >= 0.1);
	const sizeChanges = sizes.scenarios.flatMap((s) => {
		const p = previous.scenarios.find((x) => x.name === s.name);
		return p ? FORMATS.flatMap((f) => sizes.codecs.filter((c) => p.sizes[f]?.[c] !== s.sizes[f][c]).map((c) => [esc(s.name), esc(label(f, c)), fmtBytes(p.sizes[f]?.[c] ?? 0), fmtBytes(s.sizes[f][c])])) : [];
	});
	const sameMachine = previous.env?.cpu === env.cpu;
	sections.push(`<section><h2>Change since ${esc(previousName)}</h2>${sameMachine ? '' : `<p class="warn">Different machine (${esc(previous.env?.cpu ?? '?')} → ${esc(env.cpu ?? '?')}): timings are not directly comparable.</p>`}<p class="muted">${changes.length} benchmarks in both runs; ${big.length} moved by 10% or more (median).</p>${
		big.length ? table(['Benchmark', 'Before', 'After', 'Change'], big.map((c) => [esc(c.key), fmtMs(c.before), fmtMs(c.after), `<b>${pct(c.delta)}</b>`])) : ''
	}${sizeChanges.length ? `<h3>Payload size changes</h3>${table(['Scenario', 'Format · codec', 'Before', 'After'], sizeChanges)}` : ''}</section>`);
}

sections.push(`<section><h2>Method and caveats</h2><ul>
<li><b>Medians</b> throughout (BenchmarkDotNet <code>Median</code>, tinybench <code>p50</code>); means, error margins and allocations are in the table views and <code>results.json</code>.</li>
<li><b>Server stage</b> runs the production <code>ApiSerialization.Write</code> → <code>IResult.ExecuteAsync</code> path into an in-memory response, through a <code>GZipStream</code>/<code>BrotliStream</code> the way <code>UseResponseCompression</code> wraps the body. Compress = that minus the uncompressed case. ClickHouse query time is excluded (the same for both formats).</li>
<li><b>Client stage</b> runs in Node's V8 (the engine Chrome and Edge use), not a browser. MemoryPack: generated decoder + the production mapper exported from <code>src/lib/*-api.ts</code>. JSON: <code>TextDecoder</code> + <code>JSON.parse</code> + a field-by-field mapper into the same UI types; both are checked to produce identical data before timing. Decompress = that minus the uncompressed case.</li>
<li><b>Transfer</b> is modeled from compressed bytes at fixed link speeds; it ignores RTT, TCP slow start and protocol overhead, and assumes the link is the bottleneck.</li>
<li><b>Fixtures</b>: captured scenarios come from seeded demo data, whose attribute bags are lighter than many production workloads; the synthetic logs page models a heavier, more typical OTel attribute set. Large traces are assembled from captured spans because seeded traces are tiny.</li>
<li>Server and client stages are measured on the same machine; a real deployment has a server CPU and a (usually slower) client device.</li>
</ul></section>`);

const envRows = [
	['Run', esc(results.run)],
	['Date', esc(env.date ?? '-')],
	['CPU', `${esc(env.cpu ?? bdnHost?.ProcessorName ?? '-')} (${esc(env.cores ?? '?')} logical cores, ${esc(env.memoryGb ?? '?')} GB)`],
	['OS', esc(env.os ?? bdnHost?.OsVersion ?? '-')],
	['.NET', `SDK ${esc(env.dotnetSdk ?? '-')}${bdnHost ? `, runtime ${esc(bdnHost.RuntimeVersion)}, BenchmarkDotNet ${esc(bdnHost.BenchmarkDotNetVersion)}` : ''}`],
	['Node / V8', `${esc(env.node ?? nodeMeta?.node ?? '-')} / ${esc(env.v8 ?? nodeMeta?.v8 ?? '-')}${nodeMeta ? `, tinybench ${esc(nodeMeta.tinybench)} (${nodeMeta.time} ms per case)` : ''}`],
	['Git', `${esc(env.gitCommit?.slice(0, 12) ?? '-')} on ${esc(env.gitBranch ?? '-')}${env.gitDirty ? ' (uncommitted changes)' : ''}`],
	['Suites', `${esc(env.suites ?? '-')}${env.quick === 'true' ? ' <b>(quick mode - smoke numbers only)</b>' : ''}`]
];

const html = `<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Serialization Benchmarks ${esc(results.run)}</title>
<style>
:root {
	color-scheme: light;
	--surface-0: #f5f5f3; --surface-1: #fcfcfb; --border: #e2e1dc;
	--text-primary: #0b0b0b; --text-secondary: #52514e; --text-muted: #6f6e69;
	--series-1: #2a78d6; --series-2: #eb6834; --series-3: #1baf7a; --series-4: #eda100; --series-5: #e87ba4;
	--warn-bg: #fff4dc;
}
@media (prefers-color-scheme: dark) {
	:root:not([data-theme="light"]) {
		color-scheme: dark;
		--surface-0: #121211; --surface-1: #1a1a19; --border: #33332f;
		--text-primary: #f0efec; --text-secondary: #c3c2b7; --text-muted: #9a998f;
		--series-1: #3987e5; --series-2: #d95926; --series-3: #199e70; --series-4: #c98500; --series-5: #d55181;
		--warn-bg: #3a2f12;
	}
}
:root[data-theme="dark"] {
	color-scheme: dark;
	--surface-0: #121211; --surface-1: #1a1a19; --border: #33332f;
	--text-primary: #f0efec; --text-secondary: #c3c2b7; --text-muted: #9a998f;
	--series-1: #3987e5; --series-2: #d95926; --series-3: #199e70; --series-4: #c98500; --series-5: #d55181;
	--warn-bg: #3a2f12;
}
* { box-sizing: border-box; }
body { margin: 0; background: var(--surface-0); color: var(--text-primary); font: 15px/1.5 system-ui, -apple-system, "Segoe UI", sans-serif; }
main { max-width: 900px; margin: 0 auto; padding: 24px 16px 64px; overflow-wrap: anywhere; }
h1 { font-size: 26px; margin: 0 0 4px; }
h2 { font-size: 20px; margin: 0 0 12px; }
h3 { font-size: 16px; margin: 24px 0 6px; }
h4 { font-size: 14px; margin: 18px 0 4px; color: var(--text-secondary); }
section { background: var(--surface-1); border: 1px solid var(--border); border-radius: 10px; padding: 20px; margin: 16px 0; }
.muted { color: var(--text-muted); font-size: 13px; }
.warn { background: var(--warn-bg); padding: 8px 12px; border-radius: 6px; }
code { font: 12.5px ui-monospace, SFMono-Regular, Menlo, monospace; }
ul { padding-left: 20px; } li { margin: 6px 0; }
.chart { width: 100%; height: auto; display: block; }
.chart text { font: 12px system-ui, sans-serif; fill: var(--text-secondary); }
.chart text.g { font-weight: 600; fill: var(--text-primary); }
.chart text.v { fill: var(--text-primary); font-variant-numeric: tabular-nums; }
.chart text.best { font-weight: 700; fill: var(--text-primary); }
.chart .axis { stroke: var(--border); }
.chart [data-tip]:hover { opacity: .8; }
.legend { display: flex; flex-wrap: wrap; gap: 14px; font-size: 13px; color: var(--text-secondary); margin: 4px 0 8px; }
.legend i { display: inline-block; width: 12px; height: 12px; border-radius: 3px; margin-right: 6px; vertical-align: -1px; }
.badge { font-size: 11px; padding: 1px 6px; border-radius: 99px; border: 1px solid var(--border); color: var(--text-secondary); font-weight: 500; }
.badge-captured { border-color: var(--series-3); }
.badge-synthetic { border-style: dashed; }
.tw { overflow-x: auto; }
table { border-collapse: collapse; width: 100%; font-size: 13px; margin: 6px 0; }
th, td { text-align: left; padding: 6px 8px; border-bottom: 1px solid var(--border); vertical-align: top; }
td.n { font-variant-numeric: tabular-nums; }
th { color: var(--text-secondary); font-weight: 600; }
details.tv summary { cursor: pointer; font-size: 13px; color: var(--text-muted); margin: 4px 0 10px; }
details.scenario { border-top: 1px solid var(--border); padding: 8px 0; }
details.scenario > summary { cursor: pointer; font-weight: 600; padding: 4px 0; }
#tip { position: fixed; pointer-events: none; background: var(--text-primary); color: var(--surface-1); font-size: 12px; padding: 5px 8px; border-radius: 6px; max-width: 320px; display: none; z-index: 10; }
</style>
</head>
<body>
<main>
<h1>Serialization benchmarks</h1>
<p class="muted">JSON vs MemoryPack, Flare.Api → dashboard · run <b>${esc(results.run)}</b> · ${esc(env.cpu ?? '')} · commit <code>${esc(env.gitCommit?.slice(0, 12) ?? '-')}</code>${env.quick === 'true' ? ' · <b>quick mode</b>' : ''}</p>
<section><h2>Summary</h2><ul>${bullets.map((b) => `<li>${b}</li>`).join('')}</ul>${verdictMatrix()}</section>
${sections.join('\n')}
<section><h2>Environment</h2>${table(['', ''], envRows)}<p class="muted">Raw data: <code>raw/</code> next to this file; normalized: <code>results.json</code>. Regenerate with <code>scripts/bench.sh</code>.</p></section>
</main>
<div id="tip"></div>
<script>
const tip = document.getElementById('tip');
document.addEventListener('mouseover', (e) => { const t = e.target.closest('[data-tip]'); if (!t) { tip.style.display = 'none'; return; } tip.textContent = t.getAttribute('data-tip'); tip.style.display = 'block'; });
document.addEventListener('mousemove', (e) => { const w = tip.offsetWidth; tip.style.left = Math.min(e.clientX + 12, innerWidth - w - 8) + 'px'; tip.style.top = (e.clientY + 14) + 'px'; });
document.querySelectorAll('[data-tip] > title').forEach((t) => t.remove());
</script>
</body>
</html>
`;
writeFileSync(path.join(runDir, 'report.html'), html);
console.log(`Report: ${path.join(runDir, 'report.html')}${previousName ? ` (compared with ${previousName})` : ''}`);
