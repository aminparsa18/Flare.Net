// Wall-clock <-> instant conversion for an arbitrary IANA time zone. Used by the
// maintenance window form (a window's start/end are edited as local times in the window's
// own `timeZone`, which needn't be the browser's, but sent to the API as absolute instants)
// and by the display time zone setting (./display-zone.svelte.ts) for "Today"/"This week"
// and custom-range bounds. Built on `Intl.DateTimeFormat` alone - no date library in this
// dashboard.

/** `timeZone`'s offset from UTC, in ms, at `date`. */
function offsetMs(date: Date, timeZone: string): number {
	const parts = new Intl.DateTimeFormat('en-US', {
		timeZone,
		hourCycle: 'h23',
		year: 'numeric',
		month: '2-digit',
		day: '2-digit',
		hour: '2-digit',
		minute: '2-digit',
		second: '2-digit'
	}).formatToParts(date);
	const part = (type: Intl.DateTimeFormatPartTypes) => Number(parts.find((p) => p.type === type)?.value ?? 0);
	const wallClockAsUtc = Date.UTC(part('year'), part('month') - 1, part('day'), part('hour'), part('minute'), part('second'));
	return wallClockAsUtc - (date.getTime() - date.getMilliseconds());
}

/** A `datetime-local` value (`YYYY-MM-DDTHH:mm`) read as wall-clock time in `timeZone`, as an instant. */
export function zonedToInstant(local: string, timeZone: string): Date {
	const [datePart, timePart = '00:00'] = local.split('T');
	const [year, month, day] = datePart.split('-').map(Number);
	const [hour, minute] = timePart.split(':').map(Number);
	const wallClockAsUtc = Date.UTC(year, month - 1, day, hour, minute);
	// Two passes: the first guess's offset can be off by a DST change between the guess and
	// the real instant.
	const first = wallClockAsUtc - offsetMs(new Date(wallClockAsUtc), timeZone);
	return new Date(wallClockAsUtc - offsetMs(new Date(first), timeZone));
}

/** `date` as a `datetime-local` value (`YYYY-MM-DDTHH:mm`) in `timeZone`. */
export function instantToZoned(date: Date, timeZone: string): string {
	return new Date(date.getTime() + offsetMs(date, timeZone)).toISOString().slice(0, 16);
}

export function browserTimeZone(): string {
	return Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC';
}

/** Every IANA zone this browser knows, UTC first. */
export function timeZoneOptions(): string[] {
	const zones = typeof Intl.supportedValuesOf === 'function' ? Intl.supportedValuesOf('timeZone') : [];
	return ['UTC', ...zones.filter((z) => z !== 'UTC')];
}
