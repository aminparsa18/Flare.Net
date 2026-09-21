// Cross-query formula expressions for Metrics Explorer's Formula mode (roadmap: "Cross-query
// formula expressions for metrics"). Pure, no ClickHouse/API dependency - the same "app-side
// post-processing over already-fetched rows" shape HistogramQuantileEstimator.cs uses on the
// backend, just on the dashboard side of the wire instead, per ADR-0036.
//
// Two independent pieces: parseFormula (expression -> AST, or a parse error) and
// evaluateFormula (AST + one MetricQueryResponse per referenced letter -> joined MetricSeries).
// State.svelte.ts owns fetching the per-letter MetricQueryResponses and wiring this together.

import type { MetricSeries, MetricSeriesPoint } from '$lib/metrics-api';

export type FormulaNode =
	| { kind: 'num'; value: number }
	| { kind: 'ref'; letter: string }
	| { kind: 'unary'; op: '-'; operand: FormulaNode }
	| { kind: 'binary'; op: '+' | '-' | '*' | '/'; left: FormulaNode; right: FormulaNode }
	| { kind: 'call'; fn: 'exp' | 'log' | 'sqrt'; arg: FormulaNode };

export type ParseResult = { ok: true; node: FormulaNode } | { ok: false; error: string };

const FUNCTIONS = new Set(['exp', 'log', 'sqrt']);

/** A single query-letter reference is exactly one uppercase ASCII letter (A-Z), same convention `FormulaQueryDef.letter` (state.svelte.ts) assigns them in. */
function isLetter(ch: string): boolean {
	return ch.length === 1 && ch >= 'A' && ch <= 'Z';
}

interface Token {
	type: 'num' | 'ref' | 'ident' | '+' | '-' | '*' | '/' | '(' | ')' | 'eof';
	value?: string;
}

function tokenize(expr: string): Token[] | { error: string } {
	const tokens: Token[] = [];
	let i = 0;
	while (i < expr.length) {
		const ch = expr[i];
		if (ch === ' ' || ch === '\t' || ch === '\n') {
			i++;
			continue;
		}
		if ('+-*/()'.includes(ch)) {
			tokens.push({ type: ch as Token['type'] });
			i++;
			continue;
		}
		if (ch >= '0' && ch <= '9') {
			let j = i + 1;
			while (j < expr.length && ((expr[j] >= '0' && expr[j] <= '9') || expr[j] === '.')) j++;
			const raw = expr.slice(i, j);
			if (!/^\d+(\.\d+)?$/.test(raw)) return { error: `Invalid number "${raw}".` };
			tokens.push({ type: 'num', value: raw });
			i = j;
			continue;
		}
		if (/[A-Za-z]/.test(ch)) {
			let j = i + 1;
			while (j < expr.length && /[A-Za-z0-9]/.test(expr[j])) j++;
			const raw = expr.slice(i, j);
			if (isLetter(raw) && raw.length === 1) {
				tokens.push({ type: 'ref', value: raw });
			} else {
				tokens.push({ type: 'ident', value: raw });
			}
			i = j;
			continue;
		}
		return { error: `Unexpected character "${ch}".` };
	}
	tokens.push({ type: 'eof' });
	return tokens;
}

/**
 * Recursive-descent parser, standard precedence: unary minus > `*`/`/` > `+`/`-`. A bare
 * identifier is a query-letter reference (`A`, `B`, ...) unless it's one of the reserved
 * function names (`exp`/`log`/`sqrt`), which must be followed by a single parenthesized
 * argument - the same three functions the roadmap item names, no others (no `^`/power - not
 * asked for, and it'd need a precedence tier of its own).
 */
