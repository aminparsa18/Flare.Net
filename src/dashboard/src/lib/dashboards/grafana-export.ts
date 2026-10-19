// Export of a Flare dashboard as a Grafana dashboard JSON (ADR-0253) - the write side of
// grafana-import.ts, and unlike it, *with* queries: Flare serves Prometheus, Loki and Tempo
// compatible APIs (ADR-0109, ADR-0236, ADR-0217), so a panel's saved query can be rewritten as
// PromQL / LogQL / TraceQL and run by Grafana against Flare as a data source.
//
// The output is a "share externally" export: data sources are `__inputs` placeholders
// (`${DS_FLARE_PROMETHEUS}` etc.) that Grafana's import screen asks the user to pick, and only
// the ones a panel actually uses are declared. Pure and I/O-free apart from the metric catalog
// the caller passes in (units are needed to build Prometheus metric names, and a panel's saved
// query holds only the OTel name).
//
// What does not translate is dropped, not guessed: every panel carries a "Flare export:" line
// in its Grafana description naming what was left out (post-processing functions, compare
// overlays, structural trace queries, ...), and the result counts those panels so the caller can
// say so. Translations that are approximations rather than exact are called out in ADR-0253.

import { parseFormula, type FormulaNode } from '#lib/metrics/formula.ts';
import { matchThreshold, THRESHOLD_COLORS, type PanelThreshold } from './thresholds';
import { GRID_COLUMNS } from './layout';
import type { DashboardLayout, DashboardPanel, DashboardRow, DashboardVariable } from '#lib/dashboards-api.ts';
import type { MetricNameInfo, MetricPointType } from '#lib/metrics-api.ts';
import type { AttributeFilter, BodyJsonFilter } from '#lib/api.ts';
import type { SpanAttributeFilter } from '#lib/traces-api.ts';
import type { LogsSavedViewState } from '#lib/logs/state.svelte.ts';
import type { TracesSavedViewState } from '#lib/traces/state.svelte.ts';
import type { MetricsSavedViewState } from '#lib/metrics/state.svelte.ts';
import type { PanelVisualization } from './visualization';

const GRAFANA_GRID_COLUMNS = 24;
const GRAFANA_SCHEMA_VERSION = 39;

type SourceKind = 'prometheus' | 'loki' | 'tempo';

const SOURCES: Record<SourceKind, { input: string; label: string; pluginName: string }> = {
	prometheus: { input: 'DS_FLARE_PROMETHEUS', label: 'Flare (Prometheus)', pluginName: 'Prometheus' },
	loki: { input: 'DS_FLARE_LOKI', label: 'Flare (Loki)', pluginName: 'Loki' },
	tempo: { input: 'DS_FLARE_TEMPO', label: 'Flare (Tempo)', pluginName: 'Tempo' }
};

export interface GrafanaExportResult {
	/** The Grafana dashboard JSON, ready to stringify. */
	dashboard: Record<string, unknown>;
	/** Panels exported (rows are not counted). */
	panelCount: number;
	/** Panels where some setting had no Grafana equivalent and was left out - each says what in its description. */
	partialCount: number;
}

type Json = Record<string, any>;

// ---- strings ---------------------------------------------------------------

/** A double-quoted PromQL/LogQL/TraceQL string literal. */
function quote(value: string): string {
	return `"${value.replace(/\\/g, '\\\\').replace(/"/g, '\\"').replace(/\n/g, '\\n')}"`;
}

