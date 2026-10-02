<script lang="ts">
	// "CSV" link in a chart's metadata row - styled like YAxisScaleToggle/BucketIntervalMenu
	// beside it. Exports the series the chart is already showing; see chart-csv.ts.
	import * as m from '$lib/paraglide/messages';

	let { onclick }: { onclick: () => void } = $props();
</script>

<button type="button" class="hover:text-foreground decoration-muted-foreground/50 underline decoration-dotted underline-offset-2" title={m.panelVisualization_tableDownloadCsv()} {onclick}>
	CSV
</button>
