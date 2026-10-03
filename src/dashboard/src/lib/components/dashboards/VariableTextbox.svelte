<script lang="ts">
	// Viewer-header input for a `Textbox` dashboard variable - a free-typed value (a user ID,
	// an order ID, a tenant) instead of a pick from a list. Commits on Enter or blur rather
	// than every keystroke, since each commit re-runs every applicable panel's query; an empty
	// box means "All" (this variable doesn't narrow anything), same as the dropdown variables.
	import { Input } from '$lib/components/ui/input';
	import type { DashboardVariable } from '$lib/dashboards-api';
	import SlidersHorizontalIcon from '@lucide/svelte/icons/sliders-horizontal';
	import * as m from '$lib/paraglide/messages';

	let {
		variable,
		value,
		onCommit
	}: {
		variable: DashboardVariable;
		/** The currently applied value, `''` for "All". */
		value: string;
		onCommit: (value: string) => void;
	} = $props();

	// Local draft, resynced whenever the applied value changes from outside (URL state, a
	// reload) - typing alone doesn't touch `value` until commit.
	let draft = $state('');
	$effect(() => {
		draft = value;
	});

	function commit(): void {
		const next = draft.trim();
		if (next !== value) onCommit(next);
		else draft = value;
	}
</script>

<label class="flex items-center gap-1.5 text-sm" title={variable.description || variable.name}>
	<SlidersHorizontalIcon class="text-muted-foreground size-4" />
	<span class="whitespace-nowrap">{variable.name}:</span>
	<Input
		class="h-8 w-40"
		bind:value={draft}
		placeholder={m.dashboardViewer_variableAll()}
		onkeydown={(e) => e.key === 'Enter' && commit()}
		onblur={commit}
	/>
</label>
