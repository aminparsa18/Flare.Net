<script lang="ts">
	import * as Table from '$lib/components/ui/table';
	import * as Empty from '$lib/components/ui/empty';
	import { Spinner } from '$lib/components/ui/spinner';
	import ChevronUpIcon from '@lucide/svelte/icons/chevron-up';
	import ChevronDownIcon from '@lucide/svelte/icons/chevron-down';
	import ArrowUpDownIcon from '@lucide/svelte/icons/arrow-up-down';
	import BugIcon from '@lucide/svelte/icons/bug';
	import { errorsExplorerContext } from '$lib/errors/context';
	import type { ErrorsSortColumn } from '$lib/errors/state.svelte';
	import * as m from '$lib/paraglide/messages';

	const errors = errorsExplorerContext.get();

	const compactNumber = new Intl.NumberFormat(undefined, { notation: 'compact', maximumFractionDigits: 1 });

	// Same local, component-only timestamp formatter as PatternsModal.svelte/
	// SpanDetailSheet.svelte - not shared, it's a one-line wrapper.
	function formatTimestamp(iso: string): string {
		return new Date(iso).toLocaleString(undefined, { hour12: false });
	}

	interface ColumnDef {
		column: ErrorsSortColumn;
		label: string;
		align: 'left' | 'right';
	}

	const columns = $derived<ColumnDef[]>([
		{ column: 'exceptionType', label: m.exceptionGroupsTable_typeColumn(), align: 'left' },
		{ column: 'exceptionMessage', label: m.exceptionGroupsTable_messageColumn(), align: 'left' },
		{ column: 'occurrenceCount', label: m.exceptionGroupsTable_occurrencesColumn(), align: 'right' },
		{ column: 'affectedServices', label: m.exceptionGroupsTable_servicesColumn(), align: 'right' },
		{ column: 'firstSeen', label: m.exceptionGroupsTable_firstSeenColumn(), align: 'left' },
		{ column: 'lastSeen', label: m.exceptionGroupsTable_lastSeenColumn(), align: 'left' }
	]);
</script>

<div class="px-4 pb-4">
	{#if errors.loading && errors.groups.length === 0}
		<div class="flex h-32 items-center justify-center">
			<Spinner />
		</div>
	{:else if errors.error}
		<Empty.Root>
			<Empty.Header>
				<Empty.Media variant="icon"><BugIcon /></Empty.Media>
				<Empty.Title>{m.exceptionGroupsTable_loadErrorTitle()}</Empty.Title>
				<Empty.Description>{errors.error}</Empty.Description>
			</Empty.Header>
		</Empty.Root>
	{:else if errors.visibleGroups().length === 0}
		<Empty.Root>
			<Empty.Header>
				<Empty.Media variant="icon"><BugIcon /></Empty.Media>
				<Empty.Title>{m.exceptionGroupsTable_emptyTitle()}</Empty.Title>
				<Empty.Description>{m.exceptionGroupsTable_emptyDescription()}</Empty.Description>
			</Empty.Header>
		</Empty.Root>
	{:else}
		<Table.Root>
			<Table.Header>
				<Table.Row>
					{#each columns as col (col.column)}
						{@const active = errors.sortColumn === col.column}
						<Table.Head
							class={col.align === 'right' ? 'text-right' : ''}
							aria-sort={active ? (errors.sortDescending ? 'descending' : 'ascending') : 'none'}
						>
							<button
								type="button"
								class="hover:text-foreground inline-flex items-center gap-1 {col.align === 'right' ? 'flex-row-reverse' : ''} {active
									? 'text-foreground'
									: ''}"
								onclick={() => errors.setSort(col.column)}
							>
								{col.label}
								{#if active}
									{#if errors.sortDescending}
										<ChevronDownIcon class="size-3" />
									{:else}
										<ChevronUpIcon class="size-3" />
									{/if}
								{:else}
									<ArrowUpDownIcon class="text-muted-foreground/50 size-3" />
								{/if}
							</button>
						</Table.Head>
					{/each}
				</Table.Row>
			</Table.Header>
			<Table.Body>
				{#each errors.sorted() as group (group.exceptionType + ' ' + group.exceptionMessage)}
					<Table.Row class="cursor-pointer" onclick={() => errors.selectGroup(group)}>
						<Table.Cell class="max-w-xs truncate font-mono text-xs" title={group.exceptionType}>{group.exceptionType}</Table.Cell>
						<Table.Cell class="text-muted-foreground max-w-md truncate text-xs" title={group.exceptionMessage}>
							{group.exceptionMessage || m.exceptionGroupsTable_noMessage()}
						</Table.Cell>
						<Table.Cell class="text-right tabular-nums">{compactNumber.format(group.occurrenceCount)}</Table.Cell>
						<Table.Cell class="text-right tabular-nums">{group.affectedServices.length}</Table.Cell>
						<Table.Cell class="text-muted-foreground text-xs whitespace-nowrap">{formatTimestamp(group.firstSeen)}</Table.Cell>
						<Table.Cell class="text-muted-foreground text-xs whitespace-nowrap">{formatTimestamp(group.lastSeen)}</Table.Cell>
					</Table.Row>
				{/each}
			</Table.Body>
		</Table.Root>
	{/if}
</div>
