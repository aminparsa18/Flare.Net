// The one place every displayed timestamp is formatted - tables, log/trace rows, detail
// sheets, chart axes/tooltips, the terminal. Everything renders in the display time zone
// (./display-zone.svelte.ts) and always 24-hour, so switching the zone or reading two
// screens side by side never shows the same instant two different ways.
//
// Built from wall-clock parts rather than toLocaleString: the numeric shapes below
// (YYYY-MM-DD HH:mm:ss.SSS etc.) are fixed-width regardless of locale, which the
// monospace log/trace time columns rely on to not jitter row to row, and a locale's own
// date order (M/D vs D/M) is exactly the ambiguity a UTC-correlation setting is meant to
// remove. Only month *names* ("Sep 26", chart labels) go through the UI locale.
//
// Callers inside a template/$derived re-run automatically when the zone changes, since
// every function here reads displayTimeZone.zone.
import { getLocale } from '$lib/paraglide/runtime';
import { displayTimeZone } from './display-zone.svelte';

export type TimeInput = string | number | Date;

interface WallClock {
	year: number;
	month: number; // 1-12
	day: number;
	hour: number;
	minute: number;
	second: number;
	ms: number;
}

const MINUTE_MS = 60_000;
const DAY_MS = 24 * 60 * MINUTE_MS;

// One formatter per zone - constructing an Intl.DateTimeFormat is far more expensive than
// calling it, and a log table formats hundreds of rows per render.
const partsFormatters = new Map<string, Intl.DateTimeFormat>();

function partsFormatter(timeZone: string): Intl.DateTimeFormat {
	let f = partsFormatters.get(timeZone);
	if (!f) {
		f = new Intl.DateTimeFormat('en-US', {
			timeZone,
			hourCycle: 'h23',
			year: 'numeric',
			month: 'numeric',
			day: 'numeric',
			hour: 'numeric',
			minute: 'numeric',
			second: 'numeric'
		});
		partsFormatters.set(timeZone, f);
	}
	return f;
}

function wallClockIn(date: Date, timeZone: string | 'local'): WallClock {
	const ms = ((date.getTime() % 1000) + 1000) % 1000;
	if (timeZone === 'local') {
		return {
			year: date.getFullYear(),
			month: date.getMonth() + 1,
			day: date.getDate(),
			hour: date.getHours(),
			minute: date.getMinutes(),
			second: date.getSeconds(),
			ms
		};
	}
	const out: WallClock = { year: 0, month: 0, day: 0, hour: 0, minute: 0, second: 0, ms };
	for (const p of partsFormatter(timeZone).formatToParts(date)) {
		if (p.type === 'year' || p.type === 'month' || p.type === 'day' || p.type === 'hour' || p.type === 'minute' || p.type === 'second') {
			out[p.type] = Number(p.value);
		}
	}
	return out;
}

function toDate(value: TimeInput): Date {
	return value instanceof Date ? value : new Date(value);
}

/** `value`'s wall-clock parts in the display time zone. */
function wallClock(value: TimeInput): WallClock {
	return wallClockIn(toDate(value), displayTimeZone.isLocal ? 'local' : displayTimeZone.resolved);
}

const pad = (n: number, len = 2) => String(n).padStart(len, '0');

const ymd = (c: WallClock) => `${c.year}-${pad(c.month)}-${pad(c.day)}`;
const hm = (c: WallClock) => `${pad(c.hour)}:${pad(c.minute)}`;
const hms = (c: WallClock) => `${hm(c)}:${pad(c.second)}`;
const hmsms = (c: WallClock) => `${hms(c)}.${pad(c.ms, 3)}`;

const monthDayFormatters = new Map<string, Intl.DateTimeFormat>();

/**
 * "Sep 26" / "26 сент." / "9月26日" in the UI's language - the one locale-dependent piece of
 * any format here. The wall-clock day is re-read as a UTC date so Intl only decides the
 * wording and order, never the zone.
 */
function monthDay(c: WallClock): string {
	const locale = getLocale();
	let f = monthDayFormatters.get(locale);
	if (!f) {
		f = new Intl.DateTimeFormat(locale, { month: 'short', day: 'numeric', timeZone: 'UTC' });
		monthDayFormatters.set(locale, f);
	}
	return f.format(Date.UTC(c.year, c.month - 1, c.day));
}

