<script lang="ts">
	import { setMode, userPrefersMode } from 'mode-watcher';
	import ChoiceGroup from '$lib/components/settings/ChoiceGroup.svelte';
	import { Switch } from '$lib/components/ui/switch';
	import { Button } from '$lib/components/ui/button';
	import { appearance } from '$lib/appearance/prefs.svelte';
	import * as Table from '$lib/components/ui/table';
	import { Badge } from '$lib/components/ui/badge';
	import SunIcon from '@lucide/svelte/icons/sun';
	import MoonIcon from '@lucide/svelte/icons/moon';
	import MonitorIcon from '@lucide/svelte/icons/monitor';
	import PanelTopIcon from '@lucide/svelte/icons/panel-top';
	import PanelLeftIcon from '@lucide/svelte/icons/panel-left';
	import { ACCENTS } from '$lib/appearance/prefs.svelte';
	import * as m from '$lib/paraglide/messages';

	const SWATCH: Record<string, string> = {
		blue: 'oklch(0.55 0.2 255)',
		violet: 'oklch(0.55 0.22 293)',
		green: 'oklch(0.55 0.16 150)',
		orange: 'oklch(0.62 0.18 50)',
		rose: 'oklch(0.58 0.21 12)'
	};

	const PREVIEW_ROWS = [
		{ time: '12:04:31', level: 'info', service: 'checkout', body: 'Order 8841 placed in 212 ms' },
		{ time: '12:04:32', level: 'warn', service: 'payments', body: 'Retrying charge after timeout (attempt 2)' },
		{ time: '12:04:33', level: 'error', service: 'payments', body: 'Card processor returned 502' }
	];

	type Mode = 'light' | 'dark' | 'system';
</script>

<svelte:head>
	<title>{m.settingsAppearance_title()}</title>
</svelte:head>

