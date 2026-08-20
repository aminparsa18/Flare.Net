// Svelte action: smoothly resizes an element whenever its content's natural height
// changes, instead of snapping. Originally inlined in MetricChart.svelte (see git blame)
// for the metric-switch fade, whose content-swap changes the box's natural height (a
// wrapped multi-row legend collapsing to a plain 2-line one, say) with no animation of
// its own - a plain `{#key}`+fade only animates opacity, so the *container* still jumps
// to the new height the instant swapped-in content mounts. Promoted here once a second
// consumer (VolumeChart's collapse toggle) needed the exact same behavior - that file's
// own comment already flagged "no shared $lib/actions home yet... until a second
// consumer needs it".
//
// A FLIP-style animation via ResizeObserver + the Web Animations API: capture the
// height *before* this fires (the browser has already resized the box by the time the
// observer callback runs), then explicitly animate from that captured value to the new
// one.
export function animateHeight(node: HTMLElement, durationMs = 300) {
	let prevHeight = node.getBoundingClientRect().height;
	const observer = new ResizeObserver(() => {
		const nextHeight = node.getBoundingClientRect().height;
		if (Math.abs(nextHeight - prevHeight) > 0.5) {
			node.animate([{ height: `${prevHeight}px` }, { height: `${nextHeight}px` }], { duration: durationMs, easing: 'ease' });
		}
		prevHeight = nextHeight;
	});
	observer.observe(node);
	return {
		destroy() {
			observer.disconnect();
		}
	};
}
