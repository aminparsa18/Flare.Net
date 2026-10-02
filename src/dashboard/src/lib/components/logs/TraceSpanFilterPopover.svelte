<script lang="ts">
	import * as Popover from '$lib/components/ui/popover';
	import { Button } from '$lib/components/ui/button';
	import { Input } from '$lib/components/ui/input';
	import { Checkbox } from '$lib/components/ui/checkbox';
	import ChevronDownIcon from '@lucide/svelte/icons/chevron-down';
	import PopoverMultiSelect from './PopoverMultiSelect.svelte';
	import { logsExplorerContext } from '$lib/logs/context';
	import { emptyTraceSpanFilter, isTraceSpanFilterActive } from '$lib/logs/state.svelte';
	import * as m from '$lib/paraglide/messages';

	// "Logs from traces where a `payments` span errored or took > 2s" - the explorer's
	// LogFilter.traceSpanFilter. Every edit applies immediately, same as the other toolbar
	// filters; text/number fields commit on blur/Enter so a half-typed value doesn't re-query.
	const explorer = logsExplorerContext.get();

	const filter = $derived(explorer.filter.traceSpan);
	const active = $derived(isTraceSpanFilterActive(filter));
	const serviceOptions = $derived(explorer.knownServices.map((s) => ({ value: s, label: s })));

	let nameDraft = $state('');
	// bind:value on a number input yields a number (or null when empty), not a string.
	let durationDraft = $state<number | null>(null);
	$effect(() => {
		nameDraft = filter.spanName;
		durationDraft = filter.minDurationMs;
	});

	function update(patch: Partial<typeof filter>) {
		explorer.setTraceSpanFilter({ ...filter, ...patch });
	}

	function commitName() {
		if (nameDraft.trim() !== filter.spanName) update({ spanName: nameDraft.trim() });
	}

	function commitDuration() {
		const next = durationDraft != null && Number.isFinite(durationDraft) && durationDraft >= 0 ? durationDraft : null;
		if (next !== filter.minDurationMs) update({ minDurationMs: next });
		else durationDraft = next;
	}

	const buttonLabel = $derived(m.logsToolbar_traceSpanLabel());
</script>

<Popover.Root>
	<Popover.Trigger>
		{#snippet child({ props })}
			<Button {...props} variant={active ? 'secondary' : 'outline'} size="sm">
				{buttonLabel}{active ? ' •' : ''}
				<ChevronDownIcon data-icon="inline-end" />
			</Button>
		{/snippet}
	</Popover.Trigger>
	<Popover.Content class="flex w-80 flex-col gap-3 p-3" align="start">
		<p class="text-muted-foreground text-xs">{m.traceSpanFilter_hint()}</p>

		<PopoverMultiSelect
			label={m.traceSpanFilter_service()}
			options={serviceOptions}
			selected={filter.services}
			onChange={(next) => update({ services: next })}
		/>

		<label class="flex items-center gap-2 text-sm">
			<Checkbox checked={filter.errorsOnly} onCheckedChange={(v) => update({ errorsOnly: v === true })} />
			{m.traceSpanFilter_errorsOnly()}
		</label>

		<Input
			placeholder={m.traceSpanFilter_name()}
			bind:value={nameDraft}
			onblur={commitName}
			onkeydown={(e) => e.key === 'Enter' && commitName()}
		/>
		<Input
			type="number"
			min="0"
			placeholder={m.traceSpanFilter_minDuration()}
			bind:value={durationDraft}
			onblur={commitDuration}
			onkeydown={(e) => e.key === 'Enter' && commitDuration()}
		/>

		<Button variant="ghost" size="sm" class="self-end" disabled={!active} onclick={() => explorer.setTraceSpanFilter(emptyTraceSpanFilter())}>
			{m.traceSpanFilter_clear()}
		</Button>
	</Popover.Content>
</Popover.Root>
