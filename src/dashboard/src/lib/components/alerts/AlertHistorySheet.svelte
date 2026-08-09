<script lang="ts">
	// Matches EventDetailSheet.svelte's established "detail viewer" use of Sheet
	// (open/onOpenChange bound to a runes-state field) - AlertRuleFormDialog.svelte uses
	// Dialog instead, for its different "bounded form" role.
	import * as Sheet from '$lib/components/ui/sheet';
	import { ScrollArea } from '$lib/components/ui/scroll-area';
	import { Badge } from '$lib/components/ui/badge';
	import { Spinner } from '$lib/components/ui/spinner';
	import * as Empty from '$lib/components/ui/empty';
	import { alertsContext } from '$lib/alerts/context';

	const alerts = alertsContext.get();

	function formatTimestamp(iso: string): string {
		return new Date(iso).toLocaleString(undefined, { hour12: false });
	}
</script>

<Sheet.Root
	open={alerts.historyRule !== null}
	onOpenChange={(next) => {
		if (!next) alerts.closeHistory();
	}}
>
	<Sheet.Content class="flex w-full flex-col sm:max-w-md">
		{#if alerts.historyRule}
			{@const rule = alerts.historyRule}
			<Sheet.Header>
				<Sheet.Title>{rule.name}</Sheet.Title>
				<Sheet.Description>Fired-alert history</Sheet.Description>
			</Sheet.Header>
			<ScrollArea class="min-h-0 flex-1 px-4">
				{#if alerts.historyLoading}
					<div class="flex justify-center py-8">
						<Spinner />
					</div>
				{:else if alerts.historyError}
					<p class="text-destructive text-sm">{alerts.historyError}</p>
				{:else if alerts.history.length === 0}
					<Empty.Root>
						<Empty.Header>
							<Empty.Title>No alerts fired yet</Empty.Title>
							<Empty.Description>This rule hasn't breached its threshold.</Empty.Description>
						</Empty.Header>
					</Empty.Root>
				{:else}
					<div class="flex flex-col gap-3 pb-8">
						{#each alerts.history as entry (entry.eventId)}
							<div class="rounded-md border p-3 text-xs">
								<div class="flex items-center justify-between">
									<span class="font-medium">{formatTimestamp(entry.firedAt)}</span>
									<Badge variant={entry.notificationStatus === 'Sent' ? 'secondary' : 'destructive'}>
										{entry.notificationStatus}
										{#if entry.notificationStatus === 'Sent'}
											({entry.notificationStatusCode})
										{/if}
									</Badge>
								</div>
								<p class="text-muted-foreground mt-1">
									{entry.observedCount} events (threshold {entry.thresholdCount}) in the last {entry.windowSeconds}s
								</p>
								{#if entry.notificationError}
									<p class="text-destructive mt-1">{entry.notificationError}</p>
								{/if}
							</div>
						{/each}
					</div>
				{/if}
			</ScrollArea>
		{/if}
	</Sheet.Content>
</Sheet.Root>
