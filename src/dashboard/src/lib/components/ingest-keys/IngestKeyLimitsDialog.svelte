<script lang="ts">
	// Edits one key's ingestion limits (ADR-0051): an on/off toggle plus four independently
	// optional caps - events and bytes, per UTC minute and per UTC day. An empty field means
	// "no cap", matching the backend's null. Byte caps take a unit (KB/MB/GB, 1024-based like
	// $lib/ingestion/format.ts's formatBytes) so a daily cap doesn't have to be typed in raw bytes.
	import * as Dialog from '$lib/components/ui/dialog';
	import { Button } from '$lib/components/ui/button';
	import { Input } from '$lib/components/ui/input';
	import { Switch } from '$lib/components/ui/switch';
	import { Spinner } from '$lib/components/ui/spinner';
	import { ingestKeysContext } from '$lib/ingest-keys/context';
	import * as m from '$lib/paraglide/messages';

	const keys = ingestKeysContext.get();

	const UNITS = { KB: 1024, MB: 1024 ** 2, GB: 1024 ** 3 } as const;
	type Unit = keyof typeof UNITS;

	// `bind:value` on a type="number" input yields a number, or null when empty.
	interface ByteField {
		value: number | null;
		unit: Unit;
	}

	let enabled = $state(false);
	let eventsPerMinute = $state<number | null>(null);
	let eventsPerDay = $state<number | null>(null);
	let bytesPerMinute = $state<ByteField>({ value: null, unit: 'MB' });
	let bytesPerDay = $state<ByteField>({ value: null, unit: 'GB' });
	let validationError = $state<string | null>(null);

	/** Largest unit that represents the stored byte count exactly, so reopening the dialog
	 *  shows "5 GB" rather than "5120 MB". */
	function toByteField(bytes: number | null, fallback: Unit): ByteField {
		if (bytes == null) return { value: null, unit: fallback };
		for (const unit of ['GB', 'MB', 'KB'] as const) {
			if (bytes % UNITS[unit] === 0) return { value: bytes / UNITS[unit], unit };
		}
		return { value: bytes / UNITS.KB, unit: 'KB' };
	}

	$effect(() => {
		const target = keys.limitsTarget;
		if (target) {
			enabled = target.limitsEnabled;
			eventsPerMinute = target.maxEventsPerMinute;
			eventsPerDay = target.maxEventsPerDay;
			bytesPerMinute = toByteField(target.maxBytesPerMinute, 'MB');
			bytesPerDay = toByteField(target.maxBytesPerDay, 'GB');
			validationError = null;
		}
	});

	/** Empty -> null (no cap); otherwise a positive number, or undefined when invalid. */
	function parseCap(raw: number | null, multiplier = 1): number | null | undefined {
		if (raw == null) return null;
		const n = Number(raw);
		if (!Number.isFinite(n) || n <= 0) return undefined;
		return Math.max(1, Math.round(n * multiplier));
	}

	function handleOpenChange(next: boolean): void {
		if (keys.saving) return;
		if (!next) keys.closeLimits();
	}

	async function handleSubmit(event: SubmitEvent): Promise<void> {
		event.preventDefault();
		const caps = {
			maxEventsPerMinute: parseCap(eventsPerMinute),
			maxEventsPerDay: parseCap(eventsPerDay),
			maxBytesPerMinute: parseCap(bytesPerMinute.value, UNITS[bytesPerMinute.unit]),
			maxBytesPerDay: parseCap(bytesPerDay.value, UNITS[bytesPerDay.unit])
		};
		if (Object.values(caps).some((c) => c === undefined)) {
			validationError = m.ingestKeyLimitsDialog_invalidCap();
			return;
		}
		validationError = null;
		await keys.saveLimits({
			limitsEnabled: enabled,
			maxEventsPerMinute: caps.maxEventsPerMinute ?? null,
			maxEventsPerDay: caps.maxEventsPerDay ?? null,
			maxBytesPerMinute: caps.maxBytesPerMinute ?? null,
			maxBytesPerDay: caps.maxBytesPerDay ?? null
		});
	}
</script>

