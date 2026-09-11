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
	import * as m from '$lib/paraglide/messages';

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
				<Sheet.Description>{m.alertHistory_description()}</Sheet.Description>
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
							<Empty.Title>{m.alertHistory_emptyTitle()}</Empty.Title>
							<Empty.Description>{m.alertHistory_emptyDescription()}</Empty.Description>
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
									{#if entry.conditionKind === 'MetricThreshold'}
										{m.alertHistory_entrySummaryMetric({
											value: entry.observedValue ?? 0,
											threshold: entry.thresholdValue ?? 0,
											window: entry.windowSeconds
										})}
									{:else}
										{m.alertHistory_entrySummary({
											count: entry.observedCount,
											threshold: entry.thresholdCount,
											window: entry.windowSeconds
										})}
									{/if}
								</p>
								{#if entry.notificationError}
									<p class="text-destructive mt-1">{entry.notificationError}</p>
								{/if}
								{#if entry.channelResults.length > 1}
									<!-- Fan-out fire (see docs-internal/adr/0021-reusable-notification-channels.md) -
									     notificationStatus/notificationError above are the summary across every
									     channel; this is the per-channel breakdown. -->
									<div class="mt-1 flex flex-wrap gap-1">
										{#each entry.channelResults as result (result.channelId ?? result.channelName)}
											<Badge variant={result.success ? 'secondary' : 'destructive'} class="font-normal">
												{result.channelName}
											</Badge>
										{/each}
									</div>
								{/if}
							</div>
						{/each}
					</div>
				{/if}
			</ScrollArea>
		{/if}
	</Sheet.Content>
</Sheet.Root>
