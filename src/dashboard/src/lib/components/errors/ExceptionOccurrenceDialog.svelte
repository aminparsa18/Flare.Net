<script lang="ts">
	// The exception-groups table row's click-through drill-down - sample occurrences
	// (service, timestamp, a link to the existing /traces/{traceId} waterfall route) plus
	// each occurrence's own stack trace. Opened by clicking a row in ExceptionGroupsTable;
	// same open-derived-from-a-shared-selection-field, onOpenChange-clears-it precedent as
	// ServiceCallBreakdownDialog reading ServicesState.selectedService - fetching itself
	// happens in ErrorsExplorerState.selectGroup, not here (unlike that dialog, which owns
	// its own fetch effect) since this page has nowhere else selectedGroup would be read
	// from.
	import * as Dialog from '$lib/components/ui/dialog';
	import * as Table from '$lib/components/ui/table';
	import * as Empty from '$lib/components/ui/empty';
	import { Spinner } from '$lib/components/ui/spinner';
	import { errorsExplorerContext } from '$lib/errors/context';
	import * as m from '$lib/paraglide/messages';

	const errors = errorsExplorerContext.get();

	const open = $derived(errors.selectedGroup !== null);

	function formatTimestamp(iso: string): string {
		return new Date(iso).toLocaleString(undefined, { hour12: false });
	}

	function handleOpenChange(next: boolean): void {
		if (!next) errors.selectGroup(null);
	}
</script>

<Dialog.Root {open} onOpenChange={handleOpenChange}>
	<Dialog.Content class="sm:max-w-3xl">
		<Dialog.Header>
			<Dialog.Title class="font-mono text-sm">{errors.selectedGroup?.exceptionType}</Dialog.Title>
			<Dialog.Description>{errors.selectedGroup?.exceptionMessage || m.exceptionGroupsTable_noMessage()}</Dialog.Description>
		</Dialog.Header>
		<div class="max-h-[60vh] overflow-auto">
			{#if errors.occurrencesLoading && !errors.occurrences}
				<div class="flex justify-center py-12">
					<Spinner class="size-6" />
				</div>
			{:else if errors.occurrencesError}
				<Empty.Root>
					<Empty.Header>
						<Empty.Title>{m.exceptionOccurrenceDialog_loadErrorTitle()}</Empty.Title>
						<Empty.Description>{errors.occurrencesError}</Empty.Description>
					</Empty.Header>
				</Empty.Root>
			{:else if errors.occurrences && errors.occurrences.occurrences.length === 0}
				<Empty.Root>
					<Empty.Header>
						<Empty.Title>{m.exceptionOccurrenceDialog_emptyTitle()}</Empty.Title>
					</Empty.Header>
				</Empty.Root>
			{:else if errors.occurrences}
				<Table.Root>
					<Table.Header>
						<Table.Row>
							<Table.Head>{m.exceptionOccurrenceDialog_serviceColumn()}</Table.Head>
							<Table.Head>{m.exceptionOccurrenceDialog_spanNameColumn()}</Table.Head>
							<Table.Head>{m.exceptionOccurrenceDialog_timestampColumn()}</Table.Head>
							<Table.Head></Table.Head>
						</Table.Row>
					</Table.Header>
					<Table.Body>
						{#each errors.occurrences.occurrences as occurrence (occurrence.traceId + occurrence.spanId)}
							<Table.Row>
								<Table.Cell class="font-medium">{occurrence.serviceName}</Table.Cell>
								<Table.Cell class="text-muted-foreground max-w-40 truncate text-xs" title={occurrence.spanName}>
									{occurrence.spanName}
								</Table.Cell>
								<Table.Cell class="text-muted-foreground text-xs whitespace-nowrap">{formatTimestamp(occurrence.timestamp)}</Table.Cell>
								<Table.Cell class="text-right">
									<a class="text-primary text-xs hover:underline" href="/traces/{occurrence.traceId}">
										{m.exceptionOccurrenceDialog_viewTrace()}
									</a>
								</Table.Cell>
							</Table.Row>
							{#if occurrence.stacktrace}
								<Table.Row>
									<Table.Cell colspan={4} class="bg-muted/30 p-0">
										<details class="px-3 py-2">
											<summary class="text-muted-foreground cursor-pointer text-xs">{m.exceptionOccurrenceDialog_showStacktrace()}</summary>
											<pre class="mt-2 max-h-64 overflow-auto font-mono text-xs whitespace-pre-wrap">{occurrence.stacktrace}</pre>
										</details>
									</Table.Cell>
								</Table.Row>
							{/if}
						{/each}
					</Table.Body>
				</Table.Root>
			{/if}
		</div>
	</Dialog.Content>
</Dialog.Root>
