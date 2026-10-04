<script lang="ts">
	import ChoiceGroup from '$lib/components/settings/ChoiceGroup.svelte';
	import { getLocale, locales, type Locale } from '$lib/paraglide/runtime';
	import { displayTimeZone } from '$lib/time/display-zone.svelte';
	import { browserTimeZone, timeZoneOptions } from '$lib/time/time-zone';
	import { formatUtcOffset } from '$lib/time/format';
	import { regional, DEFAULT_RANGE_CHOICES } from '$lib/regional/prefs.svelte';
	import { presetLabel } from '$lib/logs/time-range';
	import { formatTimestamp } from '$lib/time/format';
	import * as m from '$lib/paraglide/messages';

	// Endonyms, never translated - same convention as the user menu's language picker.
	const localeLabels: Record<Locale, string> = { en: 'English', 'zh-CN': '中文', ru: 'Русский' };

	const sample = $derived(formatTimestamp(Date.UTC(2026, 8, 26, 14, 3, 5, 123)));
	const explorers = [
		{ key: 'logs', label: () => m.settingsRegional_explorerLogs() },
		{ key: 'traces', label: () => m.settingsRegional_explorerTraces() },
		{ key: 'metrics', label: () => m.settingsRegional_explorerMetrics() }
	] as const;

	const zones = $derived(timeZoneOptions().map((zone) => ({ zone, offset: formatUtcOffset(zone) })));
</script>

<svelte:head>
	<title>{m.settingsRegional_title()}</title>
</svelte:head>

<div class="flex flex-col gap-8">
	<div>
		<h2 class="text-xl font-semibold">{m.settingsRegional_heading()}</h2>
		<p class="text-muted-foreground text-sm">{m.settingsRegional_description()}</p>
	</div>

	<section class="flex flex-col gap-3">
		<h3 class="font-medium">{m.settingsRegional_languageHeading()}</h3>
		<ChoiceGroup
			label={m.settingsRegional_languageHeading()}
			value={getLocale()}
			onchange={(v) => void regional.changeLocale(v)}
			options={locales.map((l) => ({ value: l, label: localeLabels[l] }))}
		/>
	</section>

	<section class="flex flex-col gap-3">
		<h3 class="font-medium">{m.settingsRegional_timeZoneHeading()}</h3>
		<p class="text-muted-foreground text-sm">{m.settingsRegional_timeZoneDescription()}</p>
		<select
			class="border-input bg-background h-9 max-w-md rounded-md border px-3 text-sm"
			aria-label={m.settingsRegional_timeZoneHeading()}
			value={displayTimeZone.zone}
			onchange={(e) => displayTimeZone.set(e.currentTarget.value)}
		>
			<option value="local">{m.nav_timeZoneBrowser({ zone: browserTimeZone() })}</option>
			<option value="UTC">UTC</option>
			{#each zones as { zone, offset } (zone)}
				{#if zone !== 'UTC'}<option value={zone}>{zone} ({offset})</option>{/if}
			{/each}
		</select>
	</section>

	<section class="flex flex-col gap-3">
		<h3 class="font-medium">{m.settingsRegional_timeFormatHeading()}</h3>
		<ChoiceGroup
			label={m.settingsRegional_timeFormatHeading()}
			value={regional.timeFormat}
			onchange={(v) => regional.set('timeFormat', v)}
			options={[
				{ value: '24h', label: m.settingsRegional_timeFormat24h() },
				{ value: '12h', label: m.settingsRegional_timeFormat12h() }
			]}
		/>
		<h3 class="mt-2 font-medium">{m.settingsRegional_dateOrderHeading()}</h3>
		<ChoiceGroup
			label={m.settingsRegional_dateOrderHeading()}
			value={regional.dateOrder}
			onchange={(v) => regional.set('dateOrder', v)}
			options={[
				{ value: 'iso', label: 'YYYY-MM-DD' },
				{ value: 'dmy', label: 'DD/MM/YYYY' },
				{ value: 'mdy', label: 'MM/DD/YYYY' }
			]}
		/>
		<p class="text-muted-foreground text-sm">{m.settingsRegional_sample({ sample })}</p>
	</section>

	<section class="flex flex-col gap-3">
		<h3 class="font-medium">{m.settingsRegional_weekStartHeading()}</h3>
		<p class="text-muted-foreground text-sm">{m.settingsRegional_weekStartDescription()}</p>
		<ChoiceGroup
			label={m.settingsRegional_weekStartHeading()}
			value={regional.weekStart}
			onchange={(v) => regional.set('weekStart', v)}
			options={[
				{ value: 'monday', label: m.settingsRegional_weekMonday() },
				{ value: 'sunday', label: m.settingsRegional_weekSunday() },
				{ value: 'saturday', label: m.settingsRegional_weekSaturday() }
			]}
		/>
	</section>

	<section class="flex flex-col gap-3">
		<h3 class="font-medium">{m.settingsRegional_defaultRangeHeading()}</h3>
		<p class="text-muted-foreground text-sm">{m.settingsRegional_defaultRangeDescription()}</p>
		<div class="flex flex-col gap-2">
			{#each explorers as explorer (explorer.key)}
				<label class="flex items-center justify-between gap-4 rounded-lg border p-3 text-sm">
					<span class="font-medium">{explorer.label()}</span>
					<select
						class="border-input bg-background h-9 rounded-md border px-3 text-sm"
						value={regional.defaultRanges[explorer.key]}
						onchange={(e) => regional.setDefaultRange(explorer.key, e.currentTarget.value as (typeof DEFAULT_RANGE_CHOICES)[number])}
					>
						{#each DEFAULT_RANGE_CHOICES as preset (preset)}
							<option value={preset}>{presetLabel(preset)}</option>
						{/each}
					</select>
				</label>
			{/each}
		</div>
		<p class="text-muted-foreground text-xs">{m.settingsRegional_defaultRangeNote()}</p>
	</section>
</div>
