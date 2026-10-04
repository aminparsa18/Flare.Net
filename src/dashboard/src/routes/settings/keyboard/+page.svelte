<script lang="ts">
	import { Button } from '$lib/components/ui/button';
	import { keyboardPrefs } from '$lib/keyboard/prefs.svelte';
	import {
		FIXED_SHORTCUTS,
		SHORTCUT_ACTIONS,
		comboLabels,
		eventToCombo,
		isMac,
		type ShortcutAction
	} from '$lib/keyboard/shortcuts';
	import * as m from '$lib/paraglide/messages';

	const actionLabels: Record<ShortcutAction, () => string> = {
		commandPalette: () => m.settingsKeyboard_action_commandPalette(),
		nextEvent: () => m.settingsKeyboard_action_nextEvent(),
		prevEvent: () => m.settingsKeyboard_action_prevEvent()
	};
	const fixedLabels = {
		closeDialog: () => m.settingsKeyboard_fixed_closeDialog(),
		scrollList: () => m.settingsKeyboard_fixed_scrollList(),
		stepEvents: () => m.settingsKeyboard_fixed_stepEvents()
	};
	const groups: { heading: () => string; actions: ShortcutAction[] }[] = [
		{ heading: () => m.settingsKeyboard_global(), actions: ['commandPalette'] },
		{ heading: () => m.settingsKeyboard_logs(), actions: ['nextEvent', 'prevEvent'] }
	];

	const mac = isMac();
	let recording = $state<ShortcutAction | null>(null);
	let conflictMessage = $state<string | null>(null);

	// While recording, swallow every keydown in the capture phase so the palette (or the
	// browser) does not also act on the combo being chosen.
	$effect(() => {
		const id = recording;
		if (!id) return;
		function onKey(e: KeyboardEvent) {
			e.preventDefault();
			e.stopPropagation();
			if (e.key === 'Escape') {
				recording = null;
				conflictMessage = null;
				return;
			}
			const combo = eventToCombo(e);
			if (!combo) return; // bare modifier: keep waiting for the real key
			const other = keyboardPrefs.conflict(combo, id!);
			if (other) {
				conflictMessage = m.settingsKeyboard_conflict({ action: actionLabels[other]() });
				return;
			}
			keyboardPrefs.set(id!, combo);
			recording = null;
			conflictMessage = null;
		}
		window.addEventListener('keydown', onKey, true);
		return () => window.removeEventListener('keydown', onKey, true);
	});
</script>

<svelte:head>
	<title>{m.settingsKeyboard_title()}</title>
</svelte:head>

{#snippet keys(combo: string)}
	<span class="flex items-center gap-1">
		{#each comboLabels(combo, mac) as part, i (i)}
			<kbd class="bg-muted min-w-6 rounded border px-1.5 py-0.5 text-center font-mono text-xs">{part}</kbd>
		{/each}
	</span>
{/snippet}

<div class="flex flex-col gap-8">
	<div class="flex items-start justify-between gap-4">
		<div>
			<h2 class="text-xl font-semibold">{m.settingsKeyboard_heading()}</h2>
			<p class="text-muted-foreground text-sm">{m.settingsKeyboard_description()}</p>
		</div>
		<Button variant="outline" size="sm" disabled={keyboardPrefs.isDefaultFor()} onclick={() => keyboardPrefs.reset()}>
			{m.settingsKeyboard_resetAll()}
		</Button>
	</div>

	{#each groups as group (group.actions[0])}
		<section class="flex flex-col gap-2">
			<h3 class="font-medium">{group.heading()}</h3>
			{#each group.actions as id (id)}
				<div class="flex items-center justify-between gap-4 rounded-lg border p-3 text-sm">
					<span>{actionLabels[id]()}</span>
					<span class="flex items-center gap-2">
						<Button
							variant="outline"
							size="sm"
							aria-label={actionLabels[id]()}
							onclick={() => {
								conflictMessage = null;
								recording = recording === id ? null : id;
							}}
						>
							{#if recording === id}
								{m.settingsKeyboard_pressKeys()}
							{:else}
								{@render keys(keyboardPrefs.combo(id))}
							{/if}
						</Button>
						<Button variant="ghost" size="sm" disabled={keyboardPrefs.isDefaultFor(id)} onclick={() => keyboardPrefs.reset(id)}>
							{m.settingsKeyboard_reset()}
						</Button>
					</span>
				</div>
				{#if recording === id && conflictMessage}
					<p class="text-destructive text-xs" role="alert">{conflictMessage}</p>
				{/if}
			{/each}
		</section>
	{/each}

	<section class="flex flex-col gap-2">
		<h3 class="font-medium">{m.settingsKeyboard_fixed()}</h3>
		{#each FIXED_SHORTCUTS as s (s.id)}
			<div class="flex items-center justify-between gap-4 rounded-lg border p-3 text-sm">
				<span>{fixedLabels[s.id]()}</span>
				<span class="flex items-center gap-2">
					{#each s.keys.split(' / ') as k, i (i)}
						{@render keys(k)}
					{/each}
				</span>
			</div>
		{/each}
	</section>
</div>
