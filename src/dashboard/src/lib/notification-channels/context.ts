// Same generic typed-context helper as `$lib/access-tokens/context.ts` - re-authored here
// rather than shared, per that file's own comment on why (no shared "context utils"
// module yet for a two-line helper).

import { getContext, hasContext, setContext } from 'svelte';
import type { NotificationChannelsState } from './state.svelte';

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

/** `routes/notification-channels/+page.svelte` calls `.set(new NotificationChannelsState())`; every descendant calls `.get()` instead of receiving it as a prop. */
export const notificationChannelsContext = createContext<NotificationChannelsState>('notification-channels');
