<script lang="ts">
	// A sortable table header cell - the same button/chevron markup MessagingTable inlines,
	// shared here because the External APIs page has two sortable tables.
	import * as Table from '$lib/components/ui/table';
	import ChevronUpIcon from '@lucide/svelte/icons/chevron-up';
	import ChevronDownIcon from '@lucide/svelte/icons/chevron-down';
	import ArrowUpDownIcon from '@lucide/svelte/icons/arrow-up-down';

	interface Props {
		label: string;
		align?: 'left' | 'right';
		active: boolean;
		descending: boolean;
		onSort: () => void;
	}

	let { label, align = 'left', active, descending, onSort }: Props = $props();
</script>

<Table.Head class={align === 'right' ? 'text-right' : ''} aria-sort={active ? (descending ? 'descending' : 'ascending') : 'none'}>
	<button
		type="button"
		class="hover:text-foreground inline-flex items-center gap-1 {align === 'right' ? 'flex-row-reverse' : ''} {active ? 'text-foreground' : ''}"
		onclick={onSort}
	>
		{label}
		{#if active}
			{#if descending}
				<ChevronDownIcon class="size-3" />
			{:else}
				<ChevronUpIcon class="size-3" />
			{/if}
		{:else}
			<ArrowUpDownIcon class="text-muted-foreground/50 size-3" />
		{/if}
	</button>
</Table.Head>