/** `2026-09-26 14:03:05.123` - an event's own time: detail sheets, occurrences, anything where ms can matter. */
export function formatTimestamp(value: TimeInput): string {
	const c = wallClock(value);
	return `${ymd(c)} ${hmsms(c)}`;
}

/** `2026-09-26 14:03:05` - record bookkeeping: created/updated/last-used columns. */
export function formatDateTime(value: TimeInput): string {
	const c = wallClock(value);
	return `${ymd(c)} ${hms(c)}`;
}

/** `2026-09-26 14:03` - scheduled instants that are only ever picked to the minute (maintenance windows). */
export function formatDateTimeMinutes(value: TimeInput): string {
	const c = wallClock(value);
	return `${ymd(c)} ${hm(c)}`;
}

/** `09-26 14:03:05.123` - the fixed-width time column of Logs/Traces rows and the terminal's listings. */
export function formatRowTimestamp(value: TimeInput): string {
	const c = wallClock(value);
	return `${pad(c.month)}-${pad(c.day)} ${hmsms(c)}`;
}

/** Time of day only: `14:03`, `14:03:05`, or `14:03:05.123`. */
export function formatTimeOfDay(value: TimeInput, precision: 'minute' | 'second' | 'ms' = 'second'): string {
	const c = wallClock(value);
	return precision === 'minute' ? hm(c) : precision === 'second' ? hms(c) : hmsms(c);
}

/**
 * A chart's time-axis tick. Only date-bearing once the visible span actually crosses days -
 * a bare `14:00` on a 7-day chart is ambiguous, `Sep 26 14:00` on a 15-minute one is noise.
 * @param spanMs Width of the chart's whole x-axis, not one bucket.
 */
export function formatAxisTime(value: TimeInput, spanMs: number): string {
	const c = wallClock(value);
	if (spanMs <= 10 * MINUTE_MS) return hms(c);
	if (spanMs <= DAY_MS) return hm(c);
	if (spanMs <= 90 * DAY_MS) return `${monthDay(c)} ${hm(c)}`;
	return monthDay(c);
}

/**
 * A chart point/bucket's hover label - always dated (the tooltip is where the reader goes to
 * pin down *when*), seconds only when buckets are finer than a minute.
 * @param bucketMs The point's bucket width; omit when unknown (seconds then show only if non-zero).
 */
export function formatChartTime(value: TimeInput, bucketMs?: number): string {
	const c = wallClock(value);
	const withSeconds = bucketMs !== undefined ? bucketMs < MINUTE_MS : c.second !== 0;
	return `${monthDay(c)} ${withSeconds ? hms(c) : hm(c)}`;
}

/** A `[from, to)` span for a hover label or comparison caption - the date isn't repeated when both ends share it. */
export function formatChartRange(from: TimeInput, to: TimeInput, bucketMs?: number): string {
	const a = wallClock(from);
	const b = wallClock(to);
	const withSeconds = bucketMs !== undefined ? bucketMs < MINUTE_MS : a.second !== 0 || b.second !== 0;
	const time = withSeconds ? hms : hm;
	const sameDay = a.year === b.year && a.month === b.month && a.day === b.day;
	return `${monthDay(a)} ${time(a)} – ${sameDay ? '' : `${monthDay(b)} `}${time(b)}`;
}

/**
 * `Sep 26` for a server-side calendar day (ClickHouse `toDate(...)`, serialized as UTC
 * midnight) - read in UTC regardless of the display zone, since it names a date rather
 * than an instant; shifting it would show the previous day anywhere west of Greenwich.
 */
export function formatCalendarDay(value: TimeInput): string {
	return monthDay(wallClockIn(toDate(value), 'UTC'));
}

/** `UTC+05:30`-style offset of `timeZone` right now - shown next to zone names in the picker. */
export function formatUtcOffset(timeZone: string, at: Date = new Date()): string {
	const c = wallClockIn(at, timeZone);
	const asUtc = Date.UTC(c.year, c.month - 1, c.day, c.hour, c.minute, c.second);
	const offsetMinutes = Math.round((asUtc - (at.getTime() - c.ms)) / MINUTE_MS);
	if (offsetMinutes === 0) return 'UTC';
	const sign = offsetMinutes > 0 ? '+' : '-';
	const abs = Math.abs(offsetMinutes);
	return `UTC${sign}${pad(Math.floor(abs / 60))}:${pad(abs % 60)}`;
}
