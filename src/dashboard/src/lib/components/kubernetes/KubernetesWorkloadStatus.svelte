<script lang="ts">
	// A workload's status badges - "ready of desired" for the replicated kinds, succeeded/
	// failed/active for a Job, active jobs for a CronJob. Shared by the Workloads table and
	// the workload drill-down sheet.
	import { Badge } from '$lib/components/ui/badge';
	import { isReplicatedKind, readyVariant } from '$lib/kubernetes/format';
	import type { KubernetesWorkloadCounts, KubernetesWorkloadKind } from '$lib/kubernetes-api';
	import * as m from '$lib/paraglide/messages';

	interface Props {
		kind: KubernetesWorkloadKind;
		counts: KubernetesWorkloadCounts;
	}

	let { kind, counts }: Props = $props();

	const hasAny = $derived(Object.values(counts).some((v) => v != null));
</script>

{#if !hasAny}
	<span class="text-muted-foreground" title={m.kubernetesPage_noDataTooltip()}>&mdash;</span>
{:else}
	<span class="inline-flex flex-wrap items-center gap-1">
		{#if isReplicatedKind(kind)}
			{#if counts.ready != null && counts.desired != null}
				<Badge variant={readyVariant(counts.ready, counts.desired)}>
					{m.kubernetesPage_readyOfDesired({ ready: counts.ready, desired: counts.desired })}
				</Badge>
			{:else if counts.desired != null}
				<span class="text-xs">{m.kubernetesPage_desiredLabel()} {counts.desired}</span>
			{/if}
			{#if kind === 'DaemonSet' && counts.misscheduled}
				<Badge variant="destructive">{m.kubernetesPage_misscheduledCount({ count: counts.misscheduled })}</Badge>
			{/if}
		{:else}
			{#if counts.succeeded != null && counts.desired != null}
				<Badge variant="outline">{m.kubernetesPage_succeededOfDesired({ succeeded: counts.succeeded, desired: counts.desired })}</Badge>
			{:else if counts.succeeded}
				<Badge variant="outline">{m.kubernetesPage_succeededCount({ count: counts.succeeded })}</Badge>
			{/if}
			{#if counts.failed}
				<Badge variant="destructive">{m.kubernetesPage_failedCount({ count: counts.failed })}</Badge>
			{/if}
			{#if counts.active != null && (counts.active > 0 || kind === 'CronJob')}
				<Badge variant={counts.active > 0 ? 'secondary' : 'outline'}>{m.kubernetesPage_activeCount({ count: counts.active })}</Badge>
			{/if}
		{/if}
	</span>
{/if}
