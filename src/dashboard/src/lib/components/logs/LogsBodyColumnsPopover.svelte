<script lang="ts">
	// LogsToolbar's "Fields" control - JSON body paths (same dotted syntax as BodyJsonFilter,
	// e.g. `order.id`) shown as extra LogTable columns (LogsFilterState.bodyColumns). Applied
	// immediately on add/remove; a display preference, so no re-search.
	import * as Popover from '$lib/components/ui/popover';
	import { Button } from '$lib/components/ui/button';
	import { Input } from '$lib/components/ui/input';
	import BracesIcon from '@lucide/svelte/icons/braces';
	import XIcon from '@lucide/svelte/icons/x';
	import { MAX_BODY_COLUMNS } from '$lib/logs/state.svelte';
	import * as m from '$lib/paraglide/messages';

	let { paths, onApply }: { paths: string[]; onApply: (paths: string[]) => void } = $props();

	let draft = $state('');
	const atLimit = $derived(paths.length >= MAX_BODY_COLUMNS);

	function add() {
		const path = draft.trim();
		if (!path || atLimit) return;
		if (!paths.includes(path)) onApply([...paths, path]);
		draft = '';
	}
</script>

<Popover.Root>
	<Popover.Trigger>
		{#snippet child({ props })}
			<Button {...props} variant="outline" size="sm">
				<BracesIcon data-icon="inline-start" />
				{paths.length > 0 ? m.logsBodyColumns_triggerCount({ count: paths.length }) : m.logsBodyColumns_trigger()}
			</Button>
		{/snippet}
	</Popover.Trigger>
	<Popover.Content class="w-72" align="start">
		<div class="flex flex-col gap-3">
			<p class="text-muted-foreground text-xs">{m.logsBodyColumns_description()}</p>
			<form
				class="flex gap-2"
				onsubmit={(e) => {
					e.preventDefault();
					add();
				}}
			>
				<Input bind:value={draft} placeholder={m.logsBodyColumns_placeholder()} disabled={atLimit} class="h-8 font-mono text-xs" />
				<Button type="submit" size="sm" disabled={atLimit || !draft.trim()}>{m.logsBodyColumns_add()}</Button>
			</form>
			{#if atLimit}
				<p class="text-muted-foreground text-xs">{m.logsBodyColumns_limit({ max: MAX_BODY_COLUMNS })}</p>
			{/if}
			{#each paths as path (path)}
				<div class="flex items-center justify-between gap-2 font-mono text-xs">
					<span class="truncate">{path}</span>
					<button
						type="button"
						class="hover:text-foreground text-muted-foreground shrink-0"
						aria-label={m.logsBodyColumns_remove({ path })}
						onclick={() => onApply(paths.filter((p) => p !== path))}
					>
						<XIcon class="size-3" />
					</button>
				</div>
			{/each}
		</div>
	</Popover.Content>
</Popover.Root>
