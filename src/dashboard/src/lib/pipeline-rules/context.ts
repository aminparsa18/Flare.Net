// Same generic typed-context helper as `$lib/alerts/context.ts` - re-authored here rather
// than shared, same reasoning that file's own comment gives.

import { getContext, hasContext, setContext } from 'svelte';
import type { PipelineRulesState } from './state.svelte';

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

/** `routes/pipeline-rules/+page.svelte` calls `.set(new PipelineRulesState())`; every descendant calls `.get()` instead of receiving it as a prop. */
export const pipelineRulesContext = createContext<PipelineRulesState>('pipeline-rules');
