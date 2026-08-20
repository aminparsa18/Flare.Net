<script lang="ts">
	// The "how do I get logs into Flare" guide - OpenObserve calls this page "Data
	// sources" and links it from a persistent topbar item; this one is deliberately NOT
	// in AppNav/nav-links.ts (see that file's own list) - the only way in is the "See how
	// to ingest data" link on the Logs empty state (LogTable.svelte), which is exactly
	// the moment someone actually needs it.
	import { browser } from '$app/environment';
	import { API_BASE_URL } from '$lib/api';
	import { buildCategories } from '$lib/data-sources/catalog';
	import CodeBlock from '$lib/components/data-sources/CodeBlock.svelte';
	import { Input } from '$lib/components/ui/input';
	import { cn } from '$lib/utils.js';
	import SearchIcon from '@lucide/svelte/icons/search';

	// window.location.hostname is a correct guess for the common case (docker-compose:
	// dashboard/api/ingest all on one host, different ports - see docker-compose.yml)
	// but not a guarantee for every topology; catalog.ts's doc comment on GuideEndpoints
	// covers why Kubernetes deliberately doesn't use this.
	const host = browser ? window.location.hostname : 'localhost';
	const { categories, items } = $derived(
		buildCategories({
			grpcHostPort: `${host}:4317`,
			grpcUri: `http://${host}:4317`,
			httpUri: `http://${host}:4318`,
			apiOrigin: API_BASE_URL
		})
	);

	let activeCategoryId = $state('recommended');
	let activeItemId = $state('kubernetes');
	let query = $state('');

	const activeCategory = $derived(categories.find((c) => c.id === activeCategoryId) ?? categories[0]);
	const visibleItems = $derived(
		activeCategory.itemIds
			.map((id) => items[id])
			.filter((item) => item.title.toLowerCase().includes(query.trim().toLowerCase()))
	);
	const activeItem = $derived(items[activeItemId] ?? visibleItems[0]);

	function selectCategory(id: string): void {
		activeCategoryId = id;
		query = '';
		const first = categories.find((c) => c.id === id)?.itemIds[0];
		if (first) activeItemId = first;
	}
</script>

<svelte:head>
	<title>Data sources · Flare</title>
</svelte:head>

<div class="flex h-full min-h-0 flex-col">
	<div class="shrink-0 border-b px-6 py-4">
		<h1 class="text-lg font-semibold tracking-tight">Data sources</h1>
		<p class="text-muted-foreground mt-1 text-sm">
			Flare.Ingest speaks plain OTLP - pick where your logs are coming from for a ready-to-run snippet.
		</p>
	</div>

	<div role="tablist" aria-label="Data source category" class="flex shrink-0 gap-1 overflow-x-auto border-b px-6">
		{#each categories as category (category.id)}
			<button
				type="button"
				role="tab"
				aria-selected={category.id === activeCategoryId}
				onclick={() => selectCategory(category.id)}
				class={cn(
					'shrink-0 border-b-2 px-3 py-2.5 text-sm font-medium transition-colors',
					category.id === activeCategoryId
						? 'border-foreground text-foreground'
						: 'text-muted-foreground hover:text-foreground border-transparent'
				)}
			>
				{category.label}
			</button>
		{/each}
	</div>

	<div class="flex min-h-0 flex-1">
		<div class="flex w-64 shrink-0 flex-col border-r">
			<div class="p-3 pb-2">
				<div class="relative">
					<SearchIcon class="text-muted-foreground pointer-events-none absolute top-1/2 left-2 size-4 -translate-y-1/2" />
					<Input class="pl-8" placeholder="Search" bind:value={query} />
				</div>
			</div>
			<div class="min-h-0 flex-1 overflow-y-auto p-2 pt-0">
				{#each visibleItems as item (item.id)}
					{@const Icon = item.icon}
					<button
						type="button"
						onclick={() => (activeItemId = item.id)}
						class={cn(
							'flex w-full items-center gap-2.5 rounded-md px-2.5 py-2 text-left text-sm transition-colors',
							item.id === activeItem?.id ? 'bg-accent text-accent-foreground' : 'hover:bg-accent/50'
						)}
					>
						<Icon class="text-muted-foreground size-4 shrink-0" />
						{item.title}
					</button>
				{:else}
					<p class="text-muted-foreground px-2.5 py-2 text-sm">No matches.</p>
				{/each}
			</div>
		</div>

		<div class="min-h-0 flex-1 overflow-y-auto px-6 py-5">
			{#if activeItem}
				{@const item = activeItem}
				{@const Icon = item.icon}
				<div class="mx-auto flex max-w-2xl flex-col gap-6">
					<div>
						<h2 class="flex items-center gap-2 text-base font-semibold">
							<Icon class="text-muted-foreground size-5" />
							{item.title}
						</h2>
						<p class="text-muted-foreground mt-1 text-sm">{item.intro}</p>
					</div>

					{#each item.steps as step, i (i)}
						<div class="flex flex-col gap-2">
							{#if step.heading}
								<h3 class="text-sm font-medium">{step.heading}</h3>
							{/if}
							{#if step.body}
								<p class="text-muted-foreground text-sm">{step.body}</p>
							{/if}
							{#if step.code}
								<CodeBlock code={step.code.text} label={step.code.label} />
							{/if}
						</div>
					{/each}
				</div>
			{/if}
		</div>
	</div>
</div>
