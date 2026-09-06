<script lang="ts">
	import { onMount } from 'svelte';
	import * as Dialog from '$lib/components/ui/dialog';
	import * as Table from '$lib/components/ui/table';
	import * as Empty from '$lib/components/ui/empty';
	import { Button } from '$lib/components/ui/button';
	import { Spinner } from '$lib/components/ui/spinner';
	import RegexIcon from '@lucide/svelte/icons/regex';
	import { getPatterns, type LogPatternRow } from '$lib/api';
	import { logsExplorerContext } from '$lib/logs/context';
	import { cn } from '$lib/utils';
	import * as m from '$lib/paraglide/messages';

	// Embedded in the Logs page's toolbar (an icon-button trigger next to the search box)
	// rather than a standalone `/patterns` route - patterns are always "patterns within
	// what I'm currently looking at" (see LogsExplorerState.currentRange's remarks), so a
	// page switch away from the Logs Explorer's own filter context would be the wrong
	// shape for this. "View occurrences" closes the modal and hands off to the same
	// applyPatternIdFilter drill-down the standalone-route version used, just called
	// directly instead of round-tripping through a URL.
	let { onSelectPattern }: { onSelectPattern: (patternId: string, template: string) => void } = $props();

	const explorer = logsExplorerContext.get();

	let open = $state(false);
	let patterns = $state.raw<LogPatternRow[]>([]);
	let loading = $state(true);
	let error = $state<string | null>(null);

	const compactNumber = new Intl.NumberFormat(undefined, { notation: 'compact', maximumFractionDigits: 1 });

	function formatTimestamp(iso: string): string {
		return new Date(iso).toLocaleString(undefined, { hour12: false });
	}

	// Dialog.Content is portalled and unmounted while closed (bits-ui doesn't keep closed
	// content in the tree) - this component only exists in the DOM while open, so onMount
	// firing here is exactly "fetch fresh data every time the modal opens", no separate
	// effect watching `open` needed.
	onMount(() => {
		void load();
	});

	async function load(): Promise<void> {
		loading = true;
		error = null;
		try {
			const res = await getPatterns({ filter: explorer.buildFilter(explorer.currentRange()) });
			patterns = res.patterns;
		} catch (err) {
			error = err instanceof Error ? err.message : String(err);
		} finally {
			loading = false;
		}
	}

	function selectPattern(row: LogPatternRow): void {
		open = false;
		onSelectPattern(row.patternId, row.template);
	}
</script>

<Dialog.Root bind:open>
	<Dialog.Trigger>
		{#snippet child({ props })}
			<Button {...props} variant="outline" size="icon-sm" title={m.patterns_trigger()}>
				<RegexIcon />
			</Button>
		{/snippet}
	</Dialog.Trigger>
	<Dialog.Content class="sm:max-w-3xl">
		<Dialog.Header>
			<Dialog.Title>{m.patterns_trigger()}</Dialog.Title>
			<Dialog.Description>
				{m.patterns_description()}
			</Dialog.Description>
		</Dialog.Header>
		<div class="max-h-[60vh] overflow-auto">
			{#if loading && patterns.length === 0}
				<div class="flex justify-center py-12">
					<Spinner class="size-6" />
				</div>
			{:else if error}
				<Empty.Root>
					<Empty.Header>
						<Empty.Title>{m.patterns_loadError()}</Empty.Title>
						<Empty.Description>{error}</Empty.Description>
					</Empty.Header>
				</Empty.Root>
			{:else if patterns.length === 0}
				<Empty.Root>
					<Empty.Header>
						<Empty.Title>{m.patterns_emptyTitle()}</Empty.Title>
						<Empty.Description>
							{m.patterns_emptyDescription()}
						</Empty.Description>
					</Empty.Header>
				</Empty.Root>
			{:else}
				<Table.Root>
					<Table.Header>
						<Table.Row>
							<Table.Head>{m.patterns_colTemplate()}</Table.Head>
							<Table.Head class="text-right">{m.patterns_colCount()}</Table.Head>
							<Table.Head class="text-right">{m.patterns_colErrors()}</Table.Head>
							<Table.Head>{m.patterns_colFirstSeen()}</Table.Head>
							<Table.Head>{m.patterns_colLastSeen()}</Table.Head>
							<Table.Head></Table.Head>
						</Table.Row>
					</Table.Header>
					<Table.Body>
						{#each patterns as row (row.patternId)}
							<Table.Row>
								<Table.Cell class="max-w-md truncate font-mono text-xs" title={row.template}>{row.template}</Table.Cell>
								<Table.Cell class="text-right tabular-nums">{compactNumber.format(row.count)}</Table.Cell>
								<Table.Cell class={cn('text-right tabular-nums', row.errorCount > 0 && 'text-destructive')}>
									{compactNumber.format(row.errorCount)}
								</Table.Cell>
								<Table.Cell class="text-muted-foreground text-xs whitespace-nowrap">{formatTimestamp(row.firstSeen)}</Table.Cell>
								<Table.Cell class="text-muted-foreground text-xs whitespace-nowrap">{formatTimestamp(row.lastSeen)}</Table.Cell>
								<Table.Cell class="text-right">
									<Button variant="ghost" size="sm" onclick={() => selectPattern(row)}>{m.patterns_viewOccurrences()}</Button>
								</Table.Cell>
							</Table.Row>
						{/each}
					</Table.Body>
				</Table.Root>
			{/if}
		</div>
	</Dialog.Content>
</Dialog.Root>