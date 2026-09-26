<script lang="ts">
	// LogsToolbar's "Lines" control - how many lines of the Message column each LogTable
	// row shows (LogsFilterState.maxLinesPerRow). A radio dropdown rather than a free
	// number input: the value also sets VirtualList's one fixed row height, so it's kept
	// to a small closed set (MAX_LINES_PER_ROW_OPTIONS) instead of anything a user types.
	// Also hosts the Time/Message column toggles (LogsFilterState.showTimestampColumn/
	// showBodyColumn) - the other table-display preferences, kept in one menu.
	import * as DropdownMenu from '$lib/components/ui/dropdown-menu';
	import { Button } from '$lib/components/ui/button';
	import WrapTextIcon from '@lucide/svelte/icons/wrap-text';
	import { MAX_LINES_PER_ROW_OPTIONS } from '$lib/logs/state.svelte';
	import * as m from '$lib/paraglide/messages';

	let {
		lines,
		showTime,
		showBody,
		onChange,
		onColumnChange
	}: {
		lines: number;
		showTime: boolean;
		showBody: boolean;
		onChange: (lines: number) => void;
		onColumnChange: (column: 'timestamp' | 'body', visible: boolean) => void;
	} = $props();
</script>

<DropdownMenu.Root>
	<DropdownMenu.Trigger>
		{#snippet child({ props })}
			<Button {...props} variant="outline" size="sm">
				<WrapTextIcon data-icon="inline-start" />
				{m.logsToolbar_linesPerRow({ count: lines })}
			</Button>
		{/snippet}
	</DropdownMenu.Trigger>
	<DropdownMenu.Content class="w-44" align="start">
		<DropdownMenu.Label>{m.logsToolbar_linesPerRowHeading()}</DropdownMenu.Label>
		<DropdownMenu.RadioGroup value={String(lines)} onValueChange={(v) => v && onChange(Number(v))}>
			{#each MAX_LINES_PER_ROW_OPTIONS as option (option)}
				<DropdownMenu.RadioItem value={String(option)}>{option}</DropdownMenu.RadioItem>
			{/each}
		</DropdownMenu.RadioGroup>
		<DropdownMenu.Separator />
		<DropdownMenu.Label>{m.logsToolbar_columnsHeading()}</DropdownMenu.Label>
		<DropdownMenu.CheckboxItem checked={showTime} onCheckedChange={(v) => onColumnChange('timestamp', v)}>
			{m.logsTable_colTime()}
		</DropdownMenu.CheckboxItem>
		<DropdownMenu.CheckboxItem checked={showBody} onCheckedChange={(v) => onColumnChange('body', v)}>
			{m.logsTable_colMessage()}
		</DropdownMenu.CheckboxItem>
	</DropdownMenu.Content>
</DropdownMenu.Root>
