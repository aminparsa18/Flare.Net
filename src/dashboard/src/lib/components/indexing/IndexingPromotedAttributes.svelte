<script lang="ts">
	// Promoted attribute columns (ADR-0062) - the one thing on this page a user *can*
	// create. Promoting a key adds a MATERIALIZED column + bloom-filter skip index for it
	// on `logs`, and every log filter on that key then reads the column instead of the
	// whole attribute map. Listing is visible to everyone; the form and demote buttons are
	// Admin-only (the API enforces that independently - this just hides controls that 403).
	import * as Table from '$lib/components/ui/table';
	import * as Select from '$lib/components/ui/select';
	import { Badge } from '$lib/components/ui/badge';
	import { Button } from '$lib/components/ui/button';
	import { Input } from '$lib/components/ui/input';
	import { Switch } from '$lib/components/ui/switch';
	import { indexingContext } from '$lib/indexing/context';
	import { authContext } from '$lib/auth/context';
	import type { AttributeBag } from '$lib/api';
	import * as m from '$lib/paraglide/messages';

	const indexing = indexingContext.get();
	const auth = authContext.get();

	const BAGS: { value: AttributeBag; label: () => string }[] = [
		{ value: 'Log', label: m.indexingPromoted_bagLog },
		{ value: 'Resource', label: m.indexingPromoted_bagResource },
		{ value: 'Scope', label: m.indexingPromoted_bagScope }
	];

	let bag = $state<AttributeBag>('Log');
	let key = $state('');
	let backfill = $state(true);
	let busy = $state(false);
	let error = $state<string | null>(null);

	const attributes = $derived(indexing.promoted?.attributes ?? []);
	const max = $derived(indexing.promoted?.maxPromotedAttributes ?? 0);
	const bagLabel = (value: AttributeBag) => BAGS.find((b) => b.value === value)?.label() ?? value;

	async function promote(event: SubmitEvent) {
		event.preventDefault();
		const trimmed = key.trim();
		if (!trimmed) return;
		busy = true;
		error = null;
		try {
			await indexing.promote(bag, trimmed, backfill);
			key = '';
		} catch (err) {
			error = err instanceof Error ? err.message : String(err);
		} finally {
			busy = false;
		}
	}

	async function demote(columnName: string, attributeKey: string) {
		if (!confirm(m.indexingPromoted_confirmDemote({ key: attributeKey }))) return;
		busy = true;
		error = null;
		try {
			await indexing.demote(columnName);
		} catch (err) {
			error = err instanceof Error ? err.message : String(err);
		} finally {
			busy = false;
		}
	}
</script>

<div class="flex flex-col gap-3 px-4 pb-4">
	<div class="flex flex-col gap-1">
		<h2 class="text-sm font-medium">{m.indexingPromoted_heading()}</h2>
		<p class="text-muted-foreground text-xs">{m.indexingPromoted_description()}</p>
	</div>

	{#if auth.isAdmin}
		<form class="flex flex-wrap items-center gap-2" onsubmit={promote}>
			<Select.Root type="single" value={bag} onValueChange={(v) => v && (bag = v as AttributeBag)}>
				<Select.Trigger class="h-8 w-32" aria-label={m.indexingPromoted_bagColumn()}>
					{bagLabel(bag)}
				</Select.Trigger>
				<Select.Content>
					{#each BAGS as option (option.value)}
						<Select.Item value={option.value} label={option.label()} />
					{/each}
				</Select.Content>
			</Select.Root>
			<Input
				class="h-8 w-64 font-mono text-xs"
				placeholder={m.indexingPromoted_keyPlaceholder()}
				aria-label={m.indexingPromoted_keyColumn()}
				bind:value={key}
				disabled={busy}
			/>
			<label class="text-muted-foreground flex items-center gap-2 text-xs">
				<Switch bind:checked={backfill} disabled={busy} />
				{m.indexingPromoted_backfill()}
			</label>
			<Button type="submit" size="sm" disabled={busy || !key.trim() || attributes.length >= max}>
				{m.indexingPromoted_promote()}
			</Button>
			<span class="text-muted-foreground text-xs tabular-nums">
				{m.indexingPromoted_usage({ count: attributes.length, max })}
			</span>
		</form>
	{/if}

	{#if error}
		<p class="text-destructive text-xs">{error}</p>
	{/if}

	{#if attributes.length === 0}
		<p class="text-muted-foreground text-xs">{m.indexingPromoted_empty()}</p>
	{:else}
		<Table.Root>
			<Table.Header>
				<Table.Row>
					<Table.Head>{m.indexingPromoted_bagColumn()}</Table.Head>
					<Table.Head>{m.indexingPromoted_keyColumn()}</Table.Head>
					<Table.Head>{m.indexingPromoted_columnColumn()}</Table.Head>
					<Table.Head>{m.indexingPromoted_statusColumn()}</Table.Head>
					{#if auth.isAdmin}
						<Table.Head class="w-0"></Table.Head>
					{/if}
				</Table.Row>
			</Table.Header>
			<Table.Body>
				{#each attributes as attribute (attribute.columnName)}
					<Table.Row>
						<Table.Cell>{bagLabel(attribute.bag)}</Table.Cell>
						<Table.Cell class="font-mono text-xs">{attribute.key}</Table.Cell>
						<Table.Cell class="text-muted-foreground font-mono text-xs">{attribute.columnName}</Table.Cell>
						<Table.Cell>
							{#if attribute.backfilling}
								<Badge variant="outline" title={m.indexingPromoted_backfillingTooltip()}>{m.indexingPromoted_backfilling()}</Badge>
							{:else}
								<Badge variant="secondary">{m.indexingPromoted_active()}</Badge>
							{/if}
						</Table.Cell>
						{#if auth.isAdmin}
							<Table.Cell>
								<Button variant="ghost" size="sm" disabled={busy} onclick={() => demote(attribute.columnName, attribute.key)}>
									{m.indexingPromoted_demote()}
								</Button>
							</Table.Cell>
						{/if}
					</Table.Row>
				{/each}
			</Table.Body>
		</Table.Root>
	{/if}
</div>
