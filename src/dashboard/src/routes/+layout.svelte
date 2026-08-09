<script lang="ts">
	import './layout.css';
	import { ModeWatcher } from 'mode-watcher';
	import AppNav from '$lib/components/nav/AppNav.svelte';

	const { children } = $props();
</script>

<!-- Handles both the dark/light class on <html> and, crucially, injects an inline
     anti-FOUC script into <head> (see mode-watcher's ModeWatcherFull) that runs
     synchronously before first paint - this app is SSR'd (adapter-node, no ssr=false
     anywhere), so without that script the page would flash the wrong theme on every
     load until Svelte hydrates. Defaults: system preference until the user picks
     explicitly (then persisted to localStorage), darkClassNames: ["dark"] - already
     exactly what layout.css's .dark selector expects, so no config needed here. -->
<ModeWatcher />

<!-- Shared across every route (Logs, Alerts, ...) - a route's own root div is
     responsible for its own scroll/height handling below this, same as the Logs page's
     `flex h-screen flex-col` already did before this nav existed. -->
<div class="flex h-screen flex-col">
	<AppNav />
	<div class="min-h-0 flex-1">
		{@render children()}
	</div>
</div>
