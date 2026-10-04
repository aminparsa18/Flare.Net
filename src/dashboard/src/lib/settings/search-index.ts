// Searchable index of individual user settings (the section headings on /settings/*). Shared by
// the settings rail's search box and the command palette's "Settings" group. Entries link to the
// section page, not an anchor. Labels are functions so they follow the active locale.
import * as m from '$lib/paraglide/messages';

export interface SettingEntry {
	href: string;
	label: string;
	/** Name of the settings page the entry lives on, shown as secondary text. */
	section: string;
}

export function settingEntries(): SettingEntry[] {
	const page = (href: string, section: string, labels: string[]): SettingEntry[] =>
		labels.map((label) => ({ href, label, section }));
	return [
		...page('/settings/appearance', m.settingsAppearance_navLabel(), [
			m.settingsAppearance_themeHeading(),
			m.settingsAppearance_navHeading(),
			m.settingsAppearance_accentHeading(),
			m.settingsAppearance_densityHeading(),
			m.settingsAppearance_fontSizeHeading(),
			m.settingsAppearance_contentWidthHeading(),
			m.settingsAppearance_motionHeading()
		]),
		...page('/settings/regional', m.settingsRegional_navLabel(), [
			m.settingsRegional_languageHeading(),
			m.settingsRegional_timeZoneHeading(),
			m.settingsRegional_timeFormatHeading(),
			m.settingsRegional_dateOrderHeading(),
			m.settingsRegional_weekStartHeading(),
			m.settingsRegional_defaultRangeHeading(),
			m.settingsRegional_numberFormatHeading()
		]),
		...page('/settings/explorer', m.settingsExplorer_navLabel(), [
			m.settingsExplorer_landingHeading(),
			m.settingsExplorer_logTableHeading(),
			m.settingsExplorer_liveHeading(),
			m.settingsExplorer_chartHeading(),
			m.settingsExplorer_facetHeading(),
			m.settingsExplorer_pinnedHeading(),
			m.settingsExplorer_storedHeading()
		]),
		...page('/settings/keyboard', m.settingsKeyboard_navLabel(), [m.settingsKeyboard_heading()]),
		...page('/settings/notifications', m.settingsNotifications_navLabel(), [
			m.settingsNotifications_updateHeading(),
			m.settingsNotifications_browserHeading()
		]),
		...page('/settings/account', m.settingsAccount_navLabel(), [
			m.settingsAccount_heading(),
			m.settingsExport_heading(),
			m.settingsPrefsBackup_heading()
		])
	];
}

/** Case-insensitive match on label or section; every whitespace-separated term must hit. */
export function searchSettings(query: string, entries: SettingEntry[] = settingEntries()): SettingEntry[] {
	const terms = query.toLowerCase().split(/\s+/).filter(Boolean);
	if (!terms.length) return [];
	return entries.filter((e) => {
		const hay = `${e.label} ${e.section}`.toLowerCase();
		return terms.every((t) => hay.includes(t));
	});
}
