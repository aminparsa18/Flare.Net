import { createContext } from '#lib/notification-channels/context.ts';
import type { CheckInMonitorsState } from './state.svelte';

/** The settings page calls `.set(new CheckInMonitorsState())`; descendants call `.get()`. */
export const checkInMonitorsContext = createContext<CheckInMonitorsState>('checkin-monitors');
