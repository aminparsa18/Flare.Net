// Shared duration parsing for the terminal commands that take a span duration
// (traces --min-duration/--max-duration, search --trace-span-min-duration, --span
// min-duration=...). A direct port of Flare.Cli's TracesCommand.cs TryParseDurationNano -
// a bare number (nanoseconds) or a number with a ns/us/ms/s/m unit suffix.

import { UsageError } from './usage-error';

export function parseDurationNano(text: string, commandName: string, flag?: string): number {
	const match = text.trim().match(/^([0-9.]+)\s*(ns|us|µs|ms|s|m)?$/i);
	if (!match) {
		const subject = flag ? `${flag} '${text}'` : `duration '${text}'`;
		throw new UsageError(`${commandName}: couldn't parse ${subject} - expected e.g. 500ms, 2s, 1.5m`);
	}
	const value = Number.parseFloat(match[1]);
	const unit = (match[2] ?? 'ns').toLowerCase();
	const multiplierByUnit: Record<string, number> = {
		ns: 1,
		us: 1_000,
		µs: 1_000,
		ms: 1_000_000,
		s: 1_000_000_000,
		m: 60 * 1_000_000_000
	};
	return Math.round(value * multiplierByUnit[unit]);
}
