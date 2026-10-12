<script lang="ts">
	// Edits one key's browser-access allowlists: the origins it may be used from (ADR-0149)
	// and the service.name values it may write as (ADR-0150). One entry per line; an empty
	// box lifts that restriction. The API normalizes origins and rejects malformed ones, so
	// its message is shown as-is.
	import * as Dialog from '$lib/components/ui/dialog';
	import { Button } from '$lib/components/ui/button';
	import { Textarea } from '$lib/components/ui/textarea';
	import { Spinner } from '$lib/components/ui/spinner';
	import { ingestKeysContext } from '$lib/ingest-keys/context';
	import * as m from '$lib/paraglide/messages';

	const keys = ingestKeysContext.get();

	let origins = $state('');
	let services = $state('');

	$effect(() => {
		const target = keys.accessTarget;
		if (target) {
			origins = target.allowedOrigins.join('\n');
			services = target.allowedServices.join('\n');
		}
	});

	const lines = (text: string): string[] => [...new Set(text.split('\n').map((l) => l.trim()).filter(Boolean))];

	function handleOpenChange(next: boolean): void {
		if (keys.saving) return;
		if (!next) keys.closeAccess();
	}

	async function handleSubmit(event: SubmitEvent): Promise<void> {
		event.preventDefault();
		await keys.saveAccess(lines(origins), lines(services));
	}
</script>

<Dialog.Root open={keys.accessTarget != null} onOpenChange={handleOpenChange}>
	<Dialog.Content class="sm:max-w-lg">
		<Dialog.Header>
			<Dialog.Title>{m.ingestKeyAccessDialog_title({ name: keys.accessTarget?.name ?? '' })}</Dialog.Title>
			<Dialog.Description>{m.ingestKeyAccessDialog_description()}</Dialog.Description>
		</Dialog.Header>
		<form class="space-y-4" onsubmit={handleSubmit}>
			<div class="space-y-1.5">
				<label for="access-origins" class="text-sm font-medium">{m.ingestKeyAccessDialog_origins()}</label>
				<Textarea id="access-origins" bind:value={origins} rows={4} placeholder="https://app.example.com" class="font-mono text-xs" />
				<p class="text-muted-foreground text-xs">{m.ingestKeyAccessDialog_originsHint()}</p>
			</div>
			<div class="space-y-1.5">
				<label for="access-services" class="text-sm font-medium">{m.ingestKeyAccessDialog_services()}</label>
				<Textarea id="access-services" bind:value={services} rows={3} placeholder="web-frontend" class="font-mono text-xs" />
				<p class="text-muted-foreground text-xs">{m.ingestKeyAccessDialog_servicesHint()}</p>
			</div>
			{#if keys.saveError}
				<p class="text-destructive text-sm">{keys.saveError}</p>
			{/if}
			<Dialog.Footer>
				<Button type="button" variant="outline" onclick={() => keys.closeAccess()}>{m.ingestKeyAccessDialog_cancel()}</Button>
				<Button type="submit" disabled={keys.saving}>
					{#if keys.saving}<Spinner class="size-4" />{/if}
					{m.ingestKeyAccessDialog_save()}
				</Button>
			</Dialog.Footer>
		</form>
	</Dialog.Content>
</Dialog.Root>
