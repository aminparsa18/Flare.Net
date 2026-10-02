<script lang="ts">
	// "Color by" menu + legend shared by the waterfall and flame graph. Each group lists its
	// total self-time (time not covered by child spans) so the legend answers "which service
	// / field value actually spent the trace's time", not just "which colors exist".
	import { traceDetailContext } from '$lib/traces/trace-context';
	import { formatDurationNano } from '$lib/traces/duration';
	import * as Select from '$lib/components/ui/select';
	import * as m from '$lib/paraglide/messages';

	const detail = traceDetailContext.get();

	const named = $derived(detail.colorGroups.filter((g) => g.color !== null));
	const others = $derived(detail.colorGroups.filter((g) => g.color === null));
	const otherSelfTime = $derived(others.reduce((sum, g) => sum + g.selfTimeNano, 0));
	const otherSpanCount = $derived(others.reduce((sum, g) => sum + g.spanCount, 0));

	function optionLabel(o: (typeof detail.colorOptions)[number]): string {
		if (o.scope === 'service') return m.traceColor_service();
		return o.scope === 'span' ? m.traceColor_span({ key: o.key }) : m.traceColor_resource({ key: o.key });
	}
	const activeLabel = $derived(
		optionLabel(detail.colorOptions.find((o) => o.value === detail.effectiveColorBy) ?? detail.colorOptions[0])
	);
</script>

<div class="flex min-w-0 flex-wrap items-center gap-x-3 gap-y-1">
	<Select.Root type="single" value={detail.effectiveColorBy} onValueChange={(v) => v && (detail.colorBy = v)}>
		<Select.Trigger class="h-6 w-auto max-w-56 gap-1 px-2 text-xs" aria-label={m.traceColor_label()}>
			<span class="text-muted-foreground">{m.traceColor_label()}</span>
			<span class="truncate">{activeLabel}</span>
		</Select.Trigger>
		<Select.Content>
			{#each detail.colorOptions as option (option.value)}
				<Select.Item value={option.value} label={optionLabel(option)} />
			{/each}
		</Select.Content>
	</Select.Root>
	{#each named as group (group.value)}
		{@const label = group.value || (detail.effectiveColorBy === 'service' ? '—' : m.traceColor_none())}
		<span
			class="flex min-w-0 items-center gap-1.5"
			title="{label} · {m.traceColor_selfTime({ duration: formatDurationNano(group.selfTimeNano), count: group.spanCount })}"
		>
			<span class="size-2.5 shrink-0 rounded-sm" style="background: {group.color};"></span>
			<span class="max-w-48 truncate">{label}</span>
			<span class="text-muted-foreground tabular-nums">{formatDurationNano(group.selfTimeNano)}</span>
		</span>
	{/each}
	{#if others.length > 0}
		<span
			class="flex items-center gap-1.5"
			title={m.traceColor_selfTime({ duration: formatDurationNano(otherSelfTime), count: otherSpanCount })}
		>
			<span class="bg-muted-foreground/40 size-2.5 shrink-0 rounded-sm"></span>
			<span>{m.traceColor_other({ count: others.length })}</span>
			<span class="text-muted-foreground tabular-nums">{formatDurationNano(otherSelfTime)}</span>
		</span>
	{/if}
</div>
