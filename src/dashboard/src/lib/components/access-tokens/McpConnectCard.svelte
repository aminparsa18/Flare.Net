<script lang="ts">
	// Discoverability for the MCP endpoint (Flare.Api's /mcp): shows the URL and a ready-to-paste
	// client command next to where users create the Bearer token it needs. Docs: how-to/connect-ai-assistants.
	import { Button } from '$lib/components/ui/button';
	import { API_BASE_URL } from '$lib/api';
	import * as m from '$lib/paraglide/messages';

	const url = $derived(`${API_BASE_URL.replace(/\/$/, '')}/mcp`);
	const command = $derived(
		`claude mcp add --transport http flare ${url} --header "Authorization: Bearer <your-token>"`
	);
	let copied = $state(false);

	async function copy() {
		await navigator.clipboard.writeText(command);
		copied = true;
		setTimeout(() => (copied = false), 1500);
	}
</script>

<section class="mb-6 flex flex-col gap-2 rounded-lg border p-4 text-sm">
	<h3 class="font-medium">{m.mcpConnect_heading()}</h3>
	<p class="text-muted-foreground">{m.mcpConnect_description({ url })}</p>
	<pre class="bg-muted overflow-x-auto rounded-md p-3 text-xs"><code>{command}</code></pre>
	<div class="flex items-center justify-between gap-4">
		<a
			class="text-muted-foreground text-xs underline"
			href="https://github.com/aminparsa18/Flare.Net/blob/main/docs/how-to/connect-ai-assistants.md"
			target="_blank"
			rel="noreferrer">{m.mcpConnect_docs()}</a
		>
		<Button variant="outline" size="sm" onclick={copy}>
			{copied ? m.mcpConnect_copied() : m.mcpConnect_copy()}
		</Button>
	</div>
</section>
