<script lang="ts">
	// Strip under the nav on a public demo instance (ADR-0278), shown while browsing as its shared
	// read-only account. Not dismissible: it explains why every edit control is missing.
	import * as m from '#lib/paraglide/messages.js';
	import EyeIcon from '@lucide/svelte/icons/eye';
	import ExternalLinkIcon from '@lucide/svelte/icons/external-link';
</script>

<div class="flex items-center gap-2 border-b bg-primary/5 px-4 py-1.5 text-sm" role="status">
	<EyeIcon class="size-4 shrink-0 text-primary" />
	<span class="min-w-0 truncate">{m.demo_banner()}</span>
	<a
		href="https://github.com/aminparsa18/Flare.Net"
		target="_blank"
		rel="noopener noreferrer"
		class="ml-auto inline-flex shrink-0 items-center gap-1 font-medium text-primary hover:underline"
	>
		{m.demo_getFlare()}
		<ExternalLinkIcon class="size-3.5" />
	</a>
</div>
