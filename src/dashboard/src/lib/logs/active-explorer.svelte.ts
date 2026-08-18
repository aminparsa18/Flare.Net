// Cross-tree bridge letting CommandPalette (mounted in +layout.svelte as a *sibling* of
// `{@render children()}`, not a descendant of the Logs page) reach the Logs page's live
// toggle and export dialog. Svelte context can't do this - context only flows down to
// descendants of whoever calls .set(), and the palette sits outside the page's subtree -
// so this is deliberately a plain module-level $state singleton instead of
// logsExplorerContext. See CommandPalette.svelte's "Actions" group and the PR that added
// it (originally left Live Mode/Export out for exactly this reason).
//
// LogsToolbar.svelte registers itself on mount and clears on destroy (it's the component
// that already holds both `explorer`, via logsExplorerContext, and the ExportDialog
// instance) - `null` means "not currently on the Logs page," which the palette uses to
// hide the group entirely rather than show actions that would throw.
import type { LogsExplorerState } from './state.svelte';

export interface ActiveLogsExplorer {
	explorer: LogsExplorerState;
	/** Opens the Logs page's own ExportDialog (same scope/format picker as the toolbar button). */
	openExport: () => void;
}

let current = $state<ActiveLogsExplorer | null>(null);

export const activeLogsExplorer = {
	get current(): ActiveLogsExplorer | null {
		return current;
	}
};

export function setActiveLogsExplorer(value: ActiveLogsExplorer | null): void {
	current = value;
}
