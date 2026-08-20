<script lang="ts">
	// Thin wrapper over lottie-web - not a shadcn primitive, just kept alongside the
	// other ui/ components since nothing here is Logs-specific (LogTable's empty state
	// is only its first caller). lottie-web is loaded dynamically inside a
	// browser-guarded $effect rather than imported at module scope: it touches
	// `document` on load, which would break SvelteKit's SSR pass (this app renders
	// server-side via @sveltejs/adapter-node) if it ever ended up in the server bundle.
	import { browser } from '$app/environment';
	import { cn } from '$lib/utils.js';
	import type { AnimationItem } from 'lottie-web';

	let {
		src,
		loop = true,
		autoplay = true,
		class: className
	}: {
		/** Path to a Lottie JSON export - e.g. a static asset under /static served at "/foo.json". */
		src: string;
		loop?: boolean;
		autoplay?: boolean;
		class?: string;
	} = $props();

	let container = $state<HTMLDivElement | null>(null);

	$effect(() => {
		if (!browser || !container) return;
		// Snapshot props into the closure (not re-read inside the .then) so a prop
		// change that fires mid-load can't apply to a container the effect has since
		// moved on from.
		const target = container;
		const path = src;
		const animLoop = loop;
		const animAutoplay = autoplay;

		let anim: AnimationItem | undefined;
		let cancelled = false;

		import('lottie-web').then(({ default: lottie }) => {
			if (cancelled) return;
			anim = lottie.loadAnimation({
				container: target,
				renderer: 'svg',
				loop: animLoop,
				autoplay: animAutoplay,
				path
			});
		});

		return () => {
			cancelled = true;
			anim?.destroy();
		};
	});
</script>

<div bind:this={container} class={cn('[&_svg]:h-full [&_svg]:w-full', className)} aria-hidden="true"></div>
