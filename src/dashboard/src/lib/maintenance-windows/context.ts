import { createContext } from '$lib/notification-channels/context';
import type { MaintenanceWindowsState } from './state.svelte';

/** `routes/alerts/+page.svelte` calls `.set(new MaintenanceWindowsState())`; descendants call `.get()`. */
export const maintenanceWindowsContext = createContext<MaintenanceWindowsState>('maintenance-windows');
