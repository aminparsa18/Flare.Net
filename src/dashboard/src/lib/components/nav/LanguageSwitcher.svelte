<script lang="ts">
	// Reuses the same shadcn-svelte Select primitive as every other picker in the app
	// (e.g. TimeRangePicker/MetricsToolbar's preset dropdowns) rather than a bespoke
	// dropdown - see src/lib/components/ui/select/.
	import * as Select from '$lib/components/ui/select';
	import LanguagesIcon from '@lucide/svelte/icons/languages';
	import { getLocale, setLocale, locales, type Locale } from '$lib/paraglide/runtime';
	import * as m from '$lib/paraglide/messages';

	// Language names are always shown in their own language (endonyms), never translated
	// through m.*() - a "中文" option shouldn't turn into "Chinese" just because the UI
	// is currently in English, same convention every language picker uses.
	const localeLabels: Record<Locale, string> = {
		en: 'English',
		'zh-CN': '中文',
		ru: 'Русский'
	};
</script>

<Select.Root type="single" value={getLocale()} onValueChange={(v) => v && setLocale(v as Locale)}>
	<Select.Trigger size="sm" class="w-auto gap-1.5" aria-label={m.nav_languageLabel()}>
		<LanguagesIcon data-icon="inline-start" />
		{localeLabels[getLocale()]}
	</Select.Trigger>
	<Select.Content>
		{#each locales as l (l)}
			<Select.Item value={l} label={localeLabels[l]} />
		{/each}
	</Select.Content>
</Select.Root>