function regexEscape(value: string): string {
	return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

/** `a|b` alternation of literal values, for a `=~` matcher. */
function alternation(values: readonly string[]): string {
	return values.map(regexEscape).join('|');
}

/** Prometheus/Loki label for an OTel attribute key - the same replacement `PromNames.Sanitize` applies. */
function labelName(key: string): string {
	const s = key.replace(/[^a-zA-Z0-9_:]/g, '_');
	return /^[0-9]/.test(s) ? `_${s}` : s;
}

function grafanaVariableName(name: string, taken: Set<string>): string {
	let base = name.trim().replace(/[^a-zA-Z0-9_]/g, '_').replace(/^_+|_+$/g, '') || 'var';
	if (/^[0-9]/.test(base)) base = `v_${base}`;
	let candidate = base;
	for (let i = 2; taken.has(candidate); i++) candidate = `${base}_${i}`;
	taken.add(candidate);
	return candidate;
}

// ---- units -----------------------------------------------------------------

const GRAFANA_UNITS: Record<string, string> = {
	s: 's',
	ms: 'ms',
	us: 'µs',
	ns: 'ns',
	min: 'm',
	h: 'h',
	d: 'd',
	By: 'bytes',
	KiBy: 'kbytes',
	MiBy: 'mbytes',
	GiBy: 'gbytes',
	TiBy: 'tbytes',
	'By/s': 'Bps',
	'%': 'percent',
	Hz: 'hertz',
	Cel: 'celsius',
	V: 'volt',
	A: 'amp',
	W: 'watt',
	J: 'joule',
	m: 'lengthm'
};

// Same suffix table as Flare.Api's PromNames, so a name built here is one the Prometheus API resolves.
const PROM_UNIT_SUFFIXES: Record<string, string> = {
	s: 'seconds',
	ms: 'milliseconds',
	us: 'microseconds',
	ns: 'nanoseconds',
	min: 'minutes',
	h: 'hours',
	d: 'days',
	By: 'bytes',
	KiBy: 'kibibytes',
	MiBy: 'mebibytes',
	GiBy: 'gibibytes',
	TiBy: 'tebibytes',
	'By/s': 'bytes_per_second',
	m: 'meters',
	V: 'volts',
	A: 'amperes',
	J: 'joules',
	W: 'watts',
	Hz: 'hertz',
	Cel: 'celsius',
	'%': 'percent'
};

/** The Prometheus name(s) Flare serves an OTel metric under - mirrors `PromNames.BaseName`/`PromNamesFor`. */
function promBaseName(otelName: string, unit: string | null | undefined, type: MetricPointType): string {
	const name = labelName(otelName);
	if (!unit) return name;
	const suffix = unit === '1' ? (type === 'Gauge' ? 'ratio' : null) : PROM_UNIT_SUFFIXES[unit];
	return !suffix || name.endsWith(`_${suffix}`) ? name : `${name}_${suffix}`;
}

// ---- matchers --------------------------------------------------------------

/** A dashboard variable as the translated queries see it: the Grafana name plus what it narrows. */
interface VariableBinding {
	variable: DashboardVariable;
	name: string;
}

interface Narrowing {
	services: string[];
	/** Dashboard variables (by Grafana name) whose selection narrows the service. */
	serviceVariables: string[];
	attributeVariables: Array<{ bag: string; key: string; name: string }>;
}

function applicableVariables(panel: DashboardPanel, bindings: VariableBinding[]): VariableBinding[] {
	const excluded = new Set(panel.excludedVariableIds ?? []);
	return bindings.filter((b) => !excluded.has(b.variable.id));
}

function narrowingFor(panel: DashboardPanel, bindings: VariableBinding[], services: string[]): Narrowing {
	const out: Narrowing = { services, serviceVariables: [], attributeVariables: [] };
	for (const { variable, name } of applicableVariables(panel, bindings)) {
		if (variable.target === 'Service') out.serviceVariables.push(name);
		else if (variable.attributeBag && variable.attributeKey?.trim()) {
			out.attributeVariables.push({ bag: variable.attributeBag, key: variable.attributeKey.trim(), name });
		}
	}
	return out;
}

/** `service_name` matchers for the panel's own services plus every service variable. */
function serviceMatchers(n: Narrowing): string[] {
	const out: string[] = [];
	if (n.services.length === 1) out.push(`service_name=${quote(n.services[0])}`);
	else if (n.services.length > 1) out.push(`service_name=~${quote(alternation(n.services))}`);
	for (const v of n.serviceVariables) out.push(`service_name=~"$${v}"`);
	return out;
}

/** Label matcher text for a Flare attribute operator, or `null` when it needs a pipeline stage instead. */
function labelMatcher(label: string, operator: string, value: string, values: readonly string[]): string | null {
	switch (operator) {
		case 'Equals':
			return `${label}=${quote(value)}`;
		case 'NotEquals':
			return `${label}!=${quote(value)}`;
		case 'Exists':
			return `${label}=~".+"`;
		case 'Absent':
			return `${label}=""`;
		case 'Regex':
			return `${label}=~${quote(value)}`;
		case 'NotRegex':
			return `${label}!~${quote(value)}`;
		case 'In':
			return `${label}=~${quote(alternation(values))}`;
		case 'NotIn':
			return `${label}!~${quote(alternation(values))}`;
		default:
			return null;
	}
}

const NUMERIC_OPERATORS: Record<string, string> = { GreaterThan: '>', GreaterThanOrEqual: '>=', LessThan: '<', LessThanOrEqual: '<=' };

// ---- Prometheus (Metrics panels) -------------------------------------------

type Reducer = 'last' | 'avg' | 'sum' | 'min' | 'max';
const REDUCER_CALCS: Record<Reducer, string> = { last: 'lastNotNull', avg: 'mean', sum: 'sum', min: 'min', max: 'max' };

interface MetricRef {
	metricName: string;
	type: MetricPointType;
}

function isHistogram(type: MetricPointType): boolean {
	return type === 'Histogram' || type === 'ExponentialHistogram';
}

/** What one Flare metric query becomes in PromQL, plus how it should be drawn. */
interface PromSeriesQuery {
	/** One or more targets - a histogram's percentiles are three. */
	targets: Array<{ expr: string; legendFormat: string; format?: string }>;
	unit?: string;
}

function lookupMetric(catalog: readonly MetricNameInfo[], metric: MetricRef): MetricNameInfo | undefined {
	return catalog.find((m) => m.metricName === metric.metricName && m.type === metric.type) ?? catalog.find((m) => m.metricName === metric.metricName);
}

/** What a panel's translation dropped (`add`) or only approximated (`approximate`) - both end up in its Grafana description, only the first counts the panel as partly exported. */
class PanelNotes {
	readonly lines: string[] = [];
	readonly approximations: string[] = [];
	add(line: string): void {
		if (!this.lines.includes(line)) this.lines.push(line);
	}
	approximate(line: string): void {
		if (!this.approximations.includes(line)) this.approximations.push(line);
	}
}

interface MetricExprOptions {
	metric: MetricRef;
	catalog: readonly MetricNameInfo[];
	matchers: string[];
	groupBy: string | null;
	/** `mean` is the one number per bucket a histogram has outside its percentile lines. */
	histogram: 'percentiles' | 'mean' | 'buckets';
	topN: number | null;
}

function metricExpressions(o: MetricExprOptions): PromSeriesQuery {
	const info = lookupMetric(o.catalog, o.metric);
	const unit = info?.unit ?? undefined;
	const base = promBaseName(o.metric.metricName, unit, o.metric.type);
	const sel = (name: string) => `${name}{${o.matchers.join(',')}}`;
	const groupLabels = ['service_name', ...(o.groupBy ? [labelName(o.groupBy)] : [])];
	const by = (extra: string[] = []) => `by (${[...extra, ...groupLabels].join(', ')})`;
	const legend = o.groupBy ? `{{service_name}} {{${labelName(o.groupBy)}}}` : '{{service_name}}';
	const cap = (expr: string) => (o.groupBy && o.topN ? `topk(${o.topN}, ${expr})` : expr);

	if (o.metric.type === 'Gauge') {
		return { targets: [{ expr: cap(`avg ${by()} (${sel(base)})`), legendFormat: legend }], unit };
	}
	if (o.metric.type === 'Sum') {
		const name = base.endsWith('_total') ? base : `${base}_total`;
		return { targets: [{ expr: cap(`sum ${by()} (increase(${sel(name)}[$__interval]))`), legendFormat: legend }], unit };
	}
	// Histogram / ExponentialHistogram.
	if (o.histogram === 'buckets') {
		return { targets: [{ expr: `sum by (le) (increase(${sel(`${base}_bucket`)}[$__interval]))`, legendFormat: '{{le}}', format: 'heatmap' }], unit };
	}
	if (o.histogram === 'mean') {
		const sum = `sum ${by()} (increase(${sel(`${base}_sum`)}[$__interval]))`;
		const count = `sum ${by()} (increase(${sel(`${base}_count`)}[$__interval]))`;
		return { targets: [{ expr: cap(`${sum} / ${count}`), legendFormat: legend }], unit };
	}
	const quantile = (q: number) => `histogram_quantile(${q}, sum ${by(['le'])} (rate(${sel(`${base}_bucket`)}[$__rate_interval])))`;
	const quantiles = o.groupBy ? [0.95] : [0.5, 0.95, 0.99];
	return {
		targets: quantiles.map((q) => ({ expr: cap(quantile(q)), legendFormat: `${legend} p${Math.round(q * 100)}` })),
		unit
	};
}

const FORMULA_FUNCTIONS: Record<string, string> = { exp: 'exp', log: 'ln', sqrt: 'sqrt', abs: 'abs', ceil: 'ceil', floor: 'floor' };

function formulaToPromQl(node: FormulaNode, operands: Record<string, string>, notes: PanelNotes): string {
	switch (node.kind) {
		case 'num':
			return String(node.value);
		case 'ref':
			return `(${operands[node.letter] ?? '0'})`;
		case 'unary':
			return `-${formulaToPromQl(node.operand, operands, notes)}`;
		case 'binary':
			return `(${formulaToPromQl(node.left, operands, notes)} ${node.op} ${formulaToPromQl(node.right, operands, notes)})`;
		case 'call': {
			const arg = formulaToPromQl(node.arg, operands, notes);
			const fn = FORMULA_FUNCTIONS[node.fn];
			if (fn) return `${fn}(${arg})`;
			// runningDiff has no PromQL form: a running difference of consecutive points is not a window function.
			notes.add(`the ${node.fn}() function`);
			return arg;
		}
	}
}

/**
 * Grafana thresholds are ascending "from this value up" steps; Flare's are ordered rules with
 * comparison operators (first match wins). Probing the colour Flare would pick inside each interval
 * between the rule values gives the equivalent step list, whatever mix of `>` and `<` the rules use.
 */
function thresholdSteps(thresholds: readonly PanelThreshold[]): Array<{ value: number | null; color: string }> {
	const bounds = [...new Set(thresholds.map((t) => t.value))].sort((a, b) => a - b);
	const colorAt = (v: number) => {
		const hit = matchThreshold(thresholds, v);
		return hit ? THRESHOLD_COLORS[hit.color] : 'transparent';
	};
	const steps: Array<{ value: number | null; color: string }> = [];
	const starts: Array<number | null> = [null, ...bounds];
	starts.forEach((start, i) => {
		const next = starts[i + 1];
		const probe = start == null ? bounds[0] - 1 : next == null ? start + 1 : (start + next) / 2;
		const color = colorAt(probe);
		if (steps.length === 0 || steps[steps.length - 1].color !== color) steps.push({ value: start, color });
	});
	return steps;
}

// ---- Loki (Logs panels) ----------------------------------------------------

function severityLevels(numbers: readonly number[]): string[] {
	const names = ['trace', 'debug', 'info', 'warn', 'error', 'fatal'];
	const levels = new Set<string>();
	for (const n of numbers) levels.add(n >= 1 && n <= 24 ? names[Math.floor((n - 1) / 4)] : 'unknown');
	return names.concat('unknown').filter((l) => levels.has(l));
}

function bodyJsonStage(filter: BodyJsonFilter): string | null {
	const label = labelName(filter.path.replace(/\./g, '_'));
	const op = filter.operator ?? 'Equals';
	const matcher = labelMatcher(label, op, filter.value, filter.values ?? []);
	return matcher;
}

function logSelector(query: LogsSavedViewState, n: Narrowing, notes: PanelNotes): string {
	const matchers = serviceMatchers(n);
	const pipeline: string[] = [];

	if (query.scopeNames?.length) {
		const exact = query.scopeNames.filter((s) => !s.endsWith('*'));
		const prefixes = query.scopeNames.filter((s) => s.endsWith('*')).map((s) => s.slice(0, -1));
		const parts = [...exact.map(regexEscape), ...prefixes.map((p) => `${regexEscape(p)}.*`)];
		matchers.push(`scope_name=~${quote(parts.join('|'))}`);
	}
	if (query.severityNumbers?.length) {
		const levels = severityLevels(query.severityNumbers);
		matchers.push(levels.length === 1 ? `detected_level=${quote(levels[0])}` : `detected_level=~${quote(levels.join('|'))}`);
		// Flare filters on exact SeverityNumbers; Loki's detected_level is the whole bucket.
		const wholeLevels = levels.every((l) => {
			const first = ['trace', 'debug', 'info', 'warn', 'error', 'fatal'].indexOf(l) * 4 + 1;
			return l === 'unknown' ? query.severityNumbers.includes(0) : [0, 1, 2, 3].every((i) => query.severityNumbers.includes(first + i));
		});
		if (!wholeLevels) notes.approximate('a severity filter that picks part of a level matches the whole level');
	}
	if (query.traceId) matchers.push(`trace_id=${quote(query.traceId)}`);

	for (const f of query.attributeFilters ?? ([] as AttributeFilter[])) {
		const label = labelName(f.key);
		const op = f.operator ?? 'Equals';
		const m = labelMatcher(label, op, f.value ?? '', (f.values ?? []).filter((v): v is string => v != null));
		if (m) matchers.push(m);
		else if (NUMERIC_OPERATORS[op]) pipeline.push(`| ${label} ${NUMERIC_OPERATORS[op]} ${Number(f.value) || 0}`);
		else notes.add(`the ${op} attribute filter on ${f.key}`);
	}
	for (const v of n.attributeVariables) {
		if (v.bag === 'Span') continue;
		matchers.push(`${labelName(v.key)}=~"$${v.name}"`);
	}

	const stages: string[] = [];
	if (query.search?.trim()) {
		// Flare's search is a case-insensitive substring match on the message.
		stages.push(`|~ ${quote(`(?i)${regexEscape(query.search.trim())}`)}`);
		if (query.searchAllFields) notes.add('"search all fields" (only the message is searched)');
	}
	const jsonFilters = (query.bodyJsonFilters ?? []).map(bodyJsonStage);
	const usable = jsonFilters.filter((s): s is string => s != null);
	if (usable.length) stages.push('| json', ...usable.map((s) => `| ${s}`));
	if (usable.length < jsonFilters.length) notes.add('JSON body filters using has / not has');
	stages.push(...pipeline);

	// A LogQL selector needs at least one matcher that cannot match the empty string.
	if (!matchers.some((m) => m.startsWith('service_name'))) matchers.unshift('service_name=~".+"');
	return `{${matchers.join(', ')}}${stages.length ? ` ${stages.join(' ')}` : ''}`;
}

// ---- Tempo (Traces panels) -------------------------------------------------

const SPAN_KINDS = ['unspecified', 'internal', 'server', 'client', 'producer', 'consumer'];

function traceQl(query: TracesSavedViewState, n: Narrowing, notes: PanelNotes): string {
	const conds: string[] = [];
	if (n.services.length === 1) conds.push(`resource.service.name = ${quote(n.services[0])}`);
	else if (n.services.length > 1) conds.push(`resource.service.name =~ ${quote(alternation(n.services))}`);
	for (const v of n.serviceVariables) conds.push(`resource.service.name =~ "$${v}"`);

	const statuses = (query.statusCodes ?? []).map((s) => s.replace('STATUS_CODE_', '').toLowerCase());
	if (statuses.length === 1) conds.push(`status = ${statuses[0]}`);
	else if (statuses.length > 1) conds.push(`(${statuses.map((s) => `status = ${s}`).join(' || ')})`);

	const kinds = (query.kinds ?? []).map((k) => SPAN_KINDS[k]).filter(Boolean);
	if (kinds.length === 1) conds.push(`kind = ${kinds[0]}`);
	else if (kinds.length > 1) conds.push(`(${kinds.map((k) => `kind = ${k}`).join(' || ')})`);

	if (query.names?.length === 1) conds.push(`name = ${quote(query.names[0])}`);
	else if (query.names && query.names.length > 1) conds.push(`name =~ ${quote(alternation(query.names))}`);

	const scoped = (bag: string, key: string): string | null => (bag === 'Span' ? `span.${key}` : bag === 'Resource' ? `resource.${key}` : null);
	for (const f of query.attributeFilters ?? ([] as SpanAttributeFilter[])) {
		const field = scoped(f.bag, f.key);
		if (!field) {
			notes.add(`the scope attribute filter on ${f.key}`);
			continue;
		}
		const op = f.operator ?? 'Equals';
		const values = (f.values ?? []).filter((v): v is string => v != null);
		switch (op) {
			case 'Equals': conds.push(`${field} = ${quote(f.value)}`); break;
			case 'NotEquals': conds.push(`${field} != ${quote(f.value)}`); break;
			case 'Exists': conds.push(`${field} != nil`); break;
			case 'Absent': conds.push(`${field} = nil`); break;
			case 'Regex': conds.push(`${field} =~ ${quote(f.value)}`); break;
			case 'NotRegex': conds.push(`${field} !~ ${quote(f.value)}`); break;
			case 'In': conds.push(`${field} =~ ${quote(alternation(values))}`); break;
			case 'NotIn': conds.push(`${field} !~ ${quote(alternation(values))}`); break;
			default:
				if (NUMERIC_OPERATORS[op]) conds.push(`${field} ${NUMERIC_OPERATORS[op]} ${Number(f.value) || 0}`);
		}
	}
	for (const v of n.attributeVariables) {
		const field = scoped(v.bag, v.key);
		if (field) conds.push(`${field} =~ "$${v.name}"`);
	}

	if (query.structure) notes.add('structural trace queries');
	if (query.durationBucketNano != null) notes.add('the duration bucket filter');
	if (query.entrySpansOnly) notes.add('"entry spans only"');
	if (query.nPlusOneOnly) notes.add('the N+1 filter');
	return `{ ${conds.length ? conds.join(' && ') : 'true'} }`;
}

// ---- panels ----------------------------------------------------------------

interface Context {
	catalog: readonly MetricNameInfo[];
	bindings: VariableBinding[];
	used: Set<SourceKind>;
	nextId: number;
}

function ds(ctx: Context, kind: SourceKind): Json {
	ctx.used.add(kind);
	return { type: kind, uid: `\${${SOURCES[kind].input}}` };
}

function gridPos(layout: DashboardPanel['layout'], yOffset: number): Json {
	const scale = GRAFANA_GRID_COLUMNS / GRID_COLUMNS;
	const w = Math.min(GRAFANA_GRID_COLUMNS, Math.max(1, Math.round(layout.w * scale)));
	const x = Math.min(GRAFANA_GRID_COLUMNS - w, Math.max(0, Math.round(layout.x * scale)));
	return { x, y: yOffset + Math.max(0, Math.round(layout.y * scale)), w, h: Math.max(2, Math.round(layout.h * scale)) };
}

function legendOptions(panel: DashboardPanel, fallback: 'bottom' | 'right'): Json {
	const position = panel.legendPosition ?? fallback;
	return position === 'hidden'
		? { showLegend: false, displayMode: 'list', placement: 'bottom', calcs: [] }
		: { showLegend: true, displayMode: 'list', placement: position, calcs: [] };
}

/** `{{service}}` and attribute keys in a Flare legend template become Prometheus label references. */
function convertLegendFormat(template: string): string {
	return template.replace(/\{\{\s*([^{}]+?)\s*\}\}/g, (_, key: string) => `{{${key === 'service' ? 'service_name' : labelName(key)}}}`);
}

function metricsPanel(panel: DashboardPanel, ctx: Context, notes: PanelNotes): Json {
	const q = panel.query as MetricsSavedViewState;
	const n = narrowingFor(panel, ctx.bindings, q.services ?? []);
	const matchers = serviceMatchers(n);
	// `stackedBar` is the pre-`stacking` spelling of bar + normal stacking (see parseStacking in visualization.ts).
	const rawViz = panel.visualization as PanelVisualization | 'stackedBar' | undefined;
	const legacyStackedBar = rawViz === 'stackedBar';
	const viz: PanelVisualization = rawViz === 'stackedBar' ? 'bar' : (rawViz ?? 'timeSeries');
	const reducer = (panel.reducer as Reducer | undefined) ?? 'last';
	const topN = typeof q.topN === 'number' ? q.topN : null;

	if (q.havingOperator) notes.add('the "having" filter');
	if (q.compareEnabled) notes.add('the previous-period comparison');
	if (q.timeShiftSeconds) notes.add('the time-shift overlay');
	if (q.postProcessFunctions?.length) notes.add('post-processing functions');

	let targets: PromSeriesQuery['targets'] = [];
	// A formula's result unit is whatever its arithmetic makes of its operands' units, so none is set for it.
	let unit: string | undefined;
	const histogramMode = viz === 'heatmap' || viz === 'histogram' ? 'buckets' : viz === 'timeSeries' ? 'percentiles' : 'mean';

	if (q.mode === 'formula' && q.formulaExpression && q.formulaQueries?.length) {
		const parsed = parseFormula(q.formulaExpression);
		const operands: Record<string, string> = {};
		for (const fq of q.formulaQueries) {
			if (!fq.metric) continue;
			const out = metricExpressions({
				metric: fq.metric,
				catalog: ctx.catalog,
				matchers,
				groupBy: fq.groupByAttributeKey,
				histogram: 'mean',
				topN: null
			});
			operands[fq.letter] = out.targets[0].expr;
		}
		if (parsed.ok) {
			const groupBy = q.formulaQueries[0]?.groupByAttributeKey;
			targets = [{ expr: formulaToPromQl(parsed.node, operands, notes), legendFormat: groupBy ? `{{service_name}} {{${labelName(groupBy)}}}` : '{{service_name}}' }];
		} else {
			notes.add('the formula (it did not parse)');
		}
	} else if (q.selectedMetric) {
		const out = metricExpressions({
			metric: q.selectedMetric,
			catalog: ctx.catalog,
			matchers,
			groupBy: q.groupByAttributeKey,
			histogram: histogramMode,
			topN
		});
		targets = out.targets;
		unit = out.unit;
	} else {
		notes.add('no metric was selected');
	}

	if (panel.legendFormat) targets = targets.map((t) => (t.format === 'heatmap' ? t : { ...t, legendFormat: convertLegendFormat(panel.legendFormat!) }));

	const defaults: Json = {};
	const grafanaUnit = unit ? GRAFANA_UNITS[unit] : undefined;
	if (grafanaUnit) defaults.unit = grafanaUnit;
	if (typeof panel.decimals === 'number') defaults.decimals = panel.decimals;

	const custom: Json = {};
	const isChart = viz === 'timeSeries' || viz === 'bar';
	if (isChart) {
		if (typeof panel.yAxisMin === 'number') custom.axisSoftMin = panel.yAxisMin;
		if (typeof panel.yAxisMax === 'number') custom.axisSoftMax = panel.yAxisMax;
		if (panel.yAxisScale === 'log') custom.scaleDistribution = { type: 'log', log: 10 };
		const stacking = panel.stacking ?? (legacyStackedBar ? 'normal' : 'none');
		if (stacking !== 'none') custom.stacking = { mode: stacking, group: 'A' };
		if (viz === 'bar') {
			custom.drawStyle = 'bars';
			custom.fillOpacity = 80;
			custom.lineWidth = 1;
		} else if (stacking !== 'none') {
			custom.fillOpacity = 40;
		}
	}
	if (panel.thresholds?.length) {
		defaults.thresholds = { mode: 'absolute', steps: thresholdSteps(panel.thresholds) };
		if (isChart) custom.thresholdsStyle = { mode: 'line+area' };
		else defaults.color = { mode: 'thresholds' };
	}
	if (Object.keys(custom).length) defaults.custom = custom;

	const grafanaTargets = targets.map((t, i) => ({
		refId: String.fromCharCode(65 + i),
		datasource: ds(ctx, 'prometheus'),
		expr: t.expr,
		legendFormat: t.legendFormat,
		range: true,
		instant: false,
		...(t.format ? { format: t.format } : {})
	}));

	const base: Json = { datasource: ds(ctx, 'prometheus'), targets: grafanaTargets, fieldConfig: { defaults, overrides: [] } };
	const calc = REDUCER_CALCS[reducer] ?? 'lastNotNull';

	switch (viz) {
		case 'value':
			return { ...base, type: 'stat', options: { reduceOptions: { calcs: [calc], fields: '', values: false }, colorMode: 'value', graphMode: 'area', textMode: 'auto' } };
		case 'pie':
			return {
				...base,
				type: 'piechart',
				options: { reduceOptions: { calcs: [calc], fields: '', values: false }, pieType: 'pie', legend: legendOptions(panel, 'right'), displayLabels: [] }
			};
		case 'table':
			return {
				...base,
				type: 'table',
				transformations: [{ id: 'reduce', options: { reducers: [calc], mode: 'seriesToRows' } }],
				options: { showHeader: true }
			};
		case 'heatmap':
		case 'histogram':
			return {
				...base,
				type: 'heatmap',
				options: { calculate: false, yAxis: { axisPlacement: 'left' }, cellGap: 1, color: { scheme: 'Spectral', mode: 'scheme' }, legend: { show: true } }
			};
		default:
			return {
				...base,
				type: 'timeseries',
				options: { legend: legendOptions(panel, 'bottom'), tooltip: { mode: 'multi', sort: 'desc' } }
			};
	}
}

function logsPanel(panel: DashboardPanel, ctx: Context, notes: PanelNotes): Json {
	const q = panel.query as LogsSavedViewState;
	const n = narrowingFor(panel, ctx.bindings, q.services ?? []);
	const selector = logSelector(q, n, notes);
	if (q.postProcessFunctions?.length) notes.add('post-processing functions');
	if (q.timeShiftSeconds) notes.add('the time-shift overlay');

	// A Flare Logs panel is a volume chart, stacked by severity unless it groups by an attribute.
	const groupBy = q.volumeGroupBy && q.volumeGroupBy.bag !== 'BodyJson' ? labelName(q.volumeGroupBy.key) : q.volumeGroupBy ? null : 'detected_level';
	if (q.volumeGroupBy?.bag === 'BodyJson') notes.add('grouping by a JSON body field');
	const expr = groupBy ? `sum by (${groupBy}) (count_over_time(${selector} [$__interval]))` : `sum(count_over_time(${selector} [$__interval]))`;
	return {
		type: 'timeseries',
		datasource: ds(ctx, 'loki'),
		targets: [{ refId: 'A', datasource: ds(ctx, 'loki'), expr, legendFormat: groupBy ? `{{${groupBy}}}` : 'logs', queryType: 'range' }],
		fieldConfig: {
			defaults: {
				custom: { drawStyle: 'bars', fillOpacity: 80, lineWidth: 1, stacking: { mode: 'normal', group: 'A' } }
			},
			overrides: groupBy === 'detected_level' ? levelColorOverrides() : []
		},
		options: { legend: { showLegend: true, displayMode: 'list', placement: 'bottom', calcs: [] }, tooltip: { mode: 'multi', sort: 'desc' } }
	};
}

function levelColorOverrides(): Json[] {
	const colors: Record<string, string> = { trace: 'text', debug: 'blue', info: 'green', warn: 'yellow', error: 'red', fatal: 'dark-red', unknown: 'gray' };
	return Object.entries(colors).map(([level, color]) => ({
		matcher: { id: 'byName', options: level },
		properties: [{ id: 'color', value: { mode: 'fixed', fixedColor: color } }]
	}));
}

function tracesPanel(panel: DashboardPanel, ctx: Context, notes: PanelNotes): Json {
	const q = panel.query as TracesSavedViewState;
	const n = narrowingFor(panel, ctx.bindings, q.services ?? []);
	const query = traceQl(q, n, notes);
	return {
		type: 'table',
		datasource: ds(ctx, 'tempo'),
		targets: [{ refId: 'A', datasource: ds(ctx, 'tempo'), queryType: 'traceql', query, limit: 50, tableType: 'traces', filters: [] }],
		fieldConfig: { defaults: {}, overrides: [] },
		options: { showHeader: true }
	};
}

function textPanel(panel: DashboardPanel): Json {
	const markdown = (panel.query as { markdown?: unknown } | null)?.markdown;
	return { type: 'text', options: { mode: 'markdown', content: typeof markdown === 'string' ? markdown : '' } };
}

function buildPanel(panel: DashboardPanel, ctx: Context, yOffset: number): { json: Json; partial: boolean } {
	const notes = new PanelNotes();
	let body: Json;
	switch (panel.panelType) {
		case 'Metrics':
			body = metricsPanel(panel, ctx, notes);
			break;
		case 'Logs':
			body = logsPanel(panel, ctx, notes);
			break;
		case 'Traces':
			body = tracesPanel(panel, ctx, notes);
			break;
		default:
			body = textPanel(panel);
	}

	const description = [
		panel.description?.trim(),
		notes.lines.length ? `Flare export: left out ${notes.lines.join('; ')}.` : null,
		notes.approximations.length ? `Flare export: approximated - ${notes.approximations.join('; ')}.` : null
	]
		.filter(Boolean)
		.join('\n\n');
	const json: Json = {
		id: ctx.nextId++,
		title: panel.title,
		...(description ? { description } : {}),
		gridPos: gridPos(panel.layout, yOffset),
		...body
	};

	if (panel.repeatVariableId) {
		const binding = ctx.bindings.find((b) => b.variable.id === panel.repeatVariableId);
		if (binding && !(panel.excludedVariableIds ?? []).includes(binding.variable.id)) {
			json.repeat = binding.name;
			json.repeatDirection = panel.repeatDirection === 'vertical' ? 'v' : 'h';
			if (panel.repeatDirection !== 'vertical') json.maxPerRow = 4;
		}
	}
	return { json, partial: notes.lines.length > 0 };
}

// ---- variables -------------------------------------------------------------

function buildVariables(variables: readonly DashboardVariable[], ctx: Context, hasLogs: boolean): Json[] {
	const taken = new Set<string>();
	const out: Json[] = [];
	for (const variable of variables) {
		const name = grafanaVariableName(variable.name, taken);
		ctx.bindings.push({ variable, name });
	}
	for (const { variable, name } of ctx.bindings) {
		const selected = variable.multi ? (variable.defaultValues ?? []) : variable.defaultValue ? [variable.defaultValue] : [];
		const current = selected.length ? { selected: true, text: selected, value: selected } : { selected: true, text: ['All'], value: ['$__all'] };
		const common: Json = {
			name,
			label: variable.name,
			description: variable.description ?? '',
			multi: !!variable.multi && variable.sourceKind !== 'Textbox',
			includeAll: true,
			allValue: '.+',
			current,
			hide: 0,
			skipUrlSync: false
		};
		if (variable.valueRegex) common.regex = variable.valueRegex;

		const label = variable.target === 'Service' ? 'service_name' : labelName(variable.attributeKey ?? '');
		// A Span-bag attribute has no Loki/Prometheus label list to read, so it falls back to typed values.
		const queryable = variable.sourceKind === 'Query' && !(variable.target === 'Attribute' && variable.attributeBag === 'Span');
		if (queryable) {
			const kind: SourceKind = variable.target === 'Service' && !hasLogs ? 'prometheus' : 'loki';
			const parent = variable.dependsOnVariableId ? ctx.bindings.find((b) => b.variable.id === variable.dependsOnVariableId) : undefined;
			const parentMatcher = parent
				? parent.variable.target === 'Service'
					? `service_name=~"$${parent.name}"`
					: `${labelName(parent.variable.attributeKey ?? '')}=~"$${parent.name}"`
				: null;
			const query = parentMatcher ? `label_values({${parentMatcher}}, ${label})` : `label_values(${label})`;
			out.push({
				...common,
				type: 'query',
				datasource: ds(ctx, kind),
				query: kind === 'loki' ? { label, refId: 'LokiVariableQueryEditor-VariableQuery', stream: parentMatcher ? `{${parentMatcher}}` : '', type: 1 } : query,
				definition: query,
				refresh: 2,
				sort: 1
			});
		} else if (variable.sourceKind === 'Custom') {
			const values = variable.customValues ?? [];
			out.push({
				...common,
				type: 'custom',
				query: values.join(','),
				options: [{ selected: !selected.length, text: 'All', value: '$__all' }, ...values.map((v) => ({ selected: selected.includes(v), text: v, value: v }))]
			});
		} else {
			// Textbox, or a Span attribute: Grafana's custom variable lets the viewer type a value and still offers "All".
			out.push({ ...common, type: 'custom', query: '', options: [{ selected: !selected.length, text: 'All', value: '$__all' }], allowCustomValue: true });
		}
	}
	return out;
}

// ---- dashboard -------------------------------------------------------------

function grafanaTimeFrom(preset: string | undefined): string {
	const match = /^(\d+)([mhd])$/.exec(preset ?? '');
	if (match) return `now-${match[1]}${match[2]}`;
	if (preset === 'today') return 'now/d';
	if (preset === 'thisWeek') return 'now/w';
	return 'now-1h';
}

function panelRange(panel: DashboardPanel): string | undefined {
	const preset = (panel.query as { timeRangePreset?: unknown } | null)?.timeRangePreset;
	return typeof preset === 'string' ? preset : undefined;
}

export function buildGrafanaDashboard(
	dashboard: { name: string; description?: string; tags?: string[]; layout: DashboardLayout },
	catalog: readonly MetricNameInfo[]
): GrafanaExportResult {
	const { layout } = dashboard;
	const ctx: Context = { catalog, bindings: [], used: new Set(), nextId: 1 };
	const hasLogs = layout.panels.some((p) => p.panelType === 'Logs');
	const templating = buildVariables(layout.variables ?? [], ctx, hasLogs);

	const rows: DashboardRow[] = layout.rows ?? [];
	const rowIds = new Set(rows.map((r) => r.id));
	const sectionOf = (p: DashboardPanel) => (p.rowId && rowIds.has(p.rowId) ? p.rowId : null);
	const extent = (panels: DashboardPanel[]) => panels.reduce((max, p) => Math.max(max, gridPos(p.layout, 0).y + gridPos(p.layout, 0).h), 0);

	const panels: Json[] = [];
	let partialCount = 0;
	let panelCount = 0;
	let y = 0;

	const place = (members: DashboardPanel[], offset: number): Json[] =>
		members.map((p) => {
			const built = buildPanel(p, ctx, offset);
			panelCount++;
			if (built.partial) partialCount++;
			return built.json;
		});

	const ungrouped = layout.panels.filter((p) => sectionOf(p) === null);
	panels.push(...place(ungrouped, y));
	y += extent(ungrouped);

	for (const row of rows) {
		const members = layout.panels.filter((p) => sectionOf(p) === row.id);
		const rowY = y;
		const children = place(members, rowY + 1);
		const rowPanel: Json = { id: ctx.nextId++, type: 'row', title: row.title, collapsed: !!row.collapsed, gridPos: { x: 0, y: rowY, w: GRAFANA_GRID_COLUMNS, h: 1 }, panels: [] };
		if (row.collapsed) {
			rowPanel.panels = children;
			panels.push(rowPanel);
			y = rowY + 1;
		} else {
			panels.push(rowPanel, ...children);
			y = rowY + 1 + extent(members);
		}
	}

	const range = layout.defaultTimeRange ?? panelRange(layout.panels.find((p) => p.panelType !== 'Text') ?? layout.panels[0]);
	const inputs = [...ctx.used].map((kind) => ({
		name: SOURCES[kind].input,
		label: SOURCES[kind].label,
		description: 'Point this at Flare',
		type: 'datasource',
		pluginId: kind,
		pluginName: SOURCES[kind].pluginName
	}));

	// Flare's alert incidents and marked deploys are Prometheus-API metrics (ADR-0252), so any dashboard
	// that already reads Flare through Prometheus gets them as annotations.
	const annotations: Json[] = [
		{ builtIn: 1, datasource: { type: 'grafana', uid: '-- Grafana --' }, enable: true, hide: true, iconColor: 'rgba(0, 211, 255, 1)', name: 'Annotations & Alerts', type: 'dashboard' }
	];
	if (ctx.used.has('prometheus')) {
		const prom = { type: 'prometheus', uid: `\${${SOURCES.prometheus.input}}` };
		annotations.push(
			{ datasource: prom, enable: true, expr: 'flare_alert_firing', iconColor: 'red', name: 'Flare alerts', step: '1m', tagKeys: 'alertname', textFormat: 'Alert firing', titleFormat: '{{alertname}}' },
			{ datasource: prom, enable: true, expr: 'flare_release_deployed', iconColor: 'blue', name: 'Flare deploys', step: '1m', tagKeys: 'service_name', textFormat: '{{commit}} {{url}}', titleFormat: 'Deploy {{service_version}}' }
		);
	}

	return {
		panelCount,
		partialCount,
		dashboard: {
			__inputs: inputs,
			__requires: [{ type: 'grafana', id: 'grafana', name: 'Grafana', version: '10.0.0' }, ...inputs.map((i) => ({ type: 'datasource', id: i.pluginId, name: i.pluginName, version: '1.0.0' }))],
			annotations: { list: annotations },
			description: dashboard.description ?? '',
			editable: true,
			fiscalYearStartMonth: 0,
			graphTooltip: 1,
			id: null,
			links: [],
			panels,
			refresh: '',
			schemaVersion: GRAFANA_SCHEMA_VERSION,
			tags: ['flare', ...(dashboard.tags ?? [])],
			templating: { list: templating },
			time: { from: grafanaTimeFrom(range), to: 'now' },
			timepicker: {},
			timezone: 'browser',
			title: dashboard.name,
			uid: null,
			version: 1
		}
	};
}
