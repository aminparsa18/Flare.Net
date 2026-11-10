export type Side = 'top' | 'right' | 'bottom' | 'left';

export interface Box {
	top: number;
	left: number;
	width: number;
	height: number;
}

/** Padding between a highlighted element and the edge of its spotlight. */
export const SPOTLIGHT_PADDING = 6;

export function findTarget(id: string): HTMLElement | null {
	return document.querySelector<HTMLElement>(`[data-tour="${CSS.escape(id)}"]`);
}

/**
 * The on-screen box of a tour target. Targets are usually `display: contents` wrappers (so
 * tagging a toolbar item never changes the layout), which have no box of their own - for
 * those it's the union of their rendered descendants' boxes. Null when nothing is visible.
 */
export function measureTarget(el: Element): Box | null {
	const rects: DOMRect[] = [];
	collectRects(el, rects);
	if (rects.length === 0) return null;
	const top = Math.min(...rects.map((r) => r.top));
	const left = Math.min(...rects.map((r) => r.left));
	const bottom = Math.max(...rects.map((r) => r.bottom));
	const right = Math.max(...rects.map((r) => r.right));
	return { top, left, width: right - left, height: bottom - top };
}

function collectRects(el: Element, out: DOMRect[]): void {
	if (getComputedStyle(el).display === 'contents') {
		for (const child of el.children) collectRects(child, out);
		return;
	}
	const r = el.getBoundingClientRect();
	if (r.width > 0 && r.height > 0) out.push(r);
}

/** Scrolls the first rendered part of a target into view (a `display: contents` wrapper can't scroll itself). */
export function scrollTargetIntoView(el: Element): void {
	const boxed = firstBoxed(el);
	boxed?.scrollIntoView({ block: 'nearest', inline: 'nearest' });
}

function firstBoxed(el: Element): Element | null {
	if (getComputedStyle(el).display !== 'contents') return el;
	for (const child of el.children) {
		const found = firstBoxed(child);
		if (found) return found;
	}
	return null;
}

/** Pads a target box and clips it to the viewport, so a tall target (the log table) still reads as one spotlight. */
export function spotlightBox(box: Box, viewportWidth: number, viewportHeight: number): Box {
	const top = Math.max(box.top - SPOTLIGHT_PADDING, 2);
	const left = Math.max(box.left - SPOTLIGHT_PADDING, 2);
	const bottom = Math.min(box.top + box.height + SPOTLIGHT_PADDING, viewportHeight - 2);
	const right = Math.min(box.left + box.width + SPOTLIGHT_PADDING, viewportWidth - 2);
	return { top, left, width: Math.max(right - left, 0), height: Math.max(bottom - top, 0) };
}

export function sameBox(a: Box | null, b: Box | null): boolean {
	if (a === null || b === null) return a === b;
	return a.top === b.top && a.left === b.left && a.width === b.width && a.height === b.height;
}