{#snippet unitSelect(field: ByteField, id: string)}
	<select
		{id}
		bind:value={field.unit}
		disabled={!enabled}
		class="border-input bg-background h-8 rounded-md border px-2 text-sm disabled:opacity-50"
		aria-label={m.ingestKeyLimitsDialog_unit()}
	>
		{#each Object.keys(UNITS) as unit (unit)}
			<option value={unit}>{unit}</option>
		{/each}
	</select>
{/snippet}

<Dialog.Root open={keys.limitsTarget != null} onOpenChange={handleOpenChange}>
	<Dialog.Content class="sm:max-w-lg">
		<Dialog.Header>
			<Dialog.Title>{m.ingestKeyLimitsDialog_title({ name: keys.limitsTarget?.name ?? '' })}</Dialog.Title>
			<Dialog.Description>{m.ingestKeyLimitsDialog_description()}</Dialog.Description>
		</Dialog.Header>
		<form class="space-y-4" onsubmit={handleSubmit}>
			<label class="flex items-center gap-2 text-sm font-medium">
				<Switch bind:checked={enabled} size="sm" />
				{m.ingestKeyLimitsDialog_enabled()}
			</label>

			<div class="grid grid-cols-[auto_1fr_1fr] items-center gap-x-3 gap-y-2 text-sm">
				<span></span>
				<span class="text-muted-foreground text-xs font-medium">{m.ingestKeyLimitsDialog_perMinute()}</span>
				<span class="text-muted-foreground text-xs font-medium">{m.ingestKeyLimitsDialog_perDay()}</span>

				<span class="font-medium">{m.ingestKeyLimitsDialog_events()}</span>
				<Input
					type="number"
					min="1"
					step="1"
					bind:value={eventsPerMinute}
					disabled={!enabled}
					placeholder={m.ingestKeyLimitsDialog_noCap()}
					aria-label={`${m.ingestKeyLimitsDialog_events()} ${m.ingestKeyLimitsDialog_perMinute()}`}
				/>
				<Input
					type="number"
					min="1"
					step="1"
					bind:value={eventsPerDay}
					disabled={!enabled}
					placeholder={m.ingestKeyLimitsDialog_noCap()}
					aria-label={`${m.ingestKeyLimitsDialog_events()} ${m.ingestKeyLimitsDialog_perDay()}`}
				/>

				<span class="font-medium">{m.ingestKeyLimitsDialog_bytes()}</span>
				<div class="flex gap-1">
					<Input
						type="number"
						min="0"
						step="any"
						bind:value={bytesPerMinute.value}
						disabled={!enabled}
						placeholder={m.ingestKeyLimitsDialog_noCap()}
						aria-label={`${m.ingestKeyLimitsDialog_bytes()} ${m.ingestKeyLimitsDialog_perMinute()}`}
					/>
					{@render unitSelect(bytesPerMinute, 'bytes-per-minute-unit')}
				</div>
				<div class="flex gap-1">
					<Input
						type="number"
						min="0"
						step="any"
						bind:value={bytesPerDay.value}
						disabled={!enabled}
						placeholder={m.ingestKeyLimitsDialog_noCap()}
						aria-label={`${m.ingestKeyLimitsDialog_bytes()} ${m.ingestKeyLimitsDialog_perDay()}`}
					/>
					{@render unitSelect(bytesPerDay, 'bytes-per-day-unit')}
				</div>
			</div>

			<p class="text-muted-foreground text-xs">{m.ingestKeyLimitsDialog_hint()}</p>

			{#if validationError ?? keys.saveError}
				<p class="text-destructive text-sm">{validationError ?? keys.saveError}</p>
			{/if}
			<Dialog.Footer>
				<Button type="button" variant="outline" onclick={() => keys.closeLimits()}>{m.ingestKeyLimitsDialog_cancel()}</Button>
				<Button type="submit" disabled={keys.saving}>
					{#if keys.saving}<Spinner class="size-4" />{/if}
					{m.ingestKeyLimitsDialog_save()}
				</Button>
			</Dialog.Footer>
		</form>
	</Dialog.Content>
</Dialog.Root>
