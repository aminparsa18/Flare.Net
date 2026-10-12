<script lang="ts">
	// One signal's retention: what ClickHouse has now (parsed live) next to what was last asked
	// for, plus the editable default / cold-after / per-resource rules (ADR-0143/0144/0145).
	import * as Card from '$lib/components/ui/card';
	import { Badge } from '$lib/components/ui/badge';
	import { Button } from '$lib/components/ui/button';
	import { Input } from '$lib/components/ui/input';
	import PlusIcon from '@lucide/svelte/icons/plus';
	import XIcon from '@lucide/svelte/icons/x';
	import { MAX_RETENTION_DAYS, MAX_RETENTION_RULES, type RetentionRule, type SignalRetention } from '$lib/retention-api';
	import * as m from '$lib/paraglide/messages';

	let {
		retention,
		coldAvailable,
		busy,
		error,
		onsave
	}: {
		retention: SignalRetention;
		coldAvailable: boolean;
		/** Any retention change is pending - the API allows one at a time. */
		busy: boolean;
		error: string | null;
		onsave: (change: { days: number; coldAfterDays: number; rules: RetentionRule[] }) => void;
	} = $props();

	// The draft starts from what Flare last asked for, falling back to what ClickHouse reports.
	// The page re-keys the card when a save lands, so capturing the initial value is intended.
	/* svelte-ignore state_referenced_locally */
	let days = $state(String(retention.expectedDays ?? retention.actualDays ?? 0));
	/* svelte-ignore state_referenced_locally */
	let coldAfter = $state(String(retention.expectedColdAfterDays ?? retention.actualColdAfterDays ?? 0));
	/* svelte-ignore state_referenced_locally */
	let rules = $state<{ attribute: string; value: string; days: string }[]>(
		(retention.expectedRules.length ? retention.expectedRules : retention.actualRules).map((r) => ({ ...r, days: String(r.days) }))
	);

	const parse = (s: string): number => (String(s).trim() === '' ? NaN : Number(s));
	const validDays = (n: number): boolean => Number.isInteger(n) && n >= 0 && n <= MAX_RETENTION_DAYS;

	const problem = $derived.by(() => {
		const d = parse(days);
		if (!validDays(d)) return m.retention_invalidDays({ max: MAX_RETENTION_DAYS });
		for (const r of rules) {
			if (!r.attribute.trim() || !r.value) return m.retention_ruleIncomplete();
			if (!validDays(parse(r.days))) return m.retention_invalidDays({ max: MAX_RETENTION_DAYS });
		}
		const c = parse(coldAfter);
		if (coldAvailable && !validDays(c)) return m.retention_invalidDays({ max: MAX_RETENTION_DAYS });
		if (coldAvailable && c > 0) {
			const finite = [d, ...rules.map((r) => parse(r.days))].filter((n) => n > 0);
			if (finite.length && c >= Math.min(...finite)) return m.retention_coldTooLate({ days: Math.min(...finite) });
		}
		return null;
	});

	function describe(n: number | null): string {
		if (n === null) return '—';
		return n === 0 ? m.retention_forever() : m.retention_days({ count: n });
	}

	const actual = $derived(
		retention.actualState === 'custom'
			? m.retention_actualCustom()
			: retention.actualState === 'none'
				? m.retention_actualNone()
				: describe(retention.actualDays)
	);
	const drift = $derived(
		retention.status === 'success' &&
			retention.actualState === 'days' &&
			(retention.expectedDays !== retention.actualDays || retention.expectedRules.length !== retention.actualRules.length)
	);

	function save() {
		if (problem) return;
		onsave({
			days: parse(days),
			coldAfterDays: coldAvailable ? parse(coldAfter) : 0,
			rules: rules.map((r) => ({ attribute: r.attribute.trim(), value: r.value, days: parse(r.days) }))
		});
	}
</script>

