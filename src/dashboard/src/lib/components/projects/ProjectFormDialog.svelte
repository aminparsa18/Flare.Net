<script lang="ts">
	// Create/edit a project: name, description and the service.name allow-list (ADR-0123).
	// Same Dialog + reset-on-open $effect shape as SloFormDialog.svelte.
	import * as Dialog from '$lib/components/ui/dialog';
	import { Button } from '$lib/components/ui/button';
	import { Input } from '$lib/components/ui/input';
	import { Textarea } from '$lib/components/ui/textarea';
	import { Spinner } from '$lib/components/ui/spinner';
	import { createProject, updateProject, type Project } from '$lib/projects-api';
	import * as m from '$lib/paraglide/messages';

	let {
		target,
		onclose,
		onsaved
	}: { target: Project | 'new' | null; onclose: () => void; onsaved: () => void | Promise<void> } = $props();

	const open = $derived(target !== null);
	const isEdit = $derived(target !== null && target !== 'new');

	let name = $state('');
	let description = $state('');
	let patternsText = $state('');
	let saving = $state(false);
	let saveError = $state<string | null>(null);

	$effect(() => {
		if (target === 'new') {
			name = '';
			description = '';
			patternsText = '';
		} else if (target) {
			name = target.name;
			description = target.description;
			patternsText = target.servicePatterns.join('\n');
		}
		saveError = null;
	});

	// One pattern per line; commas also split so a pasted "a, b" works.
	const patterns = $derived(
		patternsText
			.split(/[\n,]/)
			.map((p) => p.trim())
			.filter(Boolean)
	);

	async function save(event: SubmitEvent): Promise<void> {
		event.preventDefault();
		saving = true;
		saveError = null;
		try {
			const request = { name: name.trim(), description: description.trim(), servicePatterns: patterns };
			if (target && target !== 'new') await updateProject(target.id, request);
			else await createProject(request);
			await onsaved();
		} catch (e) {
			saveError = e instanceof Error ? e.message : String(e);
		} finally {
			saving = false;
		}
	}
</script>

<Dialog.Root {open} onOpenChange={(next) => !next && !saving && onclose()}>
	<Dialog.Content class="sm:max-w-md">
		<Dialog.Header>
			<Dialog.Title>{isEdit ? m.projectForm_titleEdit() : m.projectForm_titleNew()}</Dialog.Title>
			<Dialog.Description>{m.projectForm_description()}</Dialog.Description>
		</Dialog.Header>
		<form class="space-y-4" onsubmit={save}>
			<div class="space-y-2">
				<label for="project-name" class="text-sm font-medium">{m.projectForm_nameLabel()}</label>
				<Input id="project-name" bind:value={name} required maxlength={100} />
			</div>
			<div class="space-y-2">
				<label for="project-description" class="text-sm font-medium">{m.projectForm_descriptionLabel()}</label>
				<Textarea id="project-description" bind:value={description} rows={2} maxlength={500} />
			</div>
			<div class="space-y-2">
				<label for="project-patterns" class="text-sm font-medium">{m.projectForm_patternsLabel()}</label>
				<Textarea id="project-patterns" bind:value={patternsText} rows={5} class="font-mono" placeholder={'checkout\npayments-*'} />
				<p class="text-muted-foreground text-xs">{m.projectForm_patternsHint()}</p>
			</div>
			{#if saveError}
				<p class="text-destructive text-sm">{saveError}</p>
			{/if}
			<Dialog.Footer>
				<Button type="button" variant="outline" onclick={() => onclose()}>{m.projectForm_cancel()}</Button>
				<Button type="submit" disabled={saving || !name.trim()}>
					{#if saving}<Spinner class="size-4" />{/if}
					{isEdit ? m.projectForm_save() : m.projectForm_create()}
				</Button>
			</Dialog.Footer>
		</form>
	</Dialog.Content>
</Dialog.Root>
