<script lang="ts">
	// Renders whichever tour tourController is running (see #lib/tour/). Mounted once in the
	// root layout so a page only has to tag its sections with data-tour="..." and call
	// tourController.start/maybeAutoStart.
	//
	// Three layers, all position: fixed:
	//   1. a transparent click-blocker, so the page underneath can't be changed mid-tour;
	//   2. the spotlight - a rounded box over the target whose huge box-shadow dims
	//      everything else; its top/left/size transition, so it glides from one target to
	//      the next (a centered intro/outro step is the same box shrunk to a point);
	//   3. the step card - the app's own Popover (bits-ui), anchored to an invisible proxy
	//      element sitting exactly on the spotlight. bits-ui/Floating UI does placement,
	//      flipping, focus trapping and Escape; updatePositionStrategy="always" follows the
	//      proxy as the target moves (live-tail rows arriving, toolbar re-wrapping).
	//
	// The target's box is re-measured every animation frame while a tour is open - one
	// querySelector + getBoundingClientRect per frame, only for as long as the tour runs.
	import * as Popover from '#lib/components/ui/popover/index.ts';
	import { Button } from '#lib/components/ui/button/index.ts';
	import { tourController as tour } from '#lib/tour/tour.svelte.ts';
	import { findTarget, measureTarget, sameBox, scrollTargetIntoView, spotlightBox, type Box } from '#lib/tour/dom.ts';
	import * as m from '#lib/paraglide/messages.js';
	import XIcon from '@lucide/svelte/icons/x';
	import ArrowLeftIcon from '@lucide/svelte/icons/arrow-left';
	import ArrowRightIcon from '@lucide/svelte/icons/arrow-right';

	// Where a centered (no-target) card hangs from: a point a little above the middle, so the
	// card below it reads as centered.
	const CENTER_OFFSET_Y = 110;

	let box = $state<Box | null>(null);
	let viewport = $state({ width: 0, height: 0 });
	let anchor = $state<HTMLElement | null>(null);
	let nextButton = $state<HTMLButtonElement | null>(null);

	const step = $derived(tour.step);
	const spot = $derived<Box>(
		box ?? { top: viewport.height / 2 - CENTER_OFFSET_Y, left: viewport.width / 2, width: 0, height: 0 }
	);
	const percent = $derived(tour.progress.total > 0 ? (tour.progress.current / tour.progress.total) * 100 : 0);

	$effect(() => {
		const target = step?.target;
		if (!target) return;
		const el = findTarget(target);
		if (el) scrollTargetIntoView(el);
	});

	$effect(() => {
		if (!tour.tour) return;
		const target = step?.target;
		let last: Box | null = null;
		let frame = 0;
		const tick = () => {
			const el = target ? findTarget(target) : null;
			const raw = el ? measureTarget(el) : null;
			const next = raw ? spotlightBox(raw, window.innerWidth, window.innerHeight) : null;
			if (!sameBox(next, last)) {
				last = next;
				box = next;
			}
			if (viewport.width !== window.innerWidth || viewport.height !== window.innerHeight) {
				viewport = { width: window.innerWidth, height: window.innerHeight };
			}
			frame = requestAnimationFrame(tick);
		};
		tick();
		return () => cancelAnimationFrame(frame);
	});

	function handleKeydown(e: KeyboardEvent): void {
		if (!tour.tour) return;
		if (e.key === 'ArrowRight') {
			e.preventDefault();
			tour.next();
		} else if (e.key === 'ArrowLeft') {
			e.preventDefault();
			tour.back();
		}
	}
</script>

<svelte:window onkeydown={handleKeydown} />

{#if tour.tour && step}
	<div class="fixed inset-0 z-[60]" aria-hidden="true"></div>
	<div
		class="tour-spotlight pointer-events-none fixed z-[60] rounded-md motion-safe:transition-[top,left,width,height] motion-safe:duration-300 motion-safe:ease-out"
		class:has-target={box !== null}
		style:top="{spot.top}px"
		style:left="{spot.left}px"
		style:width="{spot.width}px"
		style:height="{spot.height}px"
		aria-hidden="true"
	></div>
	<div
		bind:this={anchor}
		class="pointer-events-none fixed"
		style:top="{spot.top}px"
		style:left="{spot.left}px"
		style:width="{spot.width}px"
		style:height="{spot.height}px"
		aria-hidden="true"
	></div>

	{#if anchor}
		<Popover.Root open onOpenChange={(open) => !open && tour.finish()}>
			{#key tour.index}
				<Popover.Content
					customAnchor={anchor}
					side={box ? (step.side ?? 'bottom') : 'bottom'}
					align={box ? 'start' : 'center'}
					sideOffset={box ? 12 : 0}
					collisionPadding={12}
					updatePositionStrategy="always"
					interactOutsideBehavior="ignore"
					onOpenAutoFocus={(e) => {
						e.preventDefault();
						nextButton?.focus();
					}}
					class="z-[61] w-80 gap-3 p-4 text-sm shadow-2xl ring-foreground/15"
				>
					<div class="flex items-start justify-between gap-3">
						<div class="flex flex-col gap-1">
							<span class="text-muted-foreground text-xs tabular-nums">
								{m.tour_progress({ current: tour.progress.current, total: tour.progress.total })}
							</span>
							<Popover.Title class="text-sm font-semibold">{step.title()}</Popover.Title>
						</div>
						<Button variant="ghost" size="icon-sm" class="-mt-1 -mr-1 shrink-0" aria-label={m.tour_close()} onclick={() => tour.finish()}>
							<XIcon />
						</Button>
					</div>
					<Popover.Description class="text-sm leading-relaxed">{step.body()}</Popover.Description>
					<div class="bg-muted h-1 overflow-hidden rounded-full" aria-hidden="true">
						<div class="bg-primary h-full rounded-full motion-safe:transition-[width] motion-safe:duration-300" style:width="{percent}%"></div>
					</div>
					<div class="flex items-center justify-between gap-2">
						{#if tour.isLast}
							<span></span>
						{:else}
							<Button variant="ghost" size="sm" class="text-muted-foreground" onclick={() => tour.finish()}>{m.tour_skip()}</Button>
						{/if}
						<div class="flex items-center gap-2">
							{#if !tour.isFirst}
								<Button variant="outline" size="sm" onclick={() => tour.back()}>
									<ArrowLeftIcon data-icon="inline-start" />
									{m.tour_back()}
								</Button>
							{/if}
							<Button size="sm" bind:ref={nextButton} onclick={() => tour.next()}>
								{#if tour.isLast}
									{m.tour_done()}
								{:else}
									{tour.isFirst ? m.tour_start() : m.tour_next()}
									<ArrowRightIcon data-icon="inline-end" />
								{/if}
							</Button>
						</div>
					</div>
				</Popover.Content>
			{/key}
		</Popover.Root>
	{/if}
{/if}

<style>
	/* The outline ring and the page dimming are one box-shadow - a Tailwind ring-* utility
	   would be another box-shadow and the two would override each other. Dark mode dims
	   harder: 60% black over an already near-black page is barely visible. */
	.tour-spotlight {
		--tour-dim: rgb(0 0 0 / 0.55);
		box-shadow: 0 0 0 9999px var(--tour-dim);
	}
	.tour-spotlight.has-target {
		box-shadow:
			0 0 0 2px var(--primary),
			0 0 0 6px color-mix(in oklab, var(--primary) 25%, transparent),
			0 0 0 9999px var(--tour-dim);
	}
	:global(.dark) .tour-spotlight {
		--tour-dim: rgb(0 0 0 / 0.78);
	}
</style>
