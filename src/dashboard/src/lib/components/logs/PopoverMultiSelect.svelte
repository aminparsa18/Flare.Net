<script lang="ts" module>
	export interface MultiSelectOption {
		value: string;
		label: string;
	}
</script>

<script lang="ts">
	import * as Popover from '$lib/components/ui/popover';
	import * as Command from '$lib/components/ui/command';
	import { Button } from '$lib/components/ui/button';
	import { Checkbox } from '$lib/components/ui/checkbox';
	import { ScrollArea } from '$lib/components/ui/scroll-area';
	import ChevronDownIcon from '@lucide/svelte/icons/chevron-down';

	let {
		label,
		options,
		selected,
		onChange
	}: {
		label: string;
		options: MultiSelectOption[];
		selected: string[];
		onChange: (next: string[]) => void;
	} = $props();

	function toggle(value: string) {
		onChange(selected.includes(value) ? selected.filter((v) => v !== value) : [...selected, value]);
	}

	const buttonLabel = $derived(selected.length === 0 ? label : `${label} (${selected.length})`);
</script>

<Popover.Root>
	<Popover.Trigger>
		{#snippet child({ props })}
			<Button {...props} variant="outline" size="sm">
				{buttonLabel}
				<ChevronDownIcon data-icon="inline-end" />
			</Button>
		{/snippet}
	</Popover.Trigger>
	<Popover.Content class="w-56 p-0" align="start">
		<Command.Root>
			<Command.Input placeholder="Filter {label.toLowerCase()}..." />
			<Command.List>
				<Command.Empty>No results.</Command.Empty>
				<Command.Group>
					<ScrollArea class="h-64">
						{#each options as option (option.value)}
							<Command.Item value={option.label} onSelect={() => toggle(option.value)}>
								<Checkbox checked={selected.includes(option.value)} />
								<span>{option.label}</span>
							</Command.Item>
						{/each}
					</ScrollArea>
				</Command.Group>
			</Command.List>
		</Command.Root>
	</Popover.Content>
</Popover.Root>
