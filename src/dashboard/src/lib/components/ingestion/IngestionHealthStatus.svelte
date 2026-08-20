<script lang="ts">
	// The status verdict this page was missing (feedback: "the page doesn't immediately
	// answer 'is ingestion healthy?'") - a dot + one-line verdict next to the heading, with
	// the contributing reason(s) underneath so the verdict is never a black box. Reuses the
	// app's existing warning/destructive tokens for degraded/down (same ones
	// PipelineFlushHealthTable/PipelineStreamsTable already color their cells with) - emerald
	// is the one addition, there being no existing "good" accent color to reuse (ResourceNode's
	// Healthy badge is neutral gray, which doesn't read as reassuring enough for the one verdict
	// this whole page hangs off of).
	import { ingestionContext } from '$lib/ingestion/context';
	import { computeIngestionHealth, type IngestionHealthLevel } from '$lib/ingestion/health';

	const ingestion = ingestionContext.get();

	const health = $derived(computeIngestionHealth(ingestion.stats, ingestion.pipeline));

	const DOT_CLASS: Record<IngestionHealthLevel, string> = {
		healthy: 'bg-emerald-500 dark:bg-emerald-400',
		degraded: 'bg-warning',
		down: 'bg-destructive'
	};

	const LABEL_CLASS: Record<IngestionHealthLevel, string> = {
		healthy: 'text-emerald-600 dark:text-emerald-400',
		degraded: 'text-warning',
		down: 'text-destructive'
	};
</script>

{#if health}
	<div class="flex flex-col gap-0.5">
		<div class="flex items-center gap-1.5">
			<span class="size-2 shrink-0 rounded-full {DOT_CLASS[health.level]}" aria-hidden="true"></span>
			<span class="text-sm font-medium {LABEL_CLASS[health.level]}">{health.label}</span>
		</div>
		<p class="text-muted-foreground text-xs">{health.detail}</p>
	</div>
{/if}