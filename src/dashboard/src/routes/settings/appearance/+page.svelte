<script lang="ts">
	import { setMode, userPrefersMode } from 'mode-watcher';
	import ChoiceGroup from '$lib/components/settings/ChoiceGroup.svelte';
	import { Switch } from '$lib/components/ui/switch';
	import { Button } from '$lib/components/ui/button';
	import { appearance } from '$lib/appearance/prefs.svelte';
	import SunIcon from '@lucide/svelte/icons/sun';
	import MoonIcon from '@lucide/svelte/icons/moon';
	import MonitorIcon from '@lucide/svelte/icons/monitor';
	import PanelTopIcon from '@lucide/svelte/icons/panel-top';
	import PanelLeftIcon from '@lucide/svelte/icons/panel-left';
	import * as m from '$lib/paraglide/messages';

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
		{/if}
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
