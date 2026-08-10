// Pure duration-formatting helper for SpanDto.durationNano - used by both the trace
// list rows and the waterfall's per-span bars, same "one place, both consumers" spirit
// as `logs/severity.ts`.

export function formatDurationNano(durationNano: number): string {
	const ms = durationNano / 1_000_000;
	if (ms < 1) return `${Math.round(durationNano / 1_000)}µs`;
	if (ms < 1_000) return `${ms.toFixed(ms < 10 ? 2 : 1)}ms`;
	return `${(ms / 1_000).toFixed(2)}s`;
}
