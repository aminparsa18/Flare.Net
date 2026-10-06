// Icicle layout for a profile's merged call tree: one cell per frame, width proportional to
// its `total`, stacked by depth under its parent. Pure and DOM-free, same as
// $lib/traces/flame-graph.ts. Unlike a trace flame graph there is no time axis - siblings
// are laid out side by side, heaviest first.

import type { FlameGraphNode } from '$lib/profiles-api';

export interface ProfileCell {
	node: FlameGraphNode;
	depth: number;
	/** 0..1 within the laid-out root. */
	x: number;
	width: number;
	/** Path of child indexes from the response root; the stable key and the zoom handle. */
	path: string;
}

export interface ProfileLayout {
	cells: ProfileCell[];
	levels: number;
}

/** Frames narrower than this fraction of the root are not drawn (sub-pixel noise). */
const MIN_WIDTH = 0.0005;

/** Lays out `root` (or the subtree at `focusPath`, e.g. "0.2.1"; '' = the whole tree). */
export function layoutProfile(root: FlameGraphNode, focusPath = ''): ProfileLayout {
	let focus = root;
	for (const part of focusPath ? focusPath.split('.') : []) {
		const next = focus.children[Number(part)];
		if (!next) break;
		focus = next;
	}
	const total = Math.max(focus.total, 1);
	const cells: ProfileCell[] = [];
	let levels = 0;

	function place(node: FlameGraphNode, depth: number, x: number, path: string) {
		const width = node.total / total;
		if (width < MIN_WIDTH) return;
		cells.push({ node, depth, x, width, path });
		levels = Math.max(levels, depth + 1);
		let childX = x;
		const order = node.children.map((child, index) => ({ child, index })).sort((a, b) => b.child.total - a.child.total);
		for (const { child, index } of order) {
			place(child, depth + 1, childX, path ? `${path}.${index}` : String(index));
			childX += child.total / total;
		}
	}
	place(focus, 0, 0, focusPath);
	return { cells, levels };
}

/** Human value for a series' unit: nanoseconds as a duration, bytes as KiB/MiB, else a plain count. */
export function formatProfileValue(value: number, unit: string): string {
	if (unit === 'nanoseconds') {
		if (value >= 1e9) return `${(value / 1e9).toFixed(2)} s`;
		if (value >= 1e6) return `${(value / 1e6).toFixed(2)} ms`;
		if (value >= 1e3) return `${(value / 1e3).toFixed(2)} µs`;
		return `${value} ns`;
	}
	if (unit === 'bytes') {
		const units = ['B', 'KiB', 'MiB', 'GiB', 'TiB'];
		let v = value;
		let i = 0;
		while (v >= 1024 && i < units.length - 1) {
			v /= 1024;
			i++;
		}
		return `${i === 0 ? v : v.toFixed(1)} ${units[i]}`;
	}
	return unit ? `${value.toLocaleString()} ${unit}` : value.toLocaleString();
}

/** Deterministic warm hue per frame name, so the same function keeps its color across zooms. */
export function frameColor(name: string): string {
	let h = 0;
	for (let i = 0; i < name.length; i++) h = (h * 31 + name.charCodeAt(i)) | 0;
	return `hsl(${10 + (Math.abs(h) % 40)} 80% 55%)`;
}
