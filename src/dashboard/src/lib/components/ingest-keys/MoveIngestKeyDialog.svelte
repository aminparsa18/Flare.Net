<script lang="ts">
	// Moves an ingest key to another project (or back to instance-wide). Ownership metadata only:
	// what the key may ingest doesn't change (ADR-0123).
	import * as Dialog from '$lib/components/ui/dialog';
	import { Button } from '$lib/components/ui/button';
	import { Spinner } from '$lib/components/ui/spinner';
	import ProjectPicker from '$lib/components/projects/ProjectPicker.svelte';
	import { ingestKeysContext } from '$lib/ingest-keys/context';
	import * as m from '$lib/paraglide/messages';

	const keys = ingestKeysContext.get();

	let projectId = $state<string | null>(null);

	$effect(() => {
		if (keys.moveTarget) projectId = keys.moveTarget.projectId;
	});

	function handleOpenChange(next: boolean): void {
		if (keys.saving) return;
		if (!next) keys.closeMove();
	}
</script>

<Dialog.Root open={keys.moveTarget != null} onOpenChange={handleOpenChange}>
	<Dialog.Content class="sm:max-w-md">
		<Dialog.Header>
			<Dialog.Title>{m.moveIngestKeyDialog_title({ name: keys.moveTarget?.name ?? '' })}</Dialog.Title>
			<Dialog.Description>{m.moveIngestKeyDialog_description()}</Dialog.Description>
		</Dialog.Header>
		<ProjectPicker bind:value={projectId} includeAll />
		{#if keys.saveError}
			<p class="text-destructive text-sm">{keys.saveError}</p>
		{/if}
		<Dialog.Footer>
			<Button type="button" variant="outline" onclick={() => keys.closeMove()}>{m.moveIngestKeyDialog_cancel()}</Button>
			<Button type="button" disabled={keys.saving} onclick={() => keys.saveMove(projectId)}>
				{#if keys.saving}<Spinner class="size-4" />{/if}
				{m.moveIngestKeyDialog_save()}
			</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>
