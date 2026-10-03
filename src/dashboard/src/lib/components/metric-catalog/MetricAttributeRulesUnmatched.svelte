<script lang="ts">
	// Warns about attribute-reduction rules (ADR-0083) that match no metric ingested in the last
	// 24 h - a typo in the name, a renamed metric, or one that stopped being emitted. Shown to
	// members only (the people who can fix or delete a rule) and only when there is something
	// to report, so it stays out of the way otherwise.
	import { onMount } from 'svelte';
	import { Button } from '$lib/components/ui/button';
	import TrashIcon from '@lucide/svelte/icons/trash-2';
	import TriangleAlertIcon from '@lucide/svelte/icons/triangle-alert';
	import {
		deleteMetricAttributeRule,
		listMetricAttributeRules,
		listUnmatchedMetricAttributeRules,
		type MetricAttributeRule
	} from '$lib/metric-attribute-rules-api';
	import * as m from '$lib/paraglide/messages';

	let unmatched = $state<MetricAttributeRule[]>([]);
	let hours = $state(24);
	let busy = $state(false);

	async function load(): Promise<void> {
		try {
			const [rules, result] = await Promise.all([listMetricAttributeRules(), listUnmatchedMetricAttributeRules()]);
			const ids = new Set(result.ruleIds);
			unmatched = rules.filter((r) => ids.has(r.id));
			hours = Math.round(result.windowMinutes / 60);
		} catch {
			// Advisory only - a failed check just means no banner.
			unmatched = [];
		}
	}

	async function remove(rule: MetricAttributeRule): Promise<void> {
		busy = true;
		try {
			await deleteMetricAttributeRule(rule.id);
			await load();
		} finally {
			busy = false;
		}
	}

	onMount(() => {
		void load();
	});
</script>

{#if unmatched.length > 0}
	<div class="mx-4 mt-3 rounded-md border border-amber-500/40 bg-amber-500/10 p-3 text-sm">
		<p class="flex items-center gap-2 font-medium"><TriangleAlertIcon class="size-4" />{m.metricAttrRules_unmatchedHeading({ hours })}</p>
		<ul class="mt-2 space-y-1">
			{#each unmatched as rule (rule.id)}
				<li class="flex items-center gap-2">
					<span class="font-mono text-xs break-all">{rule.metricName}</span>
					<span class="text-muted-foreground text-xs">{rule.name}</span>
					<Button class="ml-auto" variant="ghost" size="icon-sm" disabled={busy} onclick={() => remove(rule)} aria-label={m.metricAttrRules_delete()}>
						<TrashIcon />
					</Button>
				</li>
			{/each}
		</ul>
	</div>
{/if}
