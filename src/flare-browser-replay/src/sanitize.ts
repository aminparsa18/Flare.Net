// Pure and free of rrweb, so the tests can import it in Node.
import type { NetworkRequest } from '@rrweb/rrweb-plugin-network-record';

/** Longest URL kept for a request; a longer one is cut, since a recording is not the place for a data: URL or a token blob. */
export const MAX_URL_LENGTH = 300;

/** What `networkDetail` asked for, resolved: header names are lower case. */
export interface NetworkDetail {
	headers: string[];
	bodies: boolean;
	bodyLimit: number;
}

export const DEFAULT_BODY_LIMIT = 2048;
export const MAX_HEADER_VALUE_LENGTH = 200;
const MAX_METHOD_LENGTH = 10;
const MAX_BODY_DEPTH = 6;
const MAX_BODY_ITEMS = 50;

/** Header names that are never recorded, even when the integrator lists them: credentials and session identifiers. */
const CREDENTIAL_HEADER = /auth|cookie|token|secret|password|passwd|key|session|credential/i;
/** JSON keys whose values are replaced in a recorded body. */
const SECRET_KEY = /pass(word|wd)?|secret|token|auth|api[-_]?key|card|cvv|cvc|iban|ssn|credential|private|otp/i;

/** The allowlisted headers of one side of a request, or undefined when none survive. */
export function filterHeaders(headers: Record<string, unknown> | undefined, allow: readonly string[]): Record<string, string> | undefined {
	if (!headers || allow.length === 0) return undefined;
	const out: Record<string, string> = {};
	for (const [name, value] of Object.entries(headers)) {
		const lower = name.toLowerCase();
		if (!allow.includes(lower) || CREDENTIAL_HEADER.test(lower) || typeof value !== 'string') continue;
		out[lower] = value.length > MAX_HEADER_VALUE_LENGTH ? `${value.slice(0, MAX_HEADER_VALUE_LENGTH)}…` : value;
	}
	return Object.keys(out).length > 0 ? out : undefined;
}

function redact(value: unknown, depth: number): unknown {
	if (depth > MAX_BODY_DEPTH) return '[deep]';
	if (Array.isArray(value)) return value.slice(0, MAX_BODY_ITEMS).map((v) => redact(v, depth + 1));
	if (typeof value === 'object' && value !== null) {
		const out: Record<string, unknown> = {};
		for (const [key, v] of Object.entries(value).slice(0, MAX_BODY_ITEMS)) out[key] = SECRET_KEY.test(key) ? '[redacted]' : redact(v, depth + 1);
		return out;
	}
	return value;
}

/**
 * A recorded body: JSON only (anything else, including the plugin's own "[rrweb/network] ..." placeholders, is dropped),
 * values under secret-looking keys replaced, and the text cut to `limit` characters.
 */
export function redactJsonBody(body: unknown, limit: number): string | undefined {
	if (typeof body !== 'string' || body === '') return undefined;
	let parsed: unknown;
	try {
		parsed = JSON.parse(body);
	} catch {
		return undefined;
	}
	const text = JSON.stringify(redact(parsed, 0));
	return text.length > limit ? `${text.slice(0, limit)}…` : text;
}

/**
 * What a request is reduced to before it is recorded: the URL without its query string or fragment (those carry tokens and
 * ids), cut to `MAX_URL_LENGTH`, plus status and timing. Headers and bodies are not recorded unless `detail` asks for them,
 * and then only the allowlisted headers and the redacted JSON body of a failed request. Returns undefined, which
 * drops the request, for the recorder's own uploads (`replayEndpoint` is `{endpoint}/v1/`) and for a URL that cannot be read.
 */
export function sanitizeRequest(request: NetworkRequest, replayEndpoint: string, keepQuery: boolean, detail?: NetworkDetail): NetworkRequest | undefined {
	const name = request.name;
	if (!name || name.startsWith('data:') || name.startsWith('blob:') || name.startsWith(replayEndpoint)) return undefined;
	let url = name;
	if (!keepQuery) {
		const cut = url.search(/[?#]/);
		if (cut >= 0) url = url.slice(0, cut);
	}
	if (url.length > MAX_URL_LENGTH) url = `${url.slice(0, MAX_URL_LENGTH)}…`;
	const out: NetworkRequest = {
		name: url,
		initiatorType: request.initiatorType,
		status: request.status,
		startTime: request.startTime,
		endTime: request.endTime,
		duration: request.duration,
		entryType: request.entryType,
	};
	if (!detail) return out;
	if (typeof request.method === 'string' && /^[A-Za-z]+$/.test(request.method) && request.method.length <= MAX_METHOD_LENGTH) out.method = request.method.toUpperCase();
	const requestHeaders = filterHeaders(request.requestHeaders, detail.headers);
	const responseHeaders = filterHeaders(request.responseHeaders, detail.headers);
	if (requestHeaders) out.requestHeaders = requestHeaders;
	if (responseHeaders) out.responseHeaders = responseHeaders;
	if (detail.bodies && typeof request.status === 'number' && request.status >= 400) {
		const requestBody = redactJsonBody(request.requestBody, detail.bodyLimit);
		const responseBody = redactJsonBody(request.responseBody, detail.bodyLimit);
		if (requestBody) out.requestBody = requestBody;
		if (responseBody) out.responseBody = responseBody;
	}
	return out;
}