<Card.Root>
	<Card.Header class="flex flex-row items-center justify-between gap-2">
		<div class="flex flex-col gap-0.5">
			<Card.Title class="capitalize">{retention.signal}</Card.Title>
			<span class="text-muted-foreground text-xs">{retention.tables.join(', ')}</span>
		</div>
		<div class="flex items-center gap-2">
			{#if retention.status === 'pending'}<Badge variant="warning">{m.retention_statusPending()}</Badge>{/if}
			{#if retention.status === 'failed'}<Badge variant="destructive">{m.retention_statusFailed()}</Badge>{/if}
			{#if drift}<Badge variant="warning">{m.retention_drift()}</Badge>{/if}
		</div>
	</Card.Header>
	<Card.Content class="flex flex-col gap-4">
		<dl class="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1 text-sm">
			<dt class="text-muted-foreground">{m.retention_actual()}</dt>
			<dd>
				{actual}
				{#if retention.actualColdAfterDays}
					<span class="text-muted-foreground">· {m.retention_coldAfterValue({ count: retention.actualColdAfterDays })}</span>
				{/if}
				{#if retention.actualRules.length}
					<span class="text-muted-foreground">· {m.retention_ruleCount({ count: retention.actualRules.length })}</span>
				{/if}
			</dd>
			{#if retention.status === 'failed' && retention.error}
				<dt class="text-muted-foreground">{m.retention_lastError()}</dt>
				<dd class="text-destructive">{retention.error}</dd>
			{/if}
		</dl>

		<div class="flex flex-wrap items-end gap-4">
			<label class="flex flex-col gap-1 text-sm">
				<span class="font-medium">{rules.length ? m.retention_defaultDays() : m.retention_keepDays()}</span>
				<Input type="number" min="0" max={MAX_RETENTION_DAYS} bind:value={days} class="w-32" />
			</label>
			{#if coldAvailable}
				<label class="flex flex-col gap-1 text-sm">
					<span class="font-medium">{m.retention_coldAfter()}</span>
					<Input type="number" min="0" max={MAX_RETENTION_DAYS} bind:value={coldAfter} class="w-32" />
				</label>
			{/if}
		</div>
		<p class="text-muted-foreground -mt-2 text-xs">{m.retention_zeroHint()}</p>

		<div class="flex flex-col gap-2">
			<span class="text-sm font-medium">{m.retention_rulesHeading()}</span>
			<p class="text-muted-foreground text-xs">{m.retention_rulesHint()}</p>
			{#each rules as rule, i (i)}
				<div class="flex flex-wrap items-center gap-2">
					<Input placeholder="deployment.environment" bind:value={rule.attribute} class="w-56" aria-label={m.retention_ruleAttribute()} />
					<span class="text-muted-foreground">=</span>
					<Input placeholder="dev" bind:value={rule.value} class="w-40" aria-label={m.retention_ruleValue()} />
					<Input type="number" min="0" max={MAX_RETENTION_DAYS} bind:value={rule.days} class="w-24" aria-label={m.retention_ruleDays()} />
					<Button variant="ghost" size="icon" aria-label={m.retention_removeRule()} onclick={() => (rules = rules.filter((_, j) => j !== i))}>
						<XIcon />
					</Button>
				</div>
			{/each}
			<div>
				<Button
					variant="outline"
					size="sm"
					disabled={rules.length >= MAX_RETENTION_RULES}
					onclick={() => (rules = [...rules, { attribute: '', value: '', days: '7' }])}
				>
					<PlusIcon />{m.retention_addRule()}
				</Button>
			</div>
		</div>

		{#if problem}<p class="text-destructive text-sm">{problem}</p>{/if}
		{#if error}<p class="text-destructive text-sm">{error}</p>{/if}
	</Card.Content>
	<Card.Footer>
		<Button disabled={busy || problem !== null} onclick={save}>{m.retention_save()}</Button>
	</Card.Footer>
</Card.Root>
