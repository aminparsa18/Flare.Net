<script lang="ts">
	// Admin override of managed PII redaction (ADR-0199, ADR-0210). The API cannot see Ingest's own
	// configuration, so the honest states are "follow configuration" / "force on" / "force off".
	import { onMount } from 'svelte';
	import { Button } from '#lib/components/ui/button/index.ts';
	import { authContext } from '#lib/auth/context.ts';
	import { clearPiiRedaction, getPiiRedaction, setPiiRedaction, type PiiRedactionSetting } from '#lib/runtime-settings-api.ts';
	import * as m from '#lib/paraglide/messages.js';

	const auth = authContext.get();

	let setting = $state.raw<PiiRedactionSetting | null>(null);
	let error = $state<string | null>(null);
	let busy = $state(false);

	const mode = $derived(setting?.enabled === true ? 'on' : setting?.enabled === false ? 'off' : 'config');

	async function load() {
		try {
			setting = await getPiiRedaction();
			error = null;
		} catch (err) {
			error = err instanceof Error ? err.message : String(err);
		}
	}

	async function choose(next: 'config' | 'on' | 'off') {
		if (busy || next === mode) return;
		busy = true;
		try {
			if (next === 'config') await clearPiiRedaction();
			else await setPiiRedaction(next === 'on');
			await load();
		} catch (err) {
			error = err instanceof Error ? err.message : String(err);
		} finally {
			busy = false;
		}
	}

	onMount(() => void load());
</script>

<div class="border-b px-4 py-3">
	<div class="flex flex-wrap items-center justify-between gap-3">
		<div class="min-w-0">
			<h2 class="text-sm font-semibold">{m.piiRedactionSetting_heading()}</h2>
			<p class="text-muted-foreground text-xs">{m.piiRedactionSetting_description()}</p>
		</div>
		{#if setting}
			<div class="flex items-center gap-1" role="group" aria-label={m.piiRedactionSetting_heading()}>
				{#each ['config', 'on', 'off'] as const as option (option)}
					<Button
						size="sm"
						variant={mode === option ? 'default' : 'outline'}
						disabled={busy || !auth.isAdmin}
						aria-pressed={mode === option}
						onclick={() => choose(option)}
					>
						{option === 'config' ? m.piiRedactionSetting_config() : option === 'on' ? m.piiRedactionSetting_on() : m.piiRedactionSetting_off()}
					</Button>
				{/each}
			</div>
		{/if}
	</div>
	{#if error}
		<p class="text-destructive mt-2 text-xs">{error}</p>
	{:else if setting}
		<p class="text-muted-foreground mt-2 text-xs">
			{#if setting.enabled == null}
				{m.piiRedactionSetting_statusConfig()}
			{:else if setting.updatedBy}
				{m.piiRedactionSetting_statusOverride({ state: setting.enabled ? m.piiRedactionSetting_on() : m.piiRedactionSetting_off(), user: setting.updatedBy })}
			{:else}
				{m.piiRedactionSetting_statusOverrideNoUser({ state: setting.enabled ? m.piiRedactionSetting_on() : m.piiRedactionSetting_off() })}
			{/if}
			{#if !auth.isAdmin}{m.piiRedactionSetting_adminOnly()}{/if}
		</p>
	{/if}
</div>
