<script lang="ts">
	import { onMount } from 'svelte';
	import { PipelineRulesState } from '$lib/pipeline-rules/state.svelte';
	import { pipelineRulesContext } from '$lib/pipeline-rules/context';
	import PipelineRuleTable from '$lib/components/pipeline-rules/PipelineRuleTable.svelte';
	import PipelineRuleFormDialog from '$lib/components/pipeline-rules/PipelineRuleFormDialog.svelte';
	import * as m from '$lib/paraglide/messages';

	const pipelineRules = pipelineRulesContext.set(new PipelineRulesState());

	onMount(() => {
		void pipelineRules.load();
	});
</script>

<svelte:head>
	<title>{m.pipelineRulesPage_title()}</title>
</svelte:head>

<div class="flex h-full flex-col">
	<PipelineRuleTable />
</div>
<PipelineRuleFormDialog />
