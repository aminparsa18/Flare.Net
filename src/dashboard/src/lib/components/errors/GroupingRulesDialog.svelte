<script lang="ts">
	// Editor for how exceptions are grouped (ADR-0258, ADR-0259): the built-in normalization switch, ordered
	// regular-expression replacements, and the merges created from a group's row menu. Edited as a draft and
	// saved as one document; the server re-keys triage state, so saving re-runs the search.
	import * as Dialog from '#lib/components/ui/dialog/index.ts';
	import { Button } from '#lib/components/ui/button/index.ts';
	import { Badge } from '#lib/components/ui/badge/index.ts';
	import { Input } from '#lib/components/ui/input/index.ts';
	import { Switch } from '#lib/components/ui/switch/index.ts';
	import ArrowUpIcon from '@lucide/svelte/icons/arrow-up';
	import ArrowDownIcon from '@lucide/svelte/icons/arrow-down';
	import TrashIcon from '@lucide/svelte/icons/trash-2';
	import PlusIcon from '@lucide/svelte/icons/plus';
	import { errorsExplorerContext } from '#lib/errors/context.ts';
	import type { ErrorGroupingRule } from '#lib/error-grouping-api.ts';
	import * as m from '#lib/paraglide/messages.js';

	let { open = $bindable(false) }: { open: boolean } = $props();

	const errors = errorsExplorerContext.get();

	let normalize = $state(true);
	let draft = $state<ErrorGroupingRule[]>([]);
	let saving = $state(false);
	let error = $state<string | null>(null);

	$effect(() => {
		if (open) {
			normalize = errors.grouping.normalizeMessages;
			draft = errors.grouping.rules.map((r) => ({ ...r }));
			error = null;
		}
	});

	function move(index: number, by: -1 | 1): void {
		const target = index + by;
		if (target < 0 || target >= draft.length) return;
		const next = [...draft];
		[next[index], next[target]] = [next[target], next[index]];
		draft = next;
	}

	async function save(): Promise<void> {
		if (draft.some((r) => !r.pattern || (r.kind === 'Merge' && (!r.exceptionType || !r.replacement)))) {
			error = m.errorGrouping_incomplete();
			return;
		}
		saving = true;
		try {
			await errors.saveGrouping({ normalizeMessages: normalize, rules: draft });
			open = false;
		} catch (err) {
			error = err instanceof Error ? err.message : String(err);
		} finally {
			saving = false;
		}
	}
</script>

<Dialog.Root bind:open>
	<Dialog.Content class="sm:max-w-3xl">
		<Dialog.Header>
			<Dialog.Title>{m.errorGrouping_title()}</Dialog.Title>
			<Dialog.Description>{m.errorGrouping_description()}</Dialog.Description>
		</Dialog.Header>

		<label class="flex items-start gap-3 text-sm">
			<Switch checked={normalize} onCheckedChange={(v) => (normalize = v)} class="mt-0.5" />
			<span>
				<span class="font-medium">{m.errorGrouping_normalize()}</span>
				<span class="text-muted-foreground block text-xs">{m.errorGrouping_normalizeHint()}</span>
			</span>
		</label>

		<div class="flex max-h-80 flex-col gap-2 overflow-y-auto">
			{#each draft as rule, i (i)}
				<div class="flex items-center gap-2">
					{#if rule.kind === 'Replace'}
						<Badge variant="secondary" class="w-16 justify-center">{m.errorGrouping_kindReplace()}</Badge>
						<Input class="font-mono" bind:value={rule.pattern} placeholder="user [a-z]+" aria-label={m.errorGrouping_pattern()} />
						<Input class="font-mono" bind:value={rule.replacement} placeholder="user <name>" aria-label={m.errorGrouping_replacement()} />
					{:else}
						<Badge variant="secondary" class="w-16 justify-center">{m.errorGrouping_kindMerge()}</Badge>
						<Input class="w-48 font-mono" bind:value={rule.exceptionType} aria-label={m.errorGrouping_exceptionType()} />
						<Input class="font-mono" bind:value={rule.pattern} aria-label={m.errorGrouping_mergeFrom()} />
						<Input class="font-mono" bind:value={rule.replacement} aria-label={m.errorGrouping_mergeTo()} />
					{/if}
					<Button variant="ghost" size="icon-sm" onclick={() => move(i, -1)} disabled={i === 0} aria-label={m.errorOwnership_moveUp()}><ArrowUpIcon /></Button>
					<Button variant="ghost" size="icon-sm" onclick={() => move(i, 1)} disabled={i === draft.length - 1} aria-label={m.errorOwnership_moveDown()}><ArrowDownIcon /></Button>
					<Button variant="ghost" size="icon-sm" onclick={() => (draft = draft.filter((_, j) => j !== i))} aria-label={m.errorOwnership_remove()}><TrashIcon /></Button>
				</div>
			{:else}
				<p class="text-muted-foreground text-sm">{m.errorGrouping_empty()}</p>
			{/each}
		</div>
		{#if error}<p class="text-destructive text-xs">{error}</p>{/if}
		<Dialog.Footer class="sm:justify-between">
			<Button variant="outline" size="sm" onclick={() => (draft = [...draft, { kind: 'Replace', exceptionType: '', pattern: '', replacement: '' }])}>
				<PlusIcon data-icon="inline-start" />
				{m.errorGrouping_add()}
			</Button>
			<div class="flex gap-2">
				<Button variant="ghost" onclick={() => (open = false)}>{m.errorIssue_cancel()}</Button>
				<Button onclick={save} disabled={saving}>{m.errorIssue_save()}</Button>
			</div>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>
