import { findTarget, measureTarget } from '#lib/tour/dom.ts';
import type { TourDefinition, TourStep } from '#lib/tour/types.ts';

// Which tours (and which version of each) this browser has finished or skipped. Per-viewer
// convenience state, so localStorage rather than a synced pref - losing it only means the
// tour offers itself once more.
const SEEN_KEY = 'flare.tour.seen';

function readSeen(): Record<string, number> {
	try {
		const parsed: unknown = JSON.parse(localStorage.getItem(SEEN_KEY) ?? '{}');
		return parsed && typeof parsed === 'object' ? (parsed as Record<string, number>) : {};
	} catch {
		return {};
	}
}

function writeSeen(seen: Record<string, number>): void {
	try {
		localStorage.setItem(SEEN_KEY, JSON.stringify(seen));
	} catch {
		// Private window / blocked storage: the tour just offers itself again next time.
	}
}

function isAvailable(step: TourStep): boolean {
	if (!step.target) return true;
	const el = findTarget(step.target);
	return el !== null && measureTarget(el) !== null;
}

/** App-wide tour state - one tour at a time, rendered by TourOverlay in the root layout. */
class TourController {
	tour = $state<TourDefinition | null>(null);
	index = $state(0);

	readonly step = $derived(this.tour?.steps[this.index] ?? null);
	/** Position among the steps that are actually showable right now, for the "3 / 12" counter. */
	readonly progress = $derived.by(() => {
		if (!this.tour) return { current: 0, total: 0 };
		const steps = this.tour.steps.filter((s, i) => i === this.index || isAvailable(s));
		return { current: steps.indexOf(this.tour.steps[this.index]) + 1, total: steps.length };
	});
	readonly isFirst = $derived(this.tour !== null && this.#nextAvailable(this.index - 1, -1) === -1);
	readonly isLast = $derived(this.tour !== null && this.#nextAvailable(this.index + 1, 1) === -1);

	start(tour: TourDefinition): void {
		this.tour = tour;
		this.index = 0;
		const first = this.#nextAvailable(0, 1);
		if (first === -1) this.tour = null;
		else this.index = first;
	}

	/**
	 * Starts the tour only if this browser hasn't completed (or dismissed) this version of it.
	 * Skipped under automation (`navigator.webdriver`) so Playwright screenshot runs and e2e
	 * checks never find a card covering the page.
	 */
	maybeAutoStart(tour: TourDefinition): void {
		if (this.tour || navigator.webdriver) return;
		if ((readSeen()[tour.id] ?? 0) >= tour.version) return;
		this.start(tour);
	}

	next(): void {
		const i = this.#nextAvailable(this.index + 1, 1);
		if (i === -1) this.finish();
		else this.index = i;
	}

	back(): void {
		const i = this.#nextAvailable(this.index - 1, -1);
		if (i !== -1) this.index = i;
	}

	/** Ends the tour (Done, Skip or Escape alike) and remembers it so it doesn't auto-start again. */
	finish(): void {
		if (!this.tour) return;
		writeSeen({ ...readSeen(), [this.tour.id]: this.tour.version });
		this.tour = null;
	}

	/** Closes `tour` if it's the one running, without marking it seen - its page is going away mid-tour. */
	cancel(tour: TourDefinition): void {
		if (this.tour?.id === tour.id) this.tour = null;
	}

	#nextAvailable(from: number, dir: 1 | -1): number {
		const steps = this.tour?.steps ?? [];
		for (let i = from; i >= 0 && i < steps.length; i += dir) {
			if (isAvailable(steps[i])) return i;
		}
		return -1;
	}
}

export const tourController = new TourController();
