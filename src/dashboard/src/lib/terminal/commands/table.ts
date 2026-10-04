// Plain-text table for the terminal commands whose flare.cli counterparts render a Spectre
// table (alerts list/history/import, notification-channels list): column widths come from
// the content, not a fixed per-command width, so long names/threshold summaries never
// truncate or shear the columns after them.

export function formatTable(headers: string[], rows: string[][]): string[] {
	const widths = headers.map((h, col) => Math.max(h.length, ...rows.map((r) => (r[col] ?? '').length)));
	const render = (cells: string[]) =>
		cells
			.map((cell, col) => (col === cells.length - 1 ? cell : cell.padEnd(widths[col])))
			.join('  ')
			.trimEnd();
	return [render(headers), ...rows.map(render)];
}

/** `0.###` - what flare.cli's FormatNumber prints for an observed/baseline value. */
export function formatNumber(value: number | undefined): string {
	return value === undefined ? 'n/a' : String(Number(value.toFixed(3)));
}

/** `G4` - four significant digits, no trailing zeros (alerts history's Observed/Threshold columns). */
export function formatG4(value: number): string {
	return String(Number(value.toPrecision(4)));
}
