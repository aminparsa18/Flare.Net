import { createContext } from '$lib/notification-channels/context';
import type { OnCallRotationsState } from './state.svelte';

/** The settings page and the alerts page call `.set(new OnCallRotationsState())`; descendants call `.get()`. */
export const onCallRotationsContext = createContext<OnCallRotationsState>('oncall-rotations');