export function parseFormula(expr: string): ParseResult {
	const trimmed = expr.trim();
	if (!trimmed) return { ok: false, error: 'Formula is empty.' };

	const tokenResult = tokenize(trimmed);
	if ('error' in tokenResult) return { ok: false, error: tokenResult.error };
	const tokens = tokenResult;
	let pos = 0;

	const peek = () => tokens[pos];
	const advance = () => tokens[pos++];

	function parseExpression(): FormulaNode | { error: string } {
		let left = parseTerm();
		if ('error' in left) return left;
		while (peek().type === '+' || peek().type === '-') {
			const op = advance().type as '+' | '-';
			const right = parseTerm();
			if ('error' in right) return right;
			left = { kind: 'binary', op, left, right };
		}
		return left;
	}

	function parseTerm(): FormulaNode | { error: string } {
		let left = parseUnary();
		if ('error' in left) return left;
		while (peek().type === '*' || peek().type === '/') {
			const op = advance().type as '*' | '/';
			const right = parseUnary();
			if ('error' in right) return right;
			left = { kind: 'binary', op, left, right };
		}
		return left;
	}

	function parseUnary(): FormulaNode | { error: string } {
		if (peek().type === '-') {
			advance();
			const operand = parseUnary();
			if ('error' in operand) return operand;
			return { kind: 'unary', op: '-', operand };
		}
		return parsePrimary();
	}

	function parsePrimary(): FormulaNode | { error: string } {
		const tok = peek();
		if (tok.type === 'num') {
			advance();
			return { kind: 'num', value: Number(tok.value) };
		}
		if (tok.type === 'ref') {
			advance();
			return { kind: 'ref', letter: tok.value! };
		}
		if (tok.type === 'ident') {
			advance();
			const fn = tok.value!.toLowerCase();
			if (!FUNCTIONS.has(fn)) {
				return { error: `Unknown function "${tok.value}" - supported: ${[...FUNCTIONS].join(', ')}.` };
			}
			if (peek().type !== '(') return { error: `Expected "(" after "${tok.value}".` };
			advance();
			const arg = parseExpression();
			if ('error' in arg) return arg;
			if (peek().type !== ')') return { error: `Expected ")" to close "${tok.value}(".` };
			advance();
			return { kind: 'call', fn: fn as 'exp' | 'log' | 'sqrt', arg };
		}
		if (tok.type === '(') {
			advance();
			const inner = parseExpression();
			if ('error' in inner) return inner;
			if (peek().type !== ')') return { error: 'Expected ")".' };
			advance();
			return inner;
		}
		return { error: tok.type === 'eof' ? 'Unexpected end of formula.' : `Unexpected token "${tok.value ?? tok.type}".` };
	}

	const result = parseExpression();
	if ('error' in result) return { ok: false, error: result.error };
	if (peek().type !== 'eof') return { ok: false, error: `Unexpected token "${peek().value ?? peek().type}".` };
	return { ok: true, node: result };
}

/** Every query-letter reference (`A`, `B`, ...) a parsed formula actually uses - a query row can be defined but unreferenced (e.g. kept around while iterating on the expression), so this, not the full defined-query list, is what decides which queries actually need fetching/joining. */
export function collectRefs(node: FormulaNode): Set<string> {
	const refs = new Set<string>();
	function visit(n: FormulaNode): void {
		switch (n.kind) {
			case 'ref':
				refs.add(n.letter);
				return;
			case 'num':
				return;
			case 'unary':
				visit(n.operand);
				return;
			case 'call':
				visit(n.arg);
				return;
			case 'binary':
				visit(n.left);
				visit(n.right);
				return;
		}
	}
	visit(node);
	return refs;
}

/** `null` propagates through every operator/function - a missing/invalid operand makes the whole point undefined rather than a silently wrong number (NaN/Infinity from e.g. `1/0` or `log(-1)` also collapses to `null` here, one honest "no value" outcome instead of a chart secretly plotting a non-finite y). */
function evaluateNode(node: FormulaNode, bindings: Record<string, number>): number | null {
	switch (node.kind) {
		case 'num':
			return node.value;
		case 'ref': {
			const v = bindings[node.letter];
			return v == null ? null : v;
		}
		case 'unary': {
			const v = evaluateNode(node.operand, bindings);
			return v == null ? null : -v;
		}
		case 'call': {
			const v = evaluateNode(node.arg, bindings);
			if (v == null) return null;
			const result = node.fn === 'exp' ? Math.exp(v) : node.fn === 'sqrt' ? Math.sqrt(v) : Math.log(v);
			return Number.isFinite(result) ? result : null;
		}
		case 'binary': {
			const l = evaluateNode(node.left, bindings);
			const r = evaluateNode(node.right, bindings);
			if (l == null || r == null) return null;
			const result = node.op === '+' ? l + r : node.op === '-' ? l - r : node.op === '*' ? l * r : l / r;
			return Number.isFinite(result) ? result : null;
		}
	}
}

/** One referenced letter's fetched series, keyed to the query row that produced it - `FormulaState`'s (state.svelte.ts) input to `evaluateFormula`. */
export interface FormulaSeriesInput {
	letter: string;
	series: MetricSeries[];
}

/** Cap on joined output series - defensive only. Each input query already goes through `MetricSeriesQueryBuilder`'s own TopN cap (default 20) server-side, and an inner join can never produce more series than its smallest input, so this is never expected to bind in practice. */
const MAX_OUTPUT_SERIES = 50;

