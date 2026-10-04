<script lang="ts">
	// Row action on the alerts table: mutes one rule for a quick duration (or until a date)
	// by creating a one-off maintenance window scoped to it - no new storage (ADR-0055).
	// While the rule is muted the same slot becomes an Unmute button.
	import * as Popover from '$lib/components/ui/popover';
	import { Button } from '$lib/components/ui/button';
	import { Input } from '$lib/components/ui/input';
	import { maintenanceWindowsContext } from '$lib/maintenance-windows/context';
	import type { AlertRule } from '$lib/alerts-api';
	import * as m from '$lib/paraglide/messages';
	import BellOffIcon from '@lucide/svelte/icons/bell-off';
	import BellRingIcon from '@lucide/svelte/icons/bell-ring';

	let { rule }: { rule: AlertRule } = $props();

	const maintenance = maintenanceWindowsContext.get();

	const DURATIONS = [
		{ minutes: 15, label: () => m.muteRule_15m() },
		{ minutes: 60, label: () => m.muteRule_1h() },
		{ minutes: 4 * 60, label: () => m.muteRule_4h() },
		{ minutes: 24 * 60, label: () => m.muteRule_1d() },
		{ minutes: 7 * 24 * 60, label: () => m.muteRule_1w() }
	];

	let open = $state(false);
	let reason = $state('');
	let until = $state('');
	let busy = $state(false);

	const quickMute = $derived(maintenance.activeQuickMute(rule));
	const untilDate = $derived(until ? new Date(until) : null);
	const untilValid = $derived(untilDate !== null && !Number.isNaN(untilDate.getTime()) && untilDate.getTime() > Date.now());

	async function mute(endsAt: Date): Promise<void> {
		busy = true;
		await maintenance.mute(rule, endsAt, reason);
		busy = false;
		open = false;
		reason = '';
		until = '';
	}

	async function unmute(): Promise<void> {
		if (!quickMute) return;
		busy = true;
		await maintenance.unmute(quickMute);
		busy = false;
	}
</script>

{#if quickMute}
	<Button variant="ghost" size="icon-sm" title={m.muteRule_unmute()} disabled={busy} onclick={unmute}>
		<BellRingIcon />
	</Button>
{:else}
	<Popover.Root bind:open>
		<Popover.Trigger>
			{#snippet child({ props })}
				<Button {...props} variant="ghost" size="icon-sm" title={m.muteRule_action()}>
					<BellOffIcon />
				</Button>
			{/snippet}
		</Popover.Trigger>
		<Popover.Content class="w-72 space-y-3 text-left" align="end">
			<p class="text-sm font-medium">{m.muteRule_heading({ name: rule.name })}</p>
			<Input bind:value={reason} maxlength={200} placeholder={m.muteRule_reasonPlaceholder()} aria-label={m.muteRule_reasonLabel()} />
			<div class="flex flex-wrap gap-1">
				{#each DURATIONS as d (d.minutes)}
					<Button variant="outline" size="sm" disabled={busy} onclick={() => mute(new Date(Date.now() + d.minutes * 60_000))}>{d.label()}</Button>
				{/each}
			</div>
			<div class="flex items-center gap-2">
				<Input type="datetime-local" bind:value={until} aria-label={m.muteRule_untilLabel()} />
				<Button size="sm" disabled={busy || !untilValid} onclick={() => untilDate && mute(untilDate)}>{m.muteRule_untilButton()}</Button>
			</div>
		</Popover.Content>
	</Popover.Root>
{/if}
