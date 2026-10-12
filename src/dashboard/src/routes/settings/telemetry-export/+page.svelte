<script lang="ts">
	// Telemetry export (ADR-0155/0156/0157): managed OTLP forwarding targets and the S3 archive. Admin-only,
	// like the other Workspace pages (the API enforces it; the rail link is hidden for everyone else).
	import ForwardingSection from '$lib/components/telemetry-export/ForwardingSection.svelte';
	import ArchiveSection from '$lib/components/telemetry-export/ArchiveSection.svelte';
	import * as m from '$lib/paraglide/messages';
</script>

<svelte:head>
	<title>{m.telemetryExport_title()}</title>
</svelte:head>

<div class="flex flex-col gap-8 p-4">
	<div>
		<h1 class="text-lg font-semibold">{m.telemetryExport_heading()}</h1>
		<p class="text-muted-foreground text-sm">{m.telemetryExport_intro()}</p>
	</div>

	<ForwardingSection />
	<ArchiveSection />
</div>
