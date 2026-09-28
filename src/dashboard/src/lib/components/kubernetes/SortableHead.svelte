<script lang="ts">
	// One sortable column header - the same button/chevron markup HostsTable.svelte inlines,
	// factored out because the Kubernetes page has two tables that both need it.
	import * as Table from '$lib/components/ui/table';
	import ChevronUpIcon from '@lucide/svelte/icons/chevron-up';
	import ChevronDownIcon from '@lucide/svelte/icons/chevron-down';
	import ArrowUpDownIcon from '@lucide/svelte/icons/arrow-up-down';

	interface Props {
		label: string;
		align?: 'left' | 'right';
		/** False for a plain, non-sortable header. */
		sortable?: boolean;
		active?: boolean;
		descending?: boolean;
		onSort?: () => void;
	}

	let { label, align = 'left', sortable = true, active = false, descending = false, onSort }: Props = $props();
</script>

<Table.Head class={align === 'right' ? 'text-right' : ''} aria-sort={!sortable ? undefined : active ? (descending ? 'descending' : 'ascending') : 'none'}>
	{#if !sortable}
		{label}
	{:else}
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
	{/if}
</Table.Head>
