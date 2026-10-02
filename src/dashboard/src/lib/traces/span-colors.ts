// "Color by" grouping shared by the trace waterfall and flame graph: spans are grouped by
// service (default) or by any span/resource attribute, each group gets a palette slot,
// and the legend reports each group's total self-time. Pure and DOM-free, same as
// critical-path.ts and flame-graph.ts.

import type { SpanDto } from '$lib/traces-api';
import { SERIES_COLOR_VARS } from '$lib/metrics/chart-colors';

/** `'service'`, or `'span:<key>'` / `'resource:<key>'` for an attribute. */
export type ColorBy = string;

export const COLOR_BY_SERVICE: ColorBy = 'service';

export interface ColorGroup {
	/** The grouping value; '' = spans without the attribute (or an unnamed service). */
	value: string;
	/** `var(--chart-N)`, or null for the folded "other" bucket past the palette's five. */
	color: string | null;
	spanCount: number;
	/** Sum of the group's spans' self-time, in nanoseconds. */
	selfTimeNano: number;
}

export interface ColorByOption {
	value: ColorBy;
	/** The attribute key (or 'service'); the UI labels the scope itself. */
	key: string;
	scope: 'service' | 'span' | 'resource';
}

export function colorValue(span: SpanDto, by: ColorBy): string {
	if (by === COLOR_BY_SERVICE) return span.serviceName;
	if (by.startsWith('span:')) return span.spanAttributes[by.slice(5)] ?? '';
	if (by.startsWith('resource:')) return span.resourceAttributes[by.slice(9)] ?? '';
	return '';
}

/**
 * Self-time per span: its duration minus the union of its children's intervals (clipped
 * to the span), so concurrent children aren't double-subtracted. Time with no child
 * running is time the span itself spent. Keyed by spanId, in nanoseconds.
 */
export function spanSelfTimes(spans: SpanDto[]): Map<string, number> {
	const spanIds = new Set(spans.map((s) => s.spanId));
	const children = new Map<string, SpanDto[]>();
	for (const span of spans) {
		if (span.parentSpanId && spanIds.has(span.parentSpanId)) {
			const siblings = children.get(span.parentSpanId);
			if (siblings) siblings.push(span);
			else children.set(span.parentSpanId, [span]);
		}
	}

	const result = new Map<string, number>();
	for (const span of spans) {
		const start = new Date(span.startTime).getTime();
		const end = Math.max(new Date(span.endTime).getTime(), start);
		const intervals = (children.get(span.spanId) ?? [])
			.map((c): [number, number] => [
				Math.max(new Date(c.startTime).getTime(), start),
				Math.min(new Date(c.endTime).getTime(), end)
			])
			.filter(([s, e]) => e > s)
			.sort((a, b) => a[0] - b[0]);
		let covered = 0;
		let cursor = start;
		for (const [s, e] of intervals) {
			const from = Math.max(s, cursor);
			if (e > from) {
				covered += e - from;
				cursor = e;
			}
		}
		result.set(span.spanId, Math.max(end - start - covered, 0) * 1_000_000);
	}
	return result;
}

/**
 * One palette slot per group, largest self-time first - rank rather than hash, so within
 * one trace the first five groups are always five distinct hues. Groups past the palette
 * fold into a neutral bucket (color null) rather than cycling, per chart-colors.ts's
 * "never invent a 6th hue" rule.
 */
export function computeColorGroups(spans: SpanDto[], by: ColorBy): ColorGroup[] {
	const selfTimes = spanSelfTimes(spans);
	const groups = new Map<string, { spanCount: number; selfTimeNano: number }>();
	for (const span of spans) {
		const value = colorValue(span, by);
		const g = groups.get(value) ?? { spanCount: 0, selfTimeNano: 0 };
		g.spanCount++;
		g.selfTimeNano += selfTimes.get(span.spanId) ?? 0;
		groups.set(value, g);
	}
	return [...groups.entries()]
		.sort((a, b) => b[1].selfTimeNano - a[1].selfTimeNano || b[1].spanCount - a[1].spanCount || a[0].localeCompare(b[0]))
		.map(([value, g], i) => ({
			value,
			...g,
			color: i < SERIES_COLOR_VARS.length ? `var(${SERIES_COLOR_VARS[i]})` : null
		}));
}

/**
 * Selectable grouping fields: service, plus every span/resource attribute key present in
 * the trace that actually splits the spans (at least two distinct values, or a key only
 * some spans carry) - a key every span shares identically would just paint one color.
 */
export function colorByOptions(spans: SpanDto[]): ColorByOption[] {
	const span = new Map<string, Set<string>>();
	const resource = new Map<string, Set<string>>();
	const seen = (map: Map<string, Set<string>>, key: string, value: string) => {
		const values = map.get(key);
		if (values) values.add(value);
		else map.set(key, new Set([value]));
	};
	for (const s of spans) {
		for (const [k, v] of Object.entries(s.spanAttributes)) seen(span, k, v);
		for (const [k, v] of Object.entries(s.resourceAttributes)) seen(resource, k, v);
	}
	const splits = (map: Map<string, Set<string>>, key: string) => {
		const n = map.get(key)!.size;
		// A key carried by only some spans splits them into "has it" / "doesn't".
		return n > 1 || spans.some((s) => !(key in (map === span ? s.spanAttributes : s.resourceAttributes)));
	};
	const options: ColorByOption[] = [{ value: COLOR_BY_SERVICE, key: 'service', scope: 'service' }];
	for (const key of [...span.keys()].sort()) {
		if (splits(span, key)) options.push({ value: `span:${key}`, key, scope: 'span' });
	}
	// service.name is the Service option already.
	for (const key of [...resource.keys()].sort()) {
		if (key !== 'service.name' && splits(resource, key)) options.push({ value: `resource:${key}`, key, scope: 'resource' });
	}
	return options;
}
