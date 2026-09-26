// Same generic typed-context helper as `$lib/hosts/context.ts` - re-authored here rather
// than shared, same rationale that file gives.

import { getContext, hasContext, setContext } from 'svelte';
import type { MessagingState } from './state.svelte';

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

/** `routes/messaging/+page.svelte` calls `.set(new MessagingState())`; every descendant calls `.get()`. */
export const messagingContext = createContext<MessagingState>('messaging');
