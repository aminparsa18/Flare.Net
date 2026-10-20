<script lang="ts">
	// Folds one group into another of the same exception type (ADR-0259). Saved as a Merge grouping rule, so it also
	// applies to future occurrences and shows in the grouping rules dialog, where it can be removed to split them again.
	import * as Dialog from '#lib/components/ui/dialog/index.ts';
	import { Button } from '#lib/components/ui/button/index.ts';
	import { errorsExplorerContext } from '#lib/errors/context.ts';
	import type { ExceptionGroup } from '#lib/errors-api.ts';
	import * as m from '#lib/paraglide/messages.js';

	let { open = $bindable(false), group }: { open: boolean; group: ExceptionGroup } = $props();

	const errors = errorsExplorerContext.get();

	const candidates = $derived(errors.groups.filter((g) => g.exceptionType === group.exceptionType && g.exceptionMessage !== group.exceptionMessage));

	let saving = $state(false);
	let error = $state<string | null>(null);

	$effect(() => {
		if (open) error = null;
	});

	async function mergeInto(target: ExceptionGroup): Promise<void> {
		saving = true;
		try {
			await errors.mergeGroups(group, target);
			open = false;
		} catch (err) {
			error = err instanceof Error ? err.message : String(err);
		} finally {
			saving = false;
		}
	}
</script>

<Dialog.Root bind:open>
	<Dialog.Content class="sm:max-w-xl">
		<Dialog.Header>
			<Dialog.Title>{m.errorGrouping_mergeTitle()}</Dialog.Title>
			<Dialog.Description>{m.errorGrouping_mergeDescription()}</Dialog.Description>
		</Dialog.Header>
		<p class="bg-muted/50 rounded-md px-3 py-2 font-mono text-xs break-words">{group.exceptionMessage || m.errorGrouping_emptyMessage()}</p>
		<div class="flex max-h-72 flex-col gap-1 overflow-y-auto">
			{#each candidates as target (target.exceptionMessage)}
				<button
					type="button"
					class="hover:bg-muted flex items-center justify-between gap-3 rounded-md px-3 py-2 text-left disabled:opacity-50"
					disabled={saving}
					onclick={() => mergeInto(target)}
				>
					<span class="font-mono text-xs break-words">{target.exceptionMessage || m.errorGrouping_emptyMessage()}</span>
					<span class="text-muted-foreground shrink-0 text-xs">{target.occurrenceCount.toLocaleString()}</span>
				</button>
			{:else}
				<p class="text-muted-foreground text-sm">{m.errorGrouping_mergeNone()}</p>
			{/each}
		</div>
		{#if error}<p class="text-destructive text-xs">{error}</p>{/if}
		<Dialog.Footer>
			<Button variant="ghost" onclick={() => (open = false)}>{m.errorIssue_cancel()}</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>
