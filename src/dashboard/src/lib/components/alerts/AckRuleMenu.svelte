<script lang="ts">
	// Row action on the alerts table for a firing rule: acknowledge the incident or snooze
	// re-notifications for a while (ADR-0124). Once acked/snoozed the same slot becomes a
	// Clear button.
	import * as Popover from '$lib/components/ui/popover';
	import { Button } from '$lib/components/ui/button';
	import { Input } from '$lib/components/ui/input';
	import { alertsContext } from '$lib/alerts/context';
	import { ackAlertRule, clearAlertAck, snoozeAlertRule, type AlertRule, type AlertRuleStatus } from '$lib/alerts-api';
	import * as m from '$lib/paraglide/messages';
	import BellMinusIcon from '@lucide/svelte/icons/bell-minus';
	import CheckCheckIcon from '@lucide/svelte/icons/check-check';

	let { rule, status }: { rule: AlertRule; status: AlertRuleStatus | undefined } = $props();

	const alerts = alertsContext.get();

	const DURATIONS = [
		{ minutes: 15, label: () => m.ackRule_15m() },
		{ minutes: 60, label: () => m.ackRule_1h() },
		{ minutes: 4 * 60, label: () => m.ackRule_4h() },
		{ minutes: 24 * 60, label: () => m.ackRule_1d() }
	];

	let open = $state(false);
	let note = $state('');
	let busy = $state(false);
	let error = $state<string | null>(null);

	async function run(action: () => Promise<AlertRuleStatus>): Promise<void> {
		busy = true;
		error = null;
		try {
			alerts.setStatus(await action());
			open = false;
			note = '';
		} catch (err) {
			error = err instanceof Error ? err.message : String(err);
		} finally {
			busy = false;
		}
	}
</script>

{#if status?.ack}
	<Button variant="ghost" size="icon-sm" title={m.ackRule_clearAction()} disabled={busy} onclick={() => run(() => clearAlertAck(rule.id))}>
		<BellMinusIcon />
	</Button>
{:else}
	<Popover.Root bind:open>
		<Popover.Trigger>
			{#snippet child({ props })}
				<Button {...props} variant="ghost" size="icon-sm" title={m.ackRule_action()}>
					<CheckCheckIcon />
				</Button>
			{/snippet}
		</Popover.Trigger>
		<Popover.Content class="w-72 space-y-3 text-left" align="end">
			<p class="text-sm font-medium">{m.ackRule_heading({ name: rule.name })}</p>
			<Input bind:value={note} maxlength={500} placeholder={m.ackRule_notePlaceholder()} aria-label={m.ackRule_noteLabel()} />
			<div class="space-y-1">
				<Button size="sm" disabled={busy} onclick={() => run(() => ackAlertRule(rule.id, note))}>{m.ackRule_ack()}</Button>
				<p class="text-muted-foreground text-xs">{m.ackRule_ackHint()}</p>
			</div>
			<p class="text-muted-foreground text-xs">{m.ackRule_snoozeHeading()}</p>
			<div class="flex flex-wrap gap-1">
				{#each DURATIONS as d (d.minutes)}
					<Button variant="outline" size="sm" disabled={busy} onclick={() => run(() => snoozeAlertRule(rule.id, d.minutes, note))}>{d.label()}</Button>
				{/each}
			</div>
			{#if error}
				<p class="text-destructive text-xs">{error}</p>
			{/if}
		</Popover.Content>
	</Popover.Root>
{/if}
