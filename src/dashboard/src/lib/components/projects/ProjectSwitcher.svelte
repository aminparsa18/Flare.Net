<script lang="ts">
	// Nav-bar project switcher (ADR-0123). Filters config lists to one project (plus instance-wide
	// objects); hidden until the caller belongs to at least one project.
	import * as Select from '$lib/components/ui/select';
	import { projects } from '$lib/projects/store.svelte';
	import FolderIcon from '@lucide/svelte/icons/folder';
	import * as m from '$lib/paraglide/messages';
</script>

{#if projects.mine.length > 0}
	<Select.Root type="single" value={projects.activeId} onValueChange={(v) => projects.setActive(v ?? '')}>
		<Select.Trigger size="sm" class="w-44" aria-label={m.projectSwitcher_label()}>
			<FolderIcon class="size-4 shrink-0" />
			<span class="truncate">{projects.active?.name ?? m.projectSwitcher_all()}</span>
		</Select.Trigger>
		<Select.Content>
			<Select.Item value="" label={m.projectSwitcher_all()} />
			{#each projects.mine as p (p.id)}
				<Select.Item value={p.id} label={p.name} />
			{/each}
		</Select.Content>
	</Select.Root>
{/if}
