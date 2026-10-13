<script lang="ts">
	// Orleans' own logo (docs/site/public/favicon.svg in github.com/dotnet/orleans, MIT),
	// recolored to currentColor so it follows the same text-muted-foreground tint as the
	// lucide icons next to it. The original is two purples (the ring, and two lighter
	// side faces); those two shades are kept as full and 0.7 opacity of the one tint so the
	// shape still reads. Same vendoring reason as DaprIcon.svelte: lucide has no brand logos.
	let { class: className }: { class?: string } = $props();
</script>

<svg
	xmlns="http://www.w3.org/2000/svg"
	viewBox="0 0 50.000427 57.736"
	fill="currentColor"
	class={className}
	aria-hidden="true"
>
	<g transform="matrix(0.17548936,0,0,0.17548936,-44.844551,-60.105106)">
		<g transform="translate(255.54,342.5)">
			<path
				fill-rule="evenodd"
				d="m 142.46,0 142.46,82.25 v 164.5 L 142.46,329 0,246.75 V 82.25 Z m -0.48781,71 80.973,46.75 v 93.5 l -80.973,46.75 -80.973,-46.75 v -93.5 z"
			/>
			<polygon
				opacity="0.7"
				points="222.46,117.9 222.85,211.35 284.37,246.83 284.37,82.264 142,0 142,71.117"
			/>
			<polygon
				opacity="0.7"
				transform="rotate(180,71.185,205.415)"
				points="80.46,199.9 80.849,293.35 142.37,328.83 142.37,164.26 0,82 0,153.12"
			/>
		</g>
	</g>
</svg>