/** `ServiceName` + the sorted `attributes` map, verbatim - the same (service, attribute-set) identity `MetricSeriesQueryBuilder`'s own remarks describe for a `MetricSeries`, reused here as the cross-query join key instead of a fresh one: two queries' series only "mean the same thing" at a given timestamp if they agree on both. */
function seriesJoinKey(series: MetricSeries): string {
	const sortedAttrs = Object.entries(series.attributes).sort(([a], [b]) => a.localeCompare(b));
	return `${series.serviceName}\u0000${JSON.stringify(sortedAttrs)}`;
}

interface JoinedInput {
	descriptor: { serviceName: string; attributes: Record<string, string> };
	byBucketMs: Map<number, number>;
}

function intersect<T>(sets: Set<T>[]): Set<T> {
	if (sets.length === 0) return new Set();
	return sets.reduce((acc, s) => new Set([...acc].filter((v) => s.has(v))));
}

export interface EvaluateFormulaResult {
	series: MetricSeries[];
	/** Non-fatal - e.g. a referenced letter has no matching query/series, or the join produced zero series (queries likely disagree on Group by / service, so nothing lines up per bucket). Callers show this as a hint, not an error banner. */
	warning: string | null;
}

/**
 * Joins each referenced letter's series by (ServiceName, attributes) - see `seriesJoinKey` -
 * intersecting first on that join key across every referenced letter, then on bucket
 * timestamp within each matched key, and evaluates the formula once per surviving (key,
 * bucket) pair. An inner join throughout, not a left join defaulting missing operands to
 * zero: a bucket only present in some of the referenced queries has no well-defined value for
 * the others, and defaulting to 0 would silently fabricate a data point (e.g. `A/B` spiking to
 * 0 the instant B has no data, instead of correctly having no point there at all).
 */
export function evaluateFormula(node: FormulaNode, inputs: FormulaSeriesInput[]): EvaluateFormulaResult {
	const refs = [...collectRefs(node)];
	if (refs.length === 0) {
		return { series: [], warning: 'Formula must reference at least one query (A, B, ...).' };
	}

	const byLetter = new Map(inputs.map((i) => [i.letter, i]));
	const missing = refs.filter((l) => !byLetter.has(l));
	if (missing.length > 0) {
		return { series: [], warning: `Formula references ${missing.join(', ')}, which ${missing.length === 1 ? 'has' : 'have'} no query defined.` };
	}

	const perLetter = new Map<string, Map<string, JoinedInput>>();
	for (const letter of refs) {
		const map = new Map<string, JoinedInput>();
		for (const series of byLetter.get(letter)!.series) {
			const key = seriesJoinKey(series);
			const byBucketMs = new Map<number, number>();
			for (const point of series.points) {
				if (point.value != null) byBucketMs.set(new Date(point.bucketStart).getTime(), point.value);
			}
			map.set(key, { descriptor: { serviceName: series.serviceName, attributes: series.attributes }, byBucketMs });
		}
		perLetter.set(letter, map);
	}

	const commonKeys = intersect(refs.map((l) => new Set(perLetter.get(l)!.keys())));

	const outSeries: MetricSeries[] = [];
	for (const key of commonKeys) {
		const perLetterForKey = refs.map((l) => perLetter.get(l)!.get(key)!);
		const commonBucketsMs = [...intersect(perLetterForKey.map((i) => new Set(i.byBucketMs.keys())))].sort((a, b) => a - b);

		const points: MetricSeriesPoint[] = [];
		for (const bucketMs of commonBucketsMs) {
			const bindings: Record<string, number> = {};
			for (let i = 0; i < refs.length; i++) bindings[refs[i]] = perLetterForKey[i].byBucketMs.get(bucketMs)!;
			const value = evaluateNode(node, bindings);
			if (value == null) continue;
			points.push({
				bucketStart: new Date(bucketMs).toISOString(),
				value,
				count: null,
				sum: null,
				p50: null,
				p75: null,
				p90: null,
				p95: null,
				p99: null,
				maxApprox: null
			});
		}

		if (points.length > 0) {
			outSeries.push({ ...perLetterForKey[0].descriptor, points });
		}
	}

	if (outSeries.length === 0) {
		return {
			series: [],
			warning:
				refs.length > 1
					? 'No matching data points across the referenced queries - they may need the same "Group by" attribute (or none) and overlapping services to join.'
					: null
		};
	}

	return { series: outSeries.slice(0, MAX_OUTPUT_SERIES), warning: null };
}
