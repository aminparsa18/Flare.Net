// Rebindable keyboard shortcuts (Settings > Keyboard). A binding is a combo string such as
// "Mod+K", "Shift+J" or "J": modifiers in fixed order (Mod, Alt, Shift) then one key. "Mod" is
// Cmd on macOS and Ctrl elsewhere, matched as either so a binding made on one machine works
// on the other when the document syncs.

export const SHORTCUT_ACTIONS = [
	'commandPalette',
	'showShortcuts',
	'goLogs',
	'goTraces',
	'goMetrics',
	'goAlerts',
	'goDashboards',
	'toggleLive',
	'focusSearch',
	'exportLogs',
	'nextEvent',
	'prevEvent'
] as const;
export type ShortcutAction = (typeof SHORTCUT_ACTIONS)[number];

export const DEFAULT_BINDINGS: Record<ShortcutAction, string> = {
	commandPalette: 'Mod+K',
	showShortcuts: '?',
	goLogs: '1',
	goTraces: '2',
	goMetrics: '3',
	goAlerts: '4',
	goDashboards: '5',
	toggleLive: 'L',
	focusSearch: '/',
	exportLogs: 'E',
	nextEvent: 'J',
	prevEvent: 'K'
};

/** Shortcuts that are fixed (not rebindable); listed in the cheat sheet so it is complete. */
export const FIXED_SHORTCUTS: readonly { keys: string; id: 'closeDialog' | 'scrollList' | 'stepEvents' }[] = [
	{ keys: 'Escape', id: 'closeDialog' },
	{ keys: 'ArrowUp / ArrowDown', id: 'scrollList' },
	{ keys: 'ArrowUp / ArrowDown', id: 'stepEvents' }
];

const MODIFIER_KEYS = new Set(['Control', 'Meta', 'Alt', 'Shift', 'AltGraph', 'OS']);
// Keys that browsers/OS reserve or that would make the app unusable if captured.
const RESERVED = new Set(['Tab', 'Escape', 'Enter', ' ', 'Dead']);

function normalizeKey(key: string): string {
	return key.length === 1 ? key.toUpperCase() : key;
}

/** The combo a keydown represents, or null for a bare modifier press / a reserved key. */
export function eventToCombo(e: KeyboardEvent): string | null {
	if (MODIFIER_KEYS.has(e.key) || RESERVED.has(e.key)) return null;
	const parts: string[] = [];
	if (e.metaKey || e.ctrlKey) parts.push('Mod');
	if (e.altKey) parts.push('Alt');
	// Shift on a printable character already changes the character ("?" vs "/"), so it is
	// only recorded for named keys and for letters/digits.
	if (e.shiftKey && (e.key.length > 1 || /^[a-z0-9]$/i.test(e.key))) parts.push('Shift');
	parts.push(normalizeKey(e.key));
	return parts.join('+');
}

export function matchesCombo(e: KeyboardEvent, combo: string): boolean {
	return eventToCombo(e) === combo;
}

/** True for combos that fire while typing in a field (they hold Mod or Alt); bare keys must not. */
export function firesInFields(combo: string): boolean {
	return combo.startsWith('Mod+') || combo.startsWith('Alt+');
}

export function isEditableTarget(target: EventTarget | null): boolean {
	const t = target as HTMLElement | null;
	return !!t && (t.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(t.tagName));
}

export function isValidCombo(value: unknown): value is string {
	if (typeof value !== 'string' || value.length === 0 || value.length > 32) return false;
	const parts = value.split('+');
	const key = parts[parts.length - 1];
	if (!key || MODIFIER_KEYS.has(key) || RESERVED.has(key)) return false;
	return parts.slice(0, -1).every((p) => p === 'Mod' || p === 'Alt' || p === 'Shift');
}

const KEY_LABELS: Record<string, string> = {
	ArrowUp: '↑',
	ArrowDown: '↓',
	ArrowLeft: '←',
	ArrowRight: '→',
	Backspace: '⌫',
	Delete: 'Del'
};

/** Display parts for a combo, e.g. ['⌘', 'K'] on a Mac and ['Ctrl', 'K'] elsewhere. */
export function comboLabels(combo: string, mac: boolean): string[] {
	return combo.split('+').map((p) => {
		if (p === 'Mod') return mac ? '⌘' : 'Ctrl';
		if (p === 'Alt') return mac ? '⌥' : 'Alt';
		if (p === 'Shift') return mac ? '⇧' : 'Shift';
		return KEY_LABELS[p] ?? p;
	});
}

export function isMac(): boolean {
	return typeof navigator !== 'undefined' && /Mac|iPhone|iPad/.test(navigator.platform);
}
