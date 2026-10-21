import { getRecordConsolePlugin, type LogLevel } from '@rrweb/rrweb-plugin-console-record';
import { getRecordNetworkPlugin } from '@rrweb/rrweb-plugin-network-record';
import type { ResolvedReplayOptions } from './options.js';
import { sanitizeRequest } from './sanitize.js';

/** Only these kinds of request are recorded: what the page's code asked for, not every image and stylesheet. */
const NETWORK_INITIATORS = ['fetch', 'xmlhttprequest', 'beacon'] as const;

/** The rrweb plugins the options ask for; empty when neither console nor network capture is on. */
export function capturePlugins(options: ResolvedReplayOptions): ReturnType<typeof getRecordConsolePlugin>[] {
	const plugins: ReturnType<typeof getRecordConsolePlugin>[] = [];
	if (options.captureConsole.length > 0) {
		plugins.push(
			getRecordConsolePlugin({
				level: options.captureConsole as LogLevel[],
				lengthThreshold: 50,
				stringifyOptions: { stringLengthLimit: 500, numOfKeysLimit: 30, depthOfLimit: 3 },
			}),
		);
	}
	if (options.captureNetwork) {
		const replayEndpoint = `${options.endpoint}/v1/`;
		const detail = options.networkDetail;
		plugins.push(
			getRecordNetworkPlugin({
				initiatorTypes: [...NETWORK_INITIATORS],
				// The plugin only wraps fetch and XHR, which is what yields the method, when either option is truthy. The object
				// form is truthy but records no headers; sanitizeRequest keeps only what the allowlist names.
				recordHeaders: detail && detail.headers.length > 0 ? true : detail ? { request: false, response: false } : false,
				// Only JSON bodies are ever read from the wire; the rest is never touched.
				recordBody: detail?.bodies ? ['json'] : false,
				recordInitialRequests: false,
				transformRequestFn: (request) => sanitizeRequest(request, replayEndpoint, options.networkKeepQuery, detail),
			}),
		);
	}
	return plugins;
}
