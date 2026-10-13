<script lang="ts">
	// Attach (or replace) the tracker issue a group is linked to - a plain URL, validated
	// server-side as http(s) (ADR-0195).
	import * as Dialog from '#lib/components/ui/dialog/index.ts';
	import { Button } from '#lib/components/ui/button/index.ts';
	import { Input } from '#lib/components/ui/input/index.ts';
	import * as m from '#lib/paraglide/messages.js';

	let { open = $bindable(false), current, onSave }: { open: boolean; current: string; onSave: (url: string) => Promise<void> } = $props();

	let url = $state('');
	let saving = $state(false);
	let error = $state<string | null>(null);

	$effect(() => {
		if (open) {
			url = current;
			error = null;
		}
	});

	async function save(): Promise<void> {
		const trimmed = url.trim();
		if (!/^https?:\/\/\S+$/i.test(trimmed)) {
			error = m.errorIssue_linkInvalid();
			return;
		}
		saving = true;
		try {
			await onSave(trimmed);
			open = false;
		} catch (err) {
			error = err instanceof Error ? err.message : String(err);
		} finally {
			saving = false;
		}
	}
</script>

<Dialog.Root bind:open>
	<Dialog.Content class="sm:max-w-md" onclick={(e: MouseEvent) => e.stopPropagation()}>
		<Dialog.Header>
			<Dialog.Title>{m.errorIssue_linkTitle()}</Dialog.Title>
			<Dialog.Description>{m.errorIssue_linkDescription()}</Dialog.Description>
		</Dialog.Header>
		<form
			class="flex flex-col gap-3"
			onsubmit={(e) => {
				e.preventDefault();
				void save();
			}}
		>
			<Input bind:value={url} placeholder="https://github.com/acme/shop/issues/42" aria-label={m.errorIssue_linkTitle()} />
			{#if error}<p class="text-destructive text-xs">{error}</p>{/if}
			<Dialog.Footer>
				<Button type="button" variant="ghost" onclick={() => (open = false)}>{m.errorIssue_cancel()}</Button>
				<Button type="submit" disabled={saving}>{m.errorIssue_save()}</Button>
			</Dialog.Footer>
		</form>
	</Dialog.Content>
</Dialog.Root>
