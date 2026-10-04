<script lang="ts">
	// Event logs from the collector's k8sobjects receiver - see $lib/kubernetes/events.ts.
	import * as Table from '$lib/components/ui/table';
	import * as Empty from '$lib/components/ui/empty';
	import { Badge } from '$lib/components/ui/badge';
	import { Spinner } from '$lib/components/ui/spinner';
	import BellIcon from '@lucide/svelte/icons/bell';
	import { kubernetesContext } from '$lib/kubernetes/context';
	import { formatDateTime } from '$lib/time/format';
	import * as m from '$lib/paraglide/messages';

	const k8s = kubernetesContext.get();

	/** An involved object with a sheet here (Pod, Node) opens it; anything else is plain text. */
	function open(kind: string, name: string, namespace: string): void {
		if (kind === 'Pod') k8s.openPod({ namespace, podName: name });
		else if (kind === 'Node') k8s.openNode(name);
	}
</script>

<div class="px-4 pb-4">
	{#if k8s.error}
		<p class="text-destructive py-2 text-xs">{k8s.error}</p>
	{/if}
	{#if k8s.loading && !k8s.events}
		<div class="flex h-32 items-center justify-center"><Spinner /></div>
	{:else if !k8s.events || k8s.events.length === 0}
		<Empty.Root>
			<Empty.Header>
				<Empty.Media variant="icon"><BellIcon /></Empty.Media>
				<Empty.Title>{m.kubernetesPage_noEventsTitle()}</Empty.Title>
				<Empty.Description>{m.kubernetesPage_noEventsDescription()}</Empty.Description>
			</Empty.Header>
		</Empty.Root>
	{:else}
		{#if k8s.eventsTruncated}
			<p class="text-warning py-2 text-xs">{m.kubernetesPage_truncated({ count: k8s.events.length })}</p>
		{/if}
		<Table.Root>
			<Table.Header>
				<Table.Row>
					<Table.Head>{m.kubernetesPage_eventTimeColumn()}</Table.Head>
					<Table.Head>{m.kubernetesPage_typeColumn()}</Table.Head>
					<Table.Head>{m.kubernetesPage_eventReasonColumn()}</Table.Head>
					<Table.Head>{m.kubernetesPage_eventObjectColumn()}</Table.Head>
					<Table.Head>{m.kubernetesPage_namespaceColumn()}</Table.Head>
					<Table.Head>{m.kubernetesPage_eventMessageColumn()}</Table.Head>
				</Table.Row>
			</Table.Header>
			<Table.Body>
				{#each k8s.events as event (event.eventId)}
					<Table.Row>
						<Table.Cell class="whitespace-nowrap tabular-nums">{formatDateTime(event.timestamp)}</Table.Cell>
						<Table.Cell>
							{#if event.type}<Badge variant={event.type === 'Warning' ? 'destructive' : 'outline'}>{event.type}</Badge>{/if}
						</Table.Cell>
						<Table.Cell class="font-medium">
							{event.reason}
							{#if event.count && event.count > 1}<span class="text-muted-foreground text-xs"> ×{event.count}</span>{/if}
						</Table.Cell>
						<Table.Cell class="max-w-64 truncate" title={event.name}>
							<span class="text-muted-foreground text-xs">{event.kind}</span>
							{#if event.kind === 'Pod' || event.kind === 'Node'}
								<button type="button" class="hover:underline" onclick={() => open(event.kind, event.name, event.namespace)}>{event.name}</button>
							{:else}
								{event.name}
							{/if}
						</Table.Cell>
						<Table.Cell>{event.namespace}</Table.Cell>
						<Table.Cell class="max-w-xl truncate" title={event.message}>{event.message}</Table.Cell>
					</Table.Row>
				{/each}
			</Table.Body>
		</Table.Root>
	{/if}
</div>
