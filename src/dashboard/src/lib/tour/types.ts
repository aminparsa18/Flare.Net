import type { Side } from '#lib/tour/dom.ts';

/**
 * One stop of a guided tour. `target` is the value of a `data-tour="..."` attribute on the
 * page; a step whose target is absent or has no visible box (a feature that's switched off,
 * a collapsed sidebar, ...) is skipped rather than pointing at nothing. No `target` means a
 * centered card with the whole page dimmed - used for the intro/outro steps.
 */
export interface TourStep {
	target?: string;
	title: () => string;
	body: () => string;
	/** Preferred side of the card relative to the target; it still flips when there's no room. */
	side?: Side;
}

export interface TourDefinition {
	/** Stable id, used as the "already seen" key and the `?tour=<id>` replay link. */
	id: string;
	/** Base-stripped route the tour runs on. Omit for a dynamic route and give `pathPattern` instead. */
	path?: string;
	/**
	 * Matches a dynamic route such as `/traces/<id>`. These pages have no fixed URL to link to, so
	 * Settings can't offer Replay for them - the tour is reachable from the page's own menu instead.
	 */
	pathPattern?: RegExp;
	/** Shown in Settings > Explorer > Guided tours. */
	name: () => string;
	/** Bump to show the tour again to people who've completed an older version. */
	version: number;
	steps: TourStep[];
}