<div class="flex flex-col gap-8">
	<div class="flex items-start justify-between gap-4">
		<div>
			<h2 class="text-xl font-semibold">{m.settingsAppearance_heading()}</h2>
			<p class="text-muted-foreground text-sm">{m.settingsAppearance_description()}</p>
		</div>
		<Button variant="outline" size="sm" disabled={appearance.isDefault} onclick={() => appearance.reset()}>
			{m.settingsAppearance_reset()}
		</Button>
	</div>

	<section class="bg-card flex flex-col gap-3 rounded-lg border p-4" aria-label={m.settingsAppearance_previewHeading()}>
		<div class="flex items-center justify-between gap-2">
			<h3 class="font-medium">{m.settingsAppearance_previewHeading()}</h3>
			<span class="text-muted-foreground text-xs">{m.settingsAppearance_previewHint()}</span>
		</div>
		<Table.Root>
			<Table.Header>
				<Table.Row>
					<Table.Head>{m.settingsAppearance_previewTime()}</Table.Head>
					<Table.Head>{m.settingsAppearance_previewLevel()}</Table.Head>
					<Table.Head>{m.settingsAppearance_previewService()}</Table.Head>
					<Table.Head>{m.settingsAppearance_previewMessage()}</Table.Head>
				</Table.Row>
			</Table.Header>
			<Table.Body>
				{#each PREVIEW_ROWS as row (row.time)}
					<Table.Row>
						<Table.Cell class="text-muted-foreground">{row.time}</Table.Cell>
						<Table.Cell><Badge variant={row.level === 'error' ? 'destructive' : 'secondary'}>{row.level}</Badge></Table.Cell>
						<Table.Cell>{row.service}</Table.Cell>
						<Table.Cell data-log-body>{row.body}</Table.Cell>
					</Table.Row>
				{/each}
			</Table.Body>
		</Table.Root>
		<div class="flex gap-2">
			<Button size="sm">{m.settingsAppearance_previewPrimary()}</Button>
			<Button size="sm" variant="outline">{m.settingsAppearance_previewSecondary()}</Button>
		</div>
	</section>

	<section class="flex flex-col gap-3">
		<h3 class="font-medium">{m.settingsAppearance_themeHeading()}</h3>
		<ChoiceGroup
			label={m.settingsAppearance_themeHeading()}
			value={userPrefersMode.current as Mode}
			onchange={(v) => setMode(v)}
			options={[
				{ value: 'light', label: m.nav_themeLight(), icon: SunIcon },
				{ value: 'dark', label: m.nav_themeDark(), icon: MoonIcon },
				{ value: 'system', label: m.nav_themeSystem(), icon: MonitorIcon }
			]}
		/>
	</section>

	<section class="flex flex-col gap-3">
		<h3 class="font-medium">{m.settingsAppearance_navHeading()}</h3>
		<ChoiceGroup
			label={m.settingsAppearance_navHeading()}
			value={appearance.navLayout}
			onchange={(v) => appearance.set('navLayout', v)}
			options={[
				{ value: 'top', label: m.settingsAppearance_navTop(), description: m.settingsAppearance_navTopDescription(), icon: PanelTopIcon },
				{ value: 'sidebar', label: m.settingsAppearance_navSidebar(), description: m.settingsAppearance_navSidebarDescription(), icon: PanelLeftIcon }
			]}
		/>
		{#if appearance.navLayout === 'sidebar'}
			<label class="flex items-center justify-between gap-4 rounded-lg border p-3 text-sm">
				<span>
					<span class="block font-medium">{m.settingsAppearance_sidebarCollapsedLabel()}</span>
					<span class="text-muted-foreground text-xs">{m.settingsAppearance_sidebarCollapsedDescription()}</span>
				</span>
				<Switch checked={appearance.sidebarCollapsed} onCheckedChange={(v) => appearance.set('sidebarCollapsed', v)} />
			</label>
			{#if appearance.sidebarCollapsed}
				<label class="flex items-center justify-between gap-4 rounded-lg border p-3 text-sm">
					<span>
						<span class="block font-medium">{m.settingsAppearance_sidebarHoverExpandLabel()}</span>
						<span class="text-muted-foreground text-xs">{m.settingsAppearance_sidebarHoverExpandDescription()}</span>
					</span>
					<Switch checked={appearance.sidebarHoverExpand} onCheckedChange={(v) => appearance.set('sidebarHoverExpand', v)} />
				</label>
			{/if}
		{/if}
	</section>

	<section class="flex flex-col gap-3">
		<h3 class="font-medium">{m.settingsAppearance_accentHeading()}</h3>
		<div class="flex flex-wrap gap-2" role="radiogroup" aria-label={m.settingsAppearance_accentHeading()}>
			{#each ACCENTS as accent (accent)}
				<button
					type="button"
					role="radio"
					aria-checked={appearance.accent === accent}
					data-accent={accent}
					class="flex items-center gap-2 rounded-lg border px-3 py-2 text-sm {appearance.accent === accent ? 'border-primary ring-primary/40 ring-2' : ''}"
					onclick={() => appearance.set('accent', accent)}
				>
					<span class="size-4 rounded-full border" style={accent === 'default' ? 'background: var(--foreground)' : `background: ${SWATCH[accent]}`}></span>
					{m[`settingsAppearance_accent_${accent}`]()}
				</button>
			{/each}
		</div>
	</section>

	<section class="flex flex-col gap-3">
		<h3 class="font-medium">{m.settingsAppearance_densityHeading()}</h3>
		<ChoiceGroup
			label={m.settingsAppearance_densityHeading()}
			value={appearance.density}
			onchange={(v) => appearance.set('density', v)}
			options={[
				{ value: 'comfortable', label: m.settingsAppearance_densityComfortable(), description: m.settingsAppearance_densityComfortableDescription() },
				{ value: 'compact', label: m.settingsAppearance_densityCompact(), description: m.settingsAppearance_densityCompactDescription() }
			]}
		/>
	</section>

	<section class="flex flex-col gap-3">
		<h3 class="font-medium">{m.settingsAppearance_fontSizeHeading()}</h3>
		<ChoiceGroup
			label={m.settingsAppearance_fontSizeHeading()}
			value={appearance.fontSize}
			onchange={(v) => appearance.set('fontSize', v)}
			options={[
				{ value: 'small', label: m.settingsAppearance_fontSizeSmall() },
				{ value: 'default', label: m.settingsAppearance_fontSizeDefault() },
				{ value: 'large', label: m.settingsAppearance_fontSizeLarge() }
			]}
		/>
	</section>

	<section class="flex flex-col gap-3">
		<h3 class="font-medium">{m.settingsAppearance_contentWidthHeading()}</h3>
		<ChoiceGroup
			label={m.settingsAppearance_contentWidthHeading()}
			value={appearance.contentWidth}
			onchange={(v) => appearance.set('contentWidth', v)}
			options={[
				{ value: 'full', label: m.settingsAppearance_contentWidthFull(), description: m.settingsAppearance_contentWidthFullDescription() },
				{ value: 'centered', label: m.settingsAppearance_contentWidthCentered(), description: m.settingsAppearance_contentWidthCenteredDescription() }
			]}
		/>
	</section>

	<section class="flex flex-col gap-3">
		<label class="flex items-center justify-between gap-4 rounded-lg border p-3 text-sm">
			<span>
				<span class="block font-medium">{m.settingsAppearance_monoLogsLabel()}</span>
				<span class="text-muted-foreground text-xs">{m.settingsAppearance_monoLogsDescription()}</span>
			</span>
			<Switch checked={appearance.monoLogs} onCheckedChange={(v) => appearance.set('monoLogs', v)} />
		</label>
		<label class="flex items-center justify-between gap-4 rounded-lg border p-3 text-sm">
			<span>
				<span class="block font-medium">{m.settingsAppearance_highContrastLabel()}</span>
				<span class="text-muted-foreground text-xs">{m.settingsAppearance_highContrastDescription()}</span>
			</span>
			<Switch checked={appearance.highContrast} onCheckedChange={(v) => appearance.set('highContrast', v)} />
		</label>
	</section>

	<section class="flex flex-col gap-3">
		<h3 class="font-medium">{m.settingsAppearance_motionHeading()}</h3>
		<label class="flex items-center justify-between gap-4 rounded-lg border p-3 text-sm">
			<span>
				<span class="block font-medium">{m.settingsAppearance_reduceMotionLabel()}</span>
				<span class="text-muted-foreground text-xs">{m.settingsAppearance_reduceMotionDescription()}</span>
			</span>
			<Switch checked={appearance.reduceMotion} onCheckedChange={(v) => appearance.set('reduceMotion', v)} />
		</label>
	</section>

	<p class="text-muted-foreground text-xs">{m.settingsAppearance_storageNote()}</p>
</div>
