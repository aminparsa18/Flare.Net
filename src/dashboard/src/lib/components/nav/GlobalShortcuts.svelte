<script lang="ts">
	// Rebindable app-wide shortcuts (Settings > Keyboard) other than the command palette, which
	// owns its own listener. Bare keys are ignored while typing in a field or while a dialog or
	// sheet is open; the Logs-only actions do nothing off the Logs page.
	import { goto } from '$app/navigation';
	import { withBase } from '$lib/paths';
	import { keyboardPrefs } from '$lib/keyboard/prefs.svelte';
	import { firesInFields, isEditableTarget, matchesCombo, type ShortcutAction } from '$lib/keyboard/shortcuts';
	import { activeLogsExplorer } from '$lib/logs/active-explorer.svelte';

	const ROUTES: Partial<Record<ShortcutAction, string>> = {
		showShortcuts: '/settings/keyboard',
		goLogs: '/',
		goTraces: '/traces',
		goMetrics: '/metrics',
		goAlerts: '/alerts',
		goDashboards: '/dashboards'
	};

	const ACTIONS = Object.keys(ROUTES).concat(['toggleLive', 'focusSearch', 'exportLogs']) as ShortcutAction[];

	function run(id: ShortcutAction): boolean {
		const route = ROUTES[id];
		if (route !== undefined) {
			void goto(withBase(route));
			return true;
		}
		const logs = activeLogsExplorer.current;
		if (!logs) return false;
		if (id === 'toggleLive') logs.explorer.setLive(!logs.explorer.live);
		else if (id === 'exportLogs') logs.openExport();
		else {
			const input = document.querySelector<HTMLInputElement>('[data-logs-search]');
			if (!input) return false;
			input.focus();
			input.select();
		}
		return true;
	}

	function onKeydown(e: KeyboardEvent): void {
		if (e.defaultPrevented) return;
		for (const id of ACTIONS) {
			const combo = keyboardPrefs.combo(id);
			if (!matchesCombo(e, combo)) continue;
			if (!firesInFields(combo) && (isEditableTarget(e.target) || document.querySelector('[role="dialog"]'))) return;
			if (run(id)) e.preventDefault();
			return;
		}
	}
</script>

<svelte:window onkeydown={onKeydown} />
