import { createContext } from '$lib/notification-channels/context';
import type { SyntheticMonitorsState } from './state.svelte';

/** The settings page calls `.set(new SyntheticMonitorsState())`; descendants call `.get()`. */
export const syntheticMonitorsContext = createContext<SyntheticMonitorsState>('synthetic-monitors');
