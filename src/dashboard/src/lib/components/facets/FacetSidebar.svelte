<script lang="ts" generics="TBag extends string">
	// Collapsible facet filter sidebar shared by the Logs and Traces pages: one
	// FacetSection per facet (values + counts, click to filter), plus an "Add attribute"
	// form for user-chosen attribute facets. Page-agnostic - each page hands in its own
	// FacetDefinition list (`$lib/logs/facets.ts` / `$lib/traces/facets.ts`) and its
	// FacetSidebarPrefs instance.
	import PanelLeftCloseIcon from '@lucide/svelte/icons/panel-left-close';
	import PanelLeftOpenIcon from '@lucide/svelte/icons/panel-left-open';
	import RefreshCwIcon from '@lucide/svelte/icons/refresh-cw';
	import PlusIcon from '@lucide/svelte/icons/plus';
	import { Button } from '$lib/components/ui/button';
	import { Input } from '$lib/components/ui/input';
	import * as Select from '$lib/components/ui/select';
	import FacetSection from './FacetSection.svelte';
	import type { FacetDefinition } from '$lib/facets/types';
	import type { FacetSidebarPrefs } from '$lib/facets/prefs.svelte';
	import * as m from '$lib/paraglide/messages';

	let {
		facets,
		reloadKey,
		prefs,
		bagOptions
	}: {
		facets: FacetDefinition[];
		/** Changes whenever the page's filter does - every open section re-fetches on a change. */
		reloadKey: string;
		prefs: FacetSidebarPrefs<TBag>;
		/** Bags the "Add attribute" form offers, first one pre-selected. */
		bagOptions: { value: TBag; label: string }[];
	} = $props();

	// Bumped by the refresh button - folded into every section's reloadKey so a manual
	// refresh re-fetches without the filter having changed.
	let refreshCount = $state(0);
	const sectionReloadKey = $derived(`${reloadKey}#${refreshCount}`);

	let adding = $state(false);
	let newBag = $state<TBag | undefined>(undefined);
	let newKey = $state('');

	function submitAttribute(event: SubmitEvent): void {
		event.preventDefault();
		const bag = newBag ?? bagOptions[0]?.value;
		if (!bag || !newKey.trim()) return;
		prefs.addAttribute(bag, newKey);
		newKey = '';
		adding = false;
	}
</script>

{#if prefs.open}
	<aside class="flex w-60 shrink-0 flex-col border-r" aria-label={m.facets_title()}>
		<div class="flex items-center gap-1 border-b px-3 py-2">
			<span class="flex-1 text-xs font-medium">{m.facets_title()}</span>
			<Button variant="ghost" size="icon-sm" aria-label={m.facets_refresh()} title={m.facets_refresh()} onclick={() => refreshCount++}>
				<RefreshCwIcon />
			</Button>
			<Button variant="ghost" size="icon-sm" aria-label={m.facets_hide()} title={m.facets_hide()} onclick={() => prefs.setOpen(false)}>
				<PanelLeftCloseIcon />
			</Button>
		</div>
		<div class="min-h-0 flex-1 overflow-y-auto">
			{#each facets as facet (facet.id)}
				<FacetSection {facet} reloadKey={sectionReloadKey} />
			{/each}
			<div class="px-3 py-2">
				{#if adding}
					<form class="flex flex-col gap-1.5" onsubmit={submitAttribute}>
						<Select.Root type="single" value={newBag ?? bagOptions[0]?.value} onValueChange={(v) => (newBag = v as TBag)}>
							<Select.Trigger class="h-7 w-full text-xs">
								{bagOptions.find((o) => o.value === (newBag ?? bagOptions[0]?.value))?.label}
							</Select.Trigger>
							<Select.Content>
								{#each bagOptions as option (option.value)}
									<Select.Item value={option.value} label={option.label} />
								{/each}
							</Select.Content>
						</Select.Root>
						<!-- svelte-ignore a11y_autofocus -->
						<Input class="h-7 text-xs" placeholder={m.attributeFilters_keyPlaceholder()} bind:value={newKey} autofocus />
						<div class="flex gap-1.5">
							<Button type="submit" size="sm" disabled={!newKey.trim()}>{m.facets_add()}</Button>
							<Button type="button" size="sm" variant="ghost" onclick={() => (adding = false)}>{m.facets_cancel()}</Button>
						</div>
					</form>
				{:else}
					<button type="button" class="text-muted-foreground hover:text-foreground flex items-center gap-1 text-xs" onclick={() => (adding = true)}>
						<PlusIcon class="size-3.5" />
						{m.facets_addAttribute()}
					</button>
				{/if}
			</div>
		</div>
	</aside>
{:else}
	<div class="flex shrink-0 flex-col items-center border-r px-1 py-2">
		<Button variant="ghost" size="icon-sm" aria-label={m.facets_show()} title={m.facets_show()} onclick={() => prefs.setOpen(true)}>
			<PanelLeftOpenIcon />
		</Button>
	</div>
{/if}
