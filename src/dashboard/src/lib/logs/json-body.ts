// Helpers for the JSON tree in EventDetailSheet. Pure, so they're unit-testable without a DOM.

export type JsonValue = string | number | boolean | null | JsonValue[] | { [key: string]: JsonValue };

/** Only objects/arrays count as a "JSON body" - a bare `42` or `"text"` parses as JSON too but a tree adds nothing. */
export function parseJsonBody(body: string): JsonValue | null {
	const trimmed = body.trim();
	const first = trimmed[0];
	if (first !== '{' && first !== '[') return null;
	try {
		const parsed = JSON.parse(trimmed) as JsonValue;
		return typeof parsed === 'object' && parsed !== null ? parsed : null;
	} catch {
		return null;
	}
}

/**
 * BodyJsonFilter.path is object keys joined by `.` (LogFilterSqlBuilder.BodyJsonClause splits on it),
 * with no array-index syntax - so a node is only filterable while every ancestor is an object
 * and no key itself contains a dot. Returns the dotted path, or null when it can't be addressed.
 */
export function bodyJsonPath(segments: string[]): string | null {
	if (segments.length === 0 || segments.some((s) => s === '' || s.includes('.'))) return null;
	return segments.join('.');
}

/** JSONPath-style path for copy-path (any shape, including array indices), unlike {@link bodyJsonPath}. */
export function displayJsonPath(segments: (string | number)[]): string {
	return segments.reduce<string>(
		(acc, s) =>
			typeof s === 'number' ? `${acc}[${s}]` : /^[A-Za-z_][A-Za-z0-9_]*$/.test(s) ? (acc ? `${acc}.${s}` : s) : `${acc}[${JSON.stringify(s)}]`,
		''
	);
}
