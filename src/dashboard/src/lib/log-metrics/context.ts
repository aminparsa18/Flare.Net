// Same generic typed-context helper as `$lib/pipeline-rules/context.ts`.

import { getContext, hasContext, setContext } from 'svelte';
import type { LogMetricsState } from './state.svelte';

export function createContext<T>(name: string) {
	const key = Symbol(name);
	return {
		set: (value: T): T => setContext(key, value),
		get: (): T => {
			if (!hasContext(key)) {
				throw new Error(`No context found for "${name}" - did an ancestor component forget to call .set()?`);
			}
			return getContext<T>(key);
		}
	};
}

/** `routes/settings/log-metrics/+page.svelte` calls `.set(new LogMetricsState())`; descendants call `.get()`. */
export const logMetricsContext = createContext<LogMetricsState>('log-metrics');
