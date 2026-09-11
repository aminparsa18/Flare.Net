<script lang="ts" module>
	export interface SingleSelectOption {
		value: string;
		label: string;
	}
</script>

<script lang="ts">
	// Single-select sibling of PopoverMultiSelect.svelte (this directory) - same
	// Popover+Command shell, reused verbatim rather than teaching that component a
	// single-select mode, since the two differ in every behavior that matters (checkbox
	// vs. plain item, toggle-in-place vs. select-and-close, array vs. scalar value).
	// First consumer is MetricsToolbar's groupBy-attribute-key picker (roadmap: that
	// picker had no search/autocomplete, unlike Flare's existing attribute *filter
	// value* autocomplete - see AttributeValueCombobox.svelte - even though the
	// underlying option list here is a small closed, already-loaded array
	// (knownAttributeKeys), not a server-fetched one; Command's client-side substring
	// filter is all that's needed).
	import * as Popover from '$lib/components/ui/popover';
	import * as Command from '$lib/components/ui/command';
	import { Button } from '$lib/components/ui/button';
	import { ScrollArea } from '$lib/components/ui/scroll-area';
	import ChevronDownIcon from '@lucide/svelte/icons/chevron-down';
	import { cn } from '$lib/utils';
	import * as m from '$lib/paraglide/messages';

	let {
		label,
		triggerLabel,
		options,
		value,
		onChange
	}: {
		/** Lowercased into the filter input's placeholder - not shown anywhere else, the button itself shows `triggerLabel`. */
		label: string;
		/** What the trigger button displays - callers format this themselves (e.g. "Group by: host") rather than this component deriving it from `value`/`options`. */
		triggerLabel: string;
		options: SingleSelectOption[];
		value: string;
		onChange: (next: string) => void;
	} = $props();

	let open = $state(false);

	function pick(v: string): void {
		onChange(v);
		open = false;
	}
</script>

<Popover.Root bind:open>
	<Popover.Trigger>
		{#snippet child({ props })}
			<Button {...props} variant="outline" size="sm">
				{triggerLabel}
				<ChevronDownIcon data-icon="inline-end" />
			</Button>
		{/snippet}
	</Popover.Trigger>
	<Popover.Content class="w-56 p-0" align="start">
		<Command.Root>
			<Command.Input placeholder={m.multiSelect_filterPlaceholder({ label: label.toLowerCase() })} />
			<Command.List>
				<Command.Empty>{m.multiSelect_noResults()}</Command.Empty>
				<Command.Group>
					<ScrollArea class="h-64">
						{#each options as option (option.value)}
							<Command.Item
								value={option.label}
								onSelect={() => pick(option.value)}
								class={cn(option.value === value && 'bg-accent text-accent-foreground')}
							>
								<span class="truncate">{option.label}</span>
							</Command.Item>
						{/each}
					</ScrollArea>
				</Command.Group>
			</Command.List>
		</Command.Root>
	</Popover.Content>
</Popover.Root>
