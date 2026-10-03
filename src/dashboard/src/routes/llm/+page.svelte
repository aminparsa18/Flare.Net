<script lang="ts">
	// Model calls grouped by provider and model, from spans' `gen_ai.*` attributes - see
	// docs-internal/adr/0100-llm-observability-genai-spans.md.
	import { onMount, onDestroy } from 'svelte';
	import { LlmState } from '$lib/llm/state.svelte';
	import { llmContext } from '$lib/llm/context';
	import LlmToolbar from '$lib/components/llm/LlmToolbar.svelte';
	import LlmTable from '$lib/components/llm/LlmTable.svelte';
	import * as m from '$lib/paraglide/messages';

	const llm = llmContext.set(new LlmState());

	onMount(() => llm.load());
	onDestroy(() => llm.dispose());
</script>

<svelte:head>
	<title>{m.llmPage_title()}</title>
</svelte:head>

<div class="flex h-full flex-col overflow-y-auto">
	<LlmToolbar />
	<LlmTable />
</div>
