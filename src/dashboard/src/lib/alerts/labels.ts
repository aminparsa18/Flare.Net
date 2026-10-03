// Alert rule labels (ADR-0084): `key=value` pairs set on a rule, and the matchers a
// maintenance window uses against them. Pure helpers for the forms and the rules list; the
// server (`Flare.Api/Model/AlertLabels.cs`) is the authority on what's valid and on which
// windows cover which rules at evaluation time.

const KEY_PATTERN = /^[A-Za-z_][A-Za-z0-9_.-]*$/;
export const MAX_LABELS = 20;
export const MAX_LABEL_KEY_LENGTH = 64;
export const MAX_LABEL_VALUE_LENGTH = 200;

export type Labels = Record<string, string>;

/** `team=payments, env=prod` -> `{ team: 'payments', env: 'prod' }`. `error` is true when any non-empty piece isn't a valid `key=value`. */
export function parseLabels(text: string): { labels: Labels; error: boolean } {
	const labels: Labels = {};
	let error = false;
	for (const piece of text.split(/[,\n]/)) {
		const trimmed = piece.trim();
		if (!trimmed) continue;
		const eq = trimmed.indexOf('=');
		const key = eq < 0 ? '' : trimmed.slice(0, eq).trim();
		const value = eq < 0 ? '' : trimmed.slice(eq + 1).trim();
		if (!KEY_PATTERN.test(key) || key.length > MAX_LABEL_KEY_LENGTH || !value || value.length > MAX_LABEL_VALUE_LENGTH) {
			error = true;
			continue;
		}
		labels[key] = value;
	}
	if (Object.keys(labels).length > MAX_LABELS) error = true;
	return { labels, error };
}

export function formatLabels(labels: Labels | null | undefined): string {
	return Object.entries(labels ?? {})
		.map(([k, v]) => `${k}=${v}`)
		.join(', ');
}

/** True when `labels` contains every pair of `matchers` (exact). Empty matchers match nothing, mirroring `AlertLabels.MatchesAll`. */
export function labelsMatch(matchers: Labels, labels: Labels): boolean {
	const entries = Object.entries(matchers);
	return entries.length > 0 && entries.every(([k, v]) => labels[k] === v);
}
