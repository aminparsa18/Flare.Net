<script lang="ts">
	// Editor for the ordered ownership rules (ADR-0195): glob on a service name or exception type
	// -> owner username. First match wins; an explicit assignee always beats a rule. Edited as a
	// draft and saved as one list.
	import * as Dialog from '#lib/components/ui/dialog/index.ts';
	import * as Select from '#lib/components/ui/select/index.ts';
	import { Button } from '#lib/components/ui/button/index.ts';
	import { Input } from '#lib/components/ui/input/index.ts';
	import { Textarea } from '#lib/components/ui/textarea/index.ts';
	import ArrowUpIcon from '@lucide/svelte/icons/arrow-up';
	import ArrowDownIcon from '@lucide/svelte/icons/arrow-down';
	import TrashIcon from '@lucide/svelte/icons/trash-2';
	import PlusIcon from '@lucide/svelte/icons/plus';
	import { errorsExplorerContext } from '#lib/errors/context.ts';
	import { parseCodeowners, type OwnershipKind, type OwnershipRule } from '#lib/errors/ownership.ts';
	import * as m from '#lib/paraglide/messages.js';

	let { open = $bindable(false) }: { open: boolean } = $props();

	const errors = errorsExplorerContext.get();

	let draft = $state<OwnershipRule[]>([]);
	let saving = $state(false);
	let error = $state<string | null>(null);
	let importing = $state(false);
	let codeowners = $state('');

	$effect(() => {
		if (open) {
			draft = errors.ownershipRules.map((r) => ({ ...r }));
			error = null;
			importing = false;
			codeowners = '';
		}
	});

	const kindLabel = (kind: OwnershipKind) =>
		kind === 'Service' ? m.errorOwnership_kindService() : kind === 'Path' ? m.errorOwnership_kindPath() : m.errorOwnership_kindType();
	const placeholder = (kind: OwnershipKind) => (kind === 'Service' ? 'payments-*' : kind === 'Path' ? '/src/payments/' : '*Timeout*');

	function importCodeowners(): void {
		const imported = parseCodeowners(codeowners);
		if (imported.length === 0) {
			error = m.errorOwnership_importEmpty();
			return;
		}
		draft = [...draft, ...imported];
		codeowners = '';
		importing = false;
		error = null;
	}

	function move(index: number, by: -1 | 1): void {
		const target = index + by;
		if (target < 0 || target >= draft.length) return;
		const next = [...draft];
		[next[index], next[target]] = [next[target], next[index]];
		draft = next;
	}

	async function save(): Promise<void> {
		const rules = draft.map((r) => ({ ...r, pattern: r.pattern.trim(), owner: r.owner.trim() })).filter((r) => r.pattern || r.owner);
		if (rules.some((r) => !r.pattern || !r.owner)) {
			error = m.errorOwnership_incomplete();
			return;
		}
		saving = true;
		try {
			await errors.saveOwnershipRules(rules);
			open = false;
		} catch (err) {
			error = err instanceof Error ? err.message : String(err);
		} finally {
			saving = false;
		}
	}
</script>

<Dialog.Root bind:open>
	<Dialog.Content class="sm:max-w-2xl">
		<Dialog.Header>
			<Dialog.Title>{m.errorOwnership_title()}</Dialog.Title>
			<Dialog.Description>{m.errorOwnership_description()}</Dialog.Description>
		</Dialog.Header>
		<div class="flex max-h-96 flex-col gap-2 overflow-y-auto">
			{#each draft as rule, i (i)}
				<div class="flex items-center gap-2">
					<Select.Root type="single" value={rule.kind} onValueChange={(v) => v && (rule.kind = v as OwnershipKind)}>
						<Select.Trigger class="w-40">{kindLabel(rule.kind)}</Select.Trigger>
						<Select.Content>
							<Select.Item value="Service" label={m.errorOwnership_kindService()} />
							<Select.Item value="ExceptionType" label={m.errorOwnership_kindType()} />
							<Select.Item value="Path" label={m.errorOwnership_kindPath()} />
						</Select.Content>
					</Select.Root>
					<Input class="font-mono" bind:value={rule.pattern} placeholder={placeholder(rule.kind)} aria-label={m.errorOwnership_pattern()} />
					<Input class="w-40" bind:value={rule.owner} placeholder={m.errorOwnership_ownerPlaceholder()} aria-label={m.errorOwnership_owner()} />
					<Button variant="ghost" size="icon-sm" onclick={() => move(i, -1)} disabled={i === 0} aria-label={m.errorOwnership_moveUp()}><ArrowUpIcon /></Button>
					<Button variant="ghost" size="icon-sm" onclick={() => move(i, 1)} disabled={i === draft.length - 1} aria-label={m.errorOwnership_moveDown()}><ArrowDownIcon /></Button>
					<Button variant="ghost" size="icon-sm" onclick={() => (draft = draft.filter((_, j) => j !== i))} aria-label={m.errorOwnership_remove()}><TrashIcon /></Button>
				</div>
			{:else}
				<p class="text-muted-foreground text-sm">{m.errorOwnership_empty()}</p>
			{/each}
		</div>
		{#if importing}
			<div class="flex flex-col gap-2">
				<p class="text-muted-foreground text-xs">{m.errorOwnership_importHint()}</p>
				<Textarea bind:value={codeowners} rows={6} class="font-mono text-xs" placeholder="/src/payments/ @alice" aria-label={m.errorOwnership_import()} />
				<div class="flex gap-2">
					<Button size="sm" onclick={importCodeowners}>{m.errorOwnership_importApply()}</Button>
					<Button size="sm" variant="ghost" onclick={() => (importing = false)}>{m.errorIssue_cancel()}</Button>
				</div>
			</div>
		{/if}
		{#if error}<p class="text-destructive text-xs">{error}</p>{/if}
		<Dialog.Footer class="sm:justify-between">
			<div class="flex gap-2">
				<Button variant="outline" size="sm" onclick={() => (draft = [...draft, { kind: 'Service', pattern: '', owner: '' }])}>
					<PlusIcon data-icon="inline-start" />
					{m.errorOwnership_add()}
				</Button>
				<Button variant="outline" size="sm" onclick={() => (importing = !importing)}>{m.errorOwnership_import()}</Button>
			</div>
			<div class="flex gap-2">
				<Button variant="ghost" onclick={() => (open = false)}>{m.errorIssue_cancel()}</Button>
				<Button onclick={save} disabled={saving}>{m.errorIssue_save()}</Button>
			</div>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>
