// "Around a time" helpers: parse a pasted/typed timestamp and turn it into a ± window.
import { zonedToInstant } from './time-zone';

/** Window half-widths offered by the picker's "Around" mode, in ms. */
export const AROUND_WINDOWS_MS = [60_000, 5 * 60_000, 15 * 60_000, 60 * 60_000] as const;

/** Default half-width for the "Show ±5m around this" row/span actions. */
export const AROUND_DEFAULT_MS = 5 * 60_000;

const HAS_OFFSET = /(Z|[+-]\d{2}:?\d{2})$/i;
const WALL_CLOCK = /^(\d{4}-\d{2}-\d{2})[T ](\d{2}:\d{2})(?::(\d{2})(?:\.(\d{1,3})\d*)?)?$/;

/**
 * Parses `input` as an instant. Accepts epoch milliseconds, an ISO-8601 string with an
 * offset/`Z`, or a zone-less `YYYY-MM-DD[ T]HH:mm[:ss[.fff]]` read as wall-clock time in
 * `timeZone` (the display zone, same as every timestamp the UI shows). Returns null when
 * it can't be parsed.
 */
export function parseAroundTime(input: string, timeZone: string): Date | null {
	const text = input.trim();
	if (!text) return null;
	if (/^\d{12,}$/.test(text)) {
		const date = new Date(Number(text));
		return Number.isNaN(date.getTime()) ? null : date;
	}
	if (HAS_OFFSET.test(text)) {
		const date = new Date(text.replace(' ', 'T'));
		return Number.isNaN(date.getTime()) ? null : date;
	}
	const match = WALL_CLOCK.exec(text);
	if (!match) return null;
	const [, day, hm, seconds = '0', millis = '0'] = match;
	const base = zonedToInstant(`${day}T${hm}`, timeZone);
	return new Date(base.getTime() + Number(seconds) * 1000 + Number(millis.padEnd(3, '0')));
}

/** `center` ± `halfMs`. */
export function aroundRange(center: Date, halfMs: number): { from: Date; to: Date } {
	return { from: new Date(center.getTime() - halfMs), to: new Date(center.getTime() + halfMs) };
}
