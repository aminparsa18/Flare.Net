<script lang="ts">
	// Linear/log Y-axis switch in MetricChart's/FormulaChart's metadata row on the Explorer
	// page - styled like BucketIntervalMenu's trigger beside it (dotted-underline text, not a
	// button chrome) so the row still reads as one line of chart settings. Dashboard panels
	// don't render this; they set their scale in YAxisBoundsPopover instead.
	import type { YAxisScale } from '$lib/metrics/axis';
	import * as m from '$lib/paraglide/messages';

	let { value, onChange }: { value: YAxisScale; onChange: (scale: YAxisScale) => void } = $props();
</script>

<button
	type="button"
	class="hover:text-foreground decoration-muted-foreground/50 underline decoration-dotted underline-offset-2"
	aria-pressed={value === 'log'}
	title={value === 'log' ? m.valueDist_switchToLinear() : `${m.valueDist_switchToLog()} - ${m.yAxisScale_logNote()}`}
	onclick={() => onChange(value === 'log' ? 'linear' : 'log')}
>
	{value === 'log' ? m.yAxisScale_axisLog() : m.yAxisScale_axisLinear()}
</button>
