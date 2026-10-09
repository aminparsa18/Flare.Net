// Pure helpers for the telemetry-export settings forms (ADR-0157).

/** Header values come back masked; sending the mask back unchanged keeps the stored value, so a
 *  textarea round-trip ("Name: value" lines) must preserve them verbatim. */
export function formatHeaderLines(headers: Record<string, string>): string {
	return Object.entries(headers)
		.map(([name, value]) => `${name}: ${value}`)
		.join('\n');
}

/** One `Name: value` per line; splits on the first colon only (values may contain colons). Blank lines are skipped. */
export function parseHeaderLines(text: string): { headers: Record<string, string>; invalidLine: string | null } {
	const headers: Record<string, string> = {};
	for (const raw of text.split('\n')) {
		const line = raw.trim();
		if (!line) continue;
		const colon = line.indexOf(':');
		const name = colon > 0 ? line.slice(0, colon).trim() : '';
		if (!name || /\s/.test(name)) return { headers: {}, invalidLine: line };
		headers[name] = line.slice(colon + 1).trim();
	}
	return { headers, invalidLine: null };
}

/** Comma- or newline-separated list, trimmed, blanks and duplicates dropped. */
export function parseList(text: string): string[] {
	return [...new Set(text.split(/[,\n]/).map((s) => s.trim()).filter(Boolean))];
}

/** Flare.Ingest keys a managed target's live status by the id without dashes ("N" format). */
export function statusKeyForTarget(id: string): string {
	return id.replaceAll('-', '').toLowerCase();
}

/** ClickHouse error text arrives with a trailing newline and can be long; one trimmed line is enough for a table cell. */
export function singleLineError(error: string | null): string | null {
	return error ? error.replace(/\s+/g, ' ').trim() : null;
}
