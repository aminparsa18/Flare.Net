<script lang="ts">
	// "Natural language -> typed filters" (ADR-0105). The server returns a validated filter in the
	// explorer's own saved-view shape - never SQL - and it is applied through the same restore path
	// as a shared link, so the result shows up as ordinary editable chips. On demand only, and
	// hidden unless an admin configured a model.
	import { onMount } from 'svelte';
	import { Input } from '$lib/components/ui/input';
	import { Button } from '$lib/components/ui/button';
	import { Spinner } from '$lib/components/ui/spinner';
	import SparklesIcon from '@lucide/svelte/icons/sparkles';
	import { generateNlFilter, getAiEnabled, type NlFilterResponse } from '$lib/ai-api';
	import * as m from '$lib/paraglide/messages';

	let {
		target,
		knownServices,
		apply
	}: {
		target: 'Logs' | 'Traces';
		knownServices: () => string[];
		/** Receives the proposed filter; the caller maps it onto its explorer's saved-view state. */
		apply: (filter: NlFilterResponse) => void;
	} = $props();

	let enabled = $state(false);
	let query = $state('');
	let loading = $state(false);
	let error = $state<string | null>(null);
	let applied = $state<NlFilterResponse | null>(null);

	onMount(() => {
		getAiEnabled().then((value) => (enabled = value));
	});

	async function run(): Promise<void> {
		const text = query.trim();
		if (!text || loading) return;
		loading = true;
		error = null;
		try {
			const filter = await generateNlFilter({ target, query: text, knownServices: knownServices().slice(0, 100) });
			apply(filter);
			applied = filter;
		} catch (err) {
			error = err instanceof Error ? err.message : String(err);
		} finally {
			loading = false;
		}
	}
</script>

{#if enabled}
	<form
		class="flex flex-wrap items-center gap-2 border-b px-4 py-2 text-sm"
		onsubmit={(e) => {
			e.preventDefault();
			void run();
		}}
	>
		<SparklesIcon class="text-muted-foreground size-4 shrink-0" />
		<Input class="min-w-48 flex-1" bind:value={query} maxlength={500} placeholder={m.nlFilter_placeholder()} title={m.nlFilter_hint()} />
		<Button type="submit" variant="outline" size="sm" disabled={loading || !query.trim()}>
			{#if loading}<Spinner class="mr-1 size-3" />{/if}
			{m.nlFilter_button()}
		</Button>
		{#if error}
			<p class="text-destructive w-full text-xs">{error}</p>
		{:else if applied}
			<div class="text-muted-foreground flex w-full items-start justify-between gap-2 text-xs">
				<div>
					<p>{m.nlFilter_applied({ model: applied.model })}</p>
					{#each applied.warnings as warning (warning)}
						<p>{warning}</p>
					{/each}
				</div>
				<button type="button" class="hover:text-foreground shrink-0 underline" onclick={() => (applied = null)}>{m.nlFilter_dismiss()}</button>
			</div>
		{/if}
	</form>
{/if}
