<script lang="ts">
	// Picker for the built-in dashboard templates (see $lib/dashboards/templates.ts and
	// docs-internal/adr/0088-built-in-dashboard-templates.md). Installing creates an ordinary
	// editable dashboard and lands on it, same "land on what you just made" UX as Duplicate/Import.
	import { goto } from '$app/navigation';
	import * as Dialog from '$lib/components/ui/dialog';
	import { Button } from '$lib/components/ui/button';
	import { Spinner } from '$lib/components/ui/spinner';
	import { dashboardsContext } from '$lib/dashboards/context';
	import { dashboardPath } from '$lib/dashboards/page-paths';
	import { DASHBOARD_TEMPLATES, templatePanelCount, type DashboardTemplate } from '$lib/dashboards/templates';
	import * as m from '$lib/paraglide/messages';

	let { open = $bindable(false) }: { open?: boolean } = $props();

	const dashboards = dashboardsContext.get();

	let installingId = $state<string | null>(null);

	async function handleInstall(template: DashboardTemplate): Promise<void> {
		installingId = template.id;
		try {
			const id = await dashboards.installTemplate(template);
			if (id) {
				open = false;
				await goto(dashboardPath({ id }));
			}
		} finally {
			installingId = null;
		}
	}
</script>

<Dialog.Root bind:open>
	<Dialog.Content class="sm:max-w-lg">
		<Dialog.Header>
			<Dialog.Title>{m.dashboardTemplates_title()}</Dialog.Title>
			<Dialog.Description>{m.dashboardTemplates_description()}</Dialog.Description>
		</Dialog.Header>
		<ul class="space-y-2">
			{#each DASHBOARD_TEMPLATES as template (template.id)}
				<li class="flex items-start justify-between gap-3 rounded-md border p-3">
					<div class="min-w-0">
						<p class="text-sm font-medium">{template.name()}</p>
						<p class="text-muted-foreground text-xs">{template.description()}</p>
						<p class="text-muted-foreground mt-1 text-xs">{m.dashboardTemplates_panelCount({ count: templatePanelCount(template) })}</p>
					</div>
					<Button size="sm" variant="outline" disabled={installingId !== null} onclick={() => handleInstall(template)}>
						{#if installingId === template.id}<Spinner class="size-4" />{/if}
						{m.dashboardTemplates_install()}
					</Button>
				</li>
			{/each}
		</ul>
		{#if dashboards.importError}
			<p class="text-destructive text-sm">{dashboards.importError}</p>
		{/if}
	</Dialog.Content>
</Dialog.Root>
