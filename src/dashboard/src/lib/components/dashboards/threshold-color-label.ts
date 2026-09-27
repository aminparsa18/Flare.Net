// Localized name of a named palette color - shared by ThresholdsPopover and LegendPopover,
// which both pick from `THRESHOLD_COLORS`. A function, not a lookup object, so it re-reads the
// live locale on every call (see time-range.ts's remarks).
import type { ThresholdColor } from '$lib/dashboards/thresholds';
import * as m from '$lib/paraglide/messages';

export function thresholdColorLabel(color: ThresholdColor): string {
	switch (color) {
		case 'red':
			return m.thresholdsPopover_colorRed();
		case 'orange':
			return m.thresholdsPopover_colorOrange();
		case 'yellow':
			return m.thresholdsPopover_colorYellow();
		case 'green':
			return m.thresholdsPopover_colorGreen();
		case 'blue':
			return m.thresholdsPopover_colorBlue();
		case 'purple':
			return m.thresholdsPopover_colorPurple();
	}
}
