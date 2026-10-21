// Pure and free of rrweb, so the tests can import it in Node.
import type { NetworkRequest } from '@rrweb/rrweb-plugin-network-record';

/** Longest URL kept for a request; a longer one is cut, since a recording is not the place for a data: URL or a token blob. */
export const MAX_URL_LENGTH = 300;

/**
 * What a request is reduced to before it is recorded: the URL without its query string or fragment (those carry tokens and
 * ids), cut to `MAX_URL_LENGTH`, plus status and timing. Headers and bodies are never recorded. Returns undefined, which
 * drops the request, for the recorder's own uploads (`replayEndpoint` is `{endpoint}/v1/`) and for a URL that cannot be read.
 */
export function sanitizeRequest(request: NetworkRequest, replayEndpoint: string, keepQuery: boolean): NetworkRequest | undefined {
	const name = request.name;
	if (!name || name.startsWith('data:') || name.startsWith('blob:') || name.startsWith(replayEndpoint)) return undefined;
	let url = name;
	if (!keepQuery) {
		const cut = url.search(/[?#]/);
		if (cut >= 0) url = url.slice(0, cut);
	}
	if (url.length > MAX_URL_LENGTH) url = `${url.slice(0, MAX_URL_LENGTH)}…`;
	return {
		name: url,
		initiatorType: request.initiatorType,
		status: request.status,
		startTime: request.startTime,
		endTime: request.endTime,
		duration: request.duration,
		entryType: request.entryType,
	};
}
