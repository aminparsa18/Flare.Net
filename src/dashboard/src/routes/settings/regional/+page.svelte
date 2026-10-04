<script lang="ts">
	import ChoiceGroup from '$lib/components/settings/ChoiceGroup.svelte';
	import { getLocale, setLocale, locales, type Locale } from '$lib/paraglide/runtime';
	import { displayTimeZone } from '$lib/time/display-zone.svelte';
	import { browserTimeZone, timeZoneOptions } from '$lib/time/time-zone';
	import { formatUtcOffset } from '$lib/time/format';
	import * as m from '$lib/paraglide/messages';

	// Endonyms, never translated - same convention as the user menu's language picker.
	const localeLabels: Record<Locale, string> = { en: 'English', 'zh-CN': '中文', ru: 'Русский' };

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
			onchange={(v) => setLocale(v)}
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
</div>
