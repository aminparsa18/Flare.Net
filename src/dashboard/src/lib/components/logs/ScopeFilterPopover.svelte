<script lang="ts">
	import * as Popover from '$lib/components/ui/popover';
	import * as Command from '$lib/components/ui/command';
	import { Button } from '$lib/components/ui/button';
	import { Checkbox } from '$lib/components/ui/checkbox';
	import { ScrollArea } from '$lib/components/ui/scroll-area';
	import ChevronDownIcon from '@lucide/svelte/icons/chevron-down';
	import PlusIcon from '@lucide/svelte/icons/plus';
	import { logsExplorerContext } from '$lib/logs/context';
	import * as m from '$lib/paraglide/messages';

	// PopoverMultiSelect's shape, plus free-text entry: a scope filter entry can be a
	// prefix pattern (`Microsoft.EntityFrameworkCore.*`) that isn't itself an observed
	// scope name, so the typed text is offered as its own selectable item. Filtering is
	// done here (shouldFilter={false}) so that item always shows while text is typed.
	const explorer = logsExplorerContext.get();

	let query = $state('');

	const selected = $derived(explorer.filter.scopeNames);
	const trimmedQuery = $derived(query.trim());
	// Selected entries first (so a typed prefix pattern stays visible and removable),
	// then every other known scope, narrowed by the typed text.
	const options = $derived(
		[...selected, ...explorer.knownScopes.filter((s) => !selected.includes(s))].filter((s) =>
			s.toLowerCase().includes(trimmedQuery.toLowerCase())
		)
	);
	const canAddQuery = $derived(trimmedQuery !== '' && !selected.includes(trimmedQuery));

	function toggle(value: string) {
		explorer.setScopeNames(selected.includes(value) ? selected.filter((v) => v !== value) : [...selected, value]);
	}

	function addQuery() {
		if (!canAddQuery) return;
		explorer.setScopeNames([...selected, trimmedQuery]);
		query = '';
	}

	function handleOpenChange(open: boolean) {
		if (open) void explorer.loadKnownScopes();
		else query = '';
	}

	const buttonLabel = $derived(
		selected.length === 0 ? m.logsToolbar_scopeLabel() : `${m.logsToolbar_scopeLabel()} (${selected.length})`
	);
</script>

<Popover.Root onOpenChange={handleOpenChange}>
	<Popover.Trigger>
		{#snippet child({ props })}
			<Button {...props} variant="outline" size="sm">
				{buttonLabel}
				<ChevronDownIcon data-icon="inline-end" />
			</Button>
		{/snippet}
	</Popover.Trigger>
	<Popover.Content class="w-80 p-0" align="start">
		<Command.Root shouldFilter={false}>
			<Command.Input placeholder={m.scopeFilter_placeholder()} bind:value={query} />
			<Command.List>
				{#if !canAddQuery && options.length === 0}
					<p class="text-muted-foreground px-2 py-4 text-center text-xs">{m.multiSelect_noResults()}</p>
				{/if}
				<Command.Group>
					{#if canAddQuery}
						<Command.Item value={`add:${trimmedQuery}`} onSelect={addQuery}>
							<PlusIcon />
							<span class="truncate">{m.scopeFilter_add({ value: trimmedQuery })}</span>
						</Command.Item>
					{/if}
					<ScrollArea class={options.length > 8 ? 'h-64' : ''}>
						{#each options as option (option)}
							<Command.Item value={option} onSelect={() => toggle(option)}>
								<Checkbox checked={selected.includes(option)} />
								<span class="truncate font-mono" title={option}>{option}</span>
							</Command.Item>
						{/each}
					</ScrollArea>
				</Command.Group>
			</Command.List>
			<p class="text-muted-foreground border-t px-3 py-2 text-xs">{m.scopeFilter_hint()}</p>
		</Command.Root>
	</Popover.Content>
</Popover.Root>
