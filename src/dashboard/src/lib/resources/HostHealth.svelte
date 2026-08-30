<script lang="ts">
	// "Host health" section - turns host-health.ts's computed checks into the small
	// icon+text list the mockup asked for, deliberately not another big dashboard section.
	// Purely presentational (checks are computed by the caller, HostOverview.svelte) - same
	// split $lib/components/indexing/IndexingStorageHealth.svelte uses between its own
	// computeStorageHealth() and rendering, and this mirrors that component's rendering
	// wholesale: a Card, divide-y rows, a tone-colored icon per row.
	import * as Card from '$lib/components/ui/card';
	import CheckIcon from '@lucide/svelte/icons/check';
	import TriangleAlertIcon from '@lucide/svelte/icons/triangle-alert';
	import type { HostHealthCheck, HostHealthTone } from '$lib/resources/host-health';

	let { checks }: { checks: HostHealthCheck[] } = $props();

	const TONE_ICON = { good: CheckIcon, warning: TriangleAlertIcon } satisfies Record<HostHealthTone, typeof CheckIcon>;

	// Same emerald/warning split IndexingStorageHealth.svelte's own TONE_TEXT_CLASS uses -
	// no 'unavailable' tone here (see host-health.ts's remarks - the checks that could be
	// unavailable, Docker/Kubernetes, are omitted from the list entirely instead).
	const TONE_TEXT_CLASS = { good: 'text-emerald-600 dark:text-emerald-400', warning: 'text-warning' } satisfies Record<HostHealthTone, string>;
</script>

<div class="mt-4 border-t pt-3">
	<h3 class="mb-2 text-sm font-medium">Host health</h3>
	<Card.Root>
		<Card.Content class="divide-y px-0 py-0">
			{#each checks as check (check.id)}
				{@const Icon = TONE_ICON[check.tone]}
				<div class="flex items-center justify-between gap-2.5 px-4 py-2.5">
					<span class="flex items-center gap-2 text-sm">
						<Icon class="size-4 shrink-0 {TONE_TEXT_CLASS[check.tone]}" />
						{check.title}
					</span>
					<span class="text-muted-foreground text-xs {check.tone === 'warning' ? TONE_TEXT_CLASS.warning : ''}">{check.detail}</span>
				</div>
			{/each}
		</Card.Content>
	</Card.Root>
</div>
