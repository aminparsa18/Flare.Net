<script lang="ts">
	// Owning-project select for the dashboard / alert / SLO / saved-view / ingest-key forms
	// (ADR-0123). Renders nothing on an instance with no projects (or none the caller can write
	// to), so those forms look exactly as before until projects are adopted.
	import * as Select from '$lib/components/ui/select';
	import { projects } from '$lib/projects/store.svelte';
	import * as m from '$lib/paraglide/messages';

	let {
		value = $bindable<string | null>(null),
		id,
		/** Ingest keys can't be created into a project the caller doesn't administer; pass all projects instead of just writable ones. */
		includeAll = false
	}: { value?: string | null; id?: string; includeAll?: boolean } = $props();

	const options = $derived(includeAll ? projects.mine : projects.writable);
	// Keep an object's current project selectable even if the caller can no longer write to it.
	const current = $derived(value ? projects.mine.find((p) => p.id === value) : undefined);
	const label = $derived(current?.name ?? (value ? m.projectPicker_unknown() : m.projectPicker_none()));
</script>

{#if options.length > 0 || value}
	<div class="flex flex-col gap-1">
		<span class="text-xs font-medium">{m.projectPicker_label()}</span>
		<Select.Root type="single" value={value ?? ''} onValueChange={(v) => (value = v || null)}>
			<Select.Trigger class="w-60" {id}>{label}</Select.Trigger>
			<Select.Content>
				<Select.Item value="" label={m.projectPicker_none()} />
				{#each options as p (p.id)}
					<Select.Item value={p.id} label={p.name} />
				{/each}
			</Select.Content>
		</Select.Root>
		<span class="text-muted-foreground text-xs">{m.projectPicker_hint()}</span>
	</div>
{/if}
