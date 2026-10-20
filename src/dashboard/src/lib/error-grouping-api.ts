// Client for Flare.Api's grouping-rules API (`/api/errors/grouping-rules`, ADR-0259): the whole
// configuration is read and replaced as one document. Plain JSON, like `error-ownership-api.ts`.

import { API_BASE_URL, apiFetch } from './api';

const URL = `${API_BASE_URL}/api/errors/grouping-rules`;

export type ErrorGroupingKind = 'Replace' | 'Merge';

export interface ErrorGroupingRule {
	kind: ErrorGroupingKind;
	/** Merge only: the exact exception type both groups belong to. '' for Replace. */
	exceptionType: string;
	/** Replace: an RE2 regular expression. Merge: the group message being merged away. */
	pattern: string;
	/** Replace: what each match becomes (`\1` for a capture group). Merge: the group message it joins. */
	replacement: string;
}

export interface ErrorGroupingDocument {
	/** The built-in normalization of ids, numbers and quoted values (ADR-0258). */
	normalizeMessages: boolean;
	rules: ErrorGroupingRule[];
}

async function expectOk(res: Response, what: string): Promise<void> {
	if (res.ok) return;
	let detail = '';
	try {
		detail = ((await res.json()) as { detail?: string }).detail ?? '';
	} catch {
		// not a problem+json body
	}
	throw new Error(detail || `${what} failed: ${res.status} ${res.statusText}`);
}

export async function getErrorGrouping(signal?: AbortSignal): Promise<ErrorGroupingDocument> {
	const res = await apiFetch(URL, { headers: { Accept: 'application/json' }, signal });
	await expectOk(res, 'GET /api/errors/grouping-rules');
	return (await res.json()) as ErrorGroupingDocument;
}

export async function replaceErrorGrouping(document: ErrorGroupingDocument): Promise<ErrorGroupingDocument> {
	const res = await apiFetch(URL, {
		method: 'PUT',
		headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
		body: JSON.stringify(document)
	});
	await expectOk(res, 'PUT /api/errors/grouping-rules');
	return (await res.json()) as ErrorGroupingDocument;
}
