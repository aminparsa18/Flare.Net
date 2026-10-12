// Pure helpers behind the "Schedule reports" dialog (ADR-0142): the cron presets it offers and the
// translation between a dashboard viewer's URL state and a schedule's `timeRange` + `variableQuery` fields.
import { TIME_RANGE_PRESETS } from '$lib/logs/time-range';

/** The fixed-duration presets a schedule may carry - the same set the dashboard's own override accepts. */
export const SCHEDULE_RANGES = TIME_RANGE_PRESETS.filter((p) => p.value !== 'custom').map((p) => p.value as string);

export interface CronPreset {
	id: 'daily' | 'weekdays' | 'weekly' | 'monthly';
	cron: string;
}

/** Common cadences, all at 08:00 in the schedule's time zone. */
export const CRON_PRESETS: CronPreset[] = [
	{ id: 'daily', cron: '0 8 * * *' },
	{ id: 'weekdays', cron: '0 8 * * 1-5' },
	{ id: 'weekly', cron: '0 8 * * 1' },
	{ id: 'monthly', cron: '0 8 1 * *' }
];

/** The preset `cron` matches exactly, or null for a hand-written expression. */
export function presetForCron(cron: string): CronPreset | null {
	const normalized = cron.trim().replace(/\s+/g, ' ');
	return CRON_PRESETS.find((p) => p.cron === normalized) ?? null;
}

/** A rough client-side check; the server's parser is the authority and its message is shown on save. */
export function looksLikeCron(cron: string): boolean {
	return cron.trim().split(/\s+/).length === 5;
}

/**
 * Splits a dashboard URL search string (`?range=7d&var-a=x`) into the schedule's two fields: the range preset
 * (empty when absent, `off`, or not one a schedule can carry) and the `var-*` params re-encoded.
 */
export function splitViewerSearch(search: string): { timeRange: string; variableQuery: string } {
	const params = new URLSearchParams(search);
	const range = params.get('range') ?? '';
	const variables = new URLSearchParams();
	for (const [key, value] of params) {
		if (key.startsWith('var-')) variables.append(key, value);
	}
	return { timeRange: SCHEDULE_RANGES.includes(range) ? range : '', variableQuery: variables.toString() };
}
