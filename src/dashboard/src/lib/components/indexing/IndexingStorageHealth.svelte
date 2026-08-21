<script lang="ts">
	// "Let Flare do the analysis" - turns the numbers already on this page (compression,
	// parts, per-table row share, cross-signal growth rate) into plain-language verdicts
	// instead of leaving the reader to eyeball the Tables/growth data themselves and decide
	// what's normal. See $lib/indexing/storage-health.ts for the checks themselves and why
	// there's no "skip index usage" check among them.
	import * as Card from '$lib/components/ui/card';
	import { Spinner } from '$lib/components/ui/spinner';
	import { indexingContext } from '$lib/indexing/context';
	import { computeStorageHealth, type StorageHealthTone } from '$lib/indexing/storage-health';
	import CheckIcon from '@lucide/svelte/icons/check';
	import TriangleAlertIcon from '@lucide/svelte/icons/triangle-alert';
	import MinusIcon from '@lucide/svelte/icons/minus';

	const indexing = indexingContext.get();

	const TONE_ICON = {
		good: CheckIcon,
		warning: TriangleAlertIcon,
		unavailable: MinusIcon
	} satisfies Record<StorageHealthTone, typeof CheckIcon>;

	const TONE_TEXT_CLASS = {
		good: 'text-emerald-600 dark:text-emerald-400',
		warning: 'text-warning',
		unavailable: 'text-muted-foreground'
	} satisfies Record<StorageHealthTone, string>;

	const checks = $derived(indexing.stats ? computeStorageHealth(indexing.stats) : []);
</script>

<div class="px-4 pb-4">
	<h2 class="mb-2 text-sm font-medium">Storage health</h2>
	{#if indexing.loading && !indexing.stats}
		<div class="flex h-32 items-center justify-center">
			<Spinner />
		</div>
	{:else}
		<Card.Root>
			<Card.Content class="divide-y px-0 py-0">
				{#each checks as check (check.id)}
					{@const Icon = TONE_ICON[check.tone]}
					<div class="flex items-start gap-2.5 px-4 py-3">
						<Icon class="mt-0.5 size-4 shrink-0 {TONE_TEXT_CLASS[check.tone]}" />
						<div class="flex flex-col gap-0.5">
							<span class="text-sm font-medium">{check.title}</span>
							<span class="text-muted-foreground text-xs">{check.detail}</span>
						</div>
					</div>
				{/each}
			</Card.Content>
		</Card.Root>
	{/if}
</div>
