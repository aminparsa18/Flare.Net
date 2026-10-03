// Client-side N+1 detection over one loaded trace: a parent span whose children include the
// same database statement ≥ `minRepeats` times. Mirrors the server's NPlusOneQueryBuilder
// (src/Flare.Api/Query/NPlusOneQueryBuilder.cs) - same statement normalization, same default
// threshold - keep the two in step.

import type { SpanDto } from '$lib/traces-api';

/** Mirrors `NPlusOneQueryBuilder.DefaultMinRepeats`. */
export const DEFAULT_N_PLUS_ONE_MIN_REPEATS = 10;

export interface NPlusOneFinding {
	statement: string;
	count: number;
	totalDurationNano: number;
}

function attr(span: SpanDto, ...keys: string[]): string {
	for (const key of keys) {
		const value = span.spanAttributes[key];
		if (value) return value;
	}
	return '';
}

/** The normalized statement for a database span, or '' when it isn't one / names nothing usable. */
export function normalizedStatement(span: SpanDto): string {
	if (!attr(span, 'db.system.name', 'db.system')) return '';
	const text = attr(span, 'db.query.text', 'db.statement');
	if (text) return text.replace(/'[^']*'/g, '?').replace(/\b[0-9]+\b/g, '?');
	const operation = attr(span, 'db.operation.name', 'db.operation');
	const collection = attr(span, 'db.collection.name', 'db.sql.table', 'db.mongodb.collection');
	return operation && collection ? `${operation} ${collection}` : '';
}

/** Parent spanId -> its worst repeated statement (most repeats), for parents reaching `minRepeats`. */
export function detectNPlusOne(spans: SpanDto[], minRepeats = DEFAULT_N_PLUS_ONE_MIN_REPEATS): Map<string, NPlusOneFinding> {
	const groups = new Map<string, Map<string, NPlusOneFinding>>();
	for (const span of spans) {
		if (!span.parentSpanId) continue;
		const statement = normalizedStatement(span);
		if (!statement) continue;
		let byStatement = groups.get(span.parentSpanId);
		if (!byStatement) groups.set(span.parentSpanId, (byStatement = new Map()));
		const found = byStatement.get(statement);
		if (found) {
			found.count++;
			found.totalDurationNano += span.durationNano;
		} else {
			byStatement.set(statement, { statement, count: 1, totalDurationNano: span.durationNano });
		}
	}

	const result = new Map<string, NPlusOneFinding>();
	for (const [parentId, byStatement] of groups) {
		let worst: NPlusOneFinding | undefined;
		for (const finding of byStatement.values()) {
			if (finding.count >= minRepeats && (!worst || finding.count > worst.count)) worst = finding;
		}
		if (worst) result.set(parentId, worst);
	}
	return result;
}
