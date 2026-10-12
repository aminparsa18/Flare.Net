<script lang="ts">
	// Admin dialog for one status page's incidents: open an incident, post updates to its timeline, delete it.
	// Self-contained like the page that hosts it. ADR-0159.
	import * as Dialog from '$lib/components/ui/dialog';
	import * as Select from '$lib/components/ui/select';
	import { Button } from '$lib/components/ui/button';
	import { Checkbox } from '$lib/components/ui/checkbox';
	import { Input } from '$lib/components/ui/input';
	import { Textarea } from '$lib/components/ui/textarea';
	import { Spinner } from '$lib/components/ui/spinner';
	import {
		INCIDENT_STATUSES,
		deleteStatusIncident,
		listStatusIncidents,
		openStatusIncident,
		postStatusIncidentUpdate,
		type StatusIncident,
		type StatusIncidentStatus,
		type StatusPage
	} from '$lib/status-pages-api';
	import { formatDateTime } from '$lib/time/format';
	import * as m from '$lib/paraglide/messages';
	import Trash2Icon from '@lucide/svelte/icons/trash-2';

	let { page, onclose }: { page: StatusPage; onclose: () => void } = $props();

	let incidents = $state<StatusIncident[]>([]);
	let loading = $state(true);
	let error = $state<string | null>(null);
	let busy = $state(false);

	let newTitle = $state('');
	let newStatus = $state<StatusIncidentStatus>('Investigating');
	let newMessage = $state('');
	let newComponents = $state<string[]>([]);
	// Draft update per incident id.
	let drafts = $state<Record<string, { status: StatusIncidentStatus; message: string; components: string[] }>>({});

	const LABEL: Record<StatusIncidentStatus, () => string> = {
		Investigating: () => m.statusIncidents_investigating(),
		Identified: () => m.statusIncidents_identified(),
		Monitoring: () => m.statusIncidents_monitoring(),
		Resolved: () => m.statusIncidents_resolved()
	};

	async function load(): Promise<void> {
		try {
			incidents = await listStatusIncidents(page.id);
			error = null;
		} catch (err) {
			error = err instanceof Error ? err.message : String(err);
		} finally {
			loading = false;
		}
	}

	$effect(() => {
		void load();
	});

	async function run(action: () => Promise<void>): Promise<void> {
		busy = true;
		try {
			await action();
			error = null;
			await load();
		} catch (err) {
			error = err instanceof Error ? err.message : String(err);
		} finally {
			busy = false;
		}
	}

	const draft = (i: StatusIncident) => (drafts[i.id] ??= { status: i.status, message: '', components: [...i.components] });

	const openIncident = () =>
		run(async () => {
			await openStatusIncident(page.id, { title: newTitle.trim(), status: newStatus, message: newMessage.trim(), components: newComponents });
			newTitle = '';
			newMessage = '';
			newStatus = 'Investigating';
			newComponents = [];
		});

	const postUpdate = (i: StatusIncident) =>
		run(async () => {
			const d = draft(i);
			await postStatusIncidentUpdate(page.id, i.id, { status: d.status, message: d.message.trim(), components: d.components });
			delete drafts[i.id];
		});

	const remove = (i: StatusIncident) => {
		if (!confirm(m.statusIncidents_deleteConfirm({ title: i.title }))) return Promise.resolve();
		return run(() => deleteStatusIncident(page.id, i.id));
	};
</script>

{#snippet statusSelect(value: StatusIncidentStatus, onChange: (v: StatusIncidentStatus) => void)}
	<Select.Root type="single" {value} onValueChange={(v) => onChange(v as StatusIncidentStatus)}>
		<Select.Trigger class="w-44">{LABEL[value]()}</Select.Trigger>
		<Select.Content>
			{#each INCIDENT_STATUSES as status (status)}
				<Select.Item value={status} label={LABEL[status]()}>{LABEL[status]()}</Select.Item>
			{/each}
		</Select.Content>
	</Select.Root>
{/snippet}

{#snippet componentPicker(selected: string[], onChange: (v: string[]) => void)}
	{#if page.components.length > 0}
		<div class="flex flex-col gap-1">
			<span class="text-muted-foreground text-xs">{m.statusIncidents_affects()}</span>
			<div class="flex flex-wrap gap-x-4 gap-y-1">
				{#each page.components as component (component.refId)}
					<label class="flex items-center gap-2 text-sm">
						<Checkbox
							checked={selected.includes(component.refId)}
							onCheckedChange={(v) => onChange(v === true ? [...selected, component.refId] : selected.filter((id) => id !== component.refId))}
						/>
						{component.name}
					</label>
				{/each}
			</div>
		</div>
	{/if}
{/snippet}

<Dialog.Root open onOpenChange={(o) => !o && onclose()}>
	<Dialog.Content class="max-h-[90vh] overflow-y-auto sm:max-w-2xl">
		<Dialog.Header>
			<Dialog.Title>{m.statusIncidents_heading({ title: page.title })}</Dialog.Title>
			<Dialog.Description>{m.statusIncidents_description()}</Dialog.Description>
		</Dialog.Header>

		<div class="flex flex-col gap-3 rounded-lg border p-3">
			<span class="text-sm font-medium">{m.statusIncidents_new()}</span>
			<Input bind:value={newTitle} maxlength={200} placeholder={m.statusIncidents_titleLabel()} aria-label={m.statusIncidents_titleLabel()} />
			<Textarea bind:value={newMessage} rows={2} maxlength={4000} placeholder={m.statusIncidents_messageLabel()} aria-label={m.statusIncidents_messageLabel()} />
			{@render componentPicker(newComponents, (v) => (newComponents = v))}
			<div class="flex items-center justify-between gap-2">
				{@render statusSelect(newStatus, (v) => (newStatus = v))}
				<Button size="sm" onclick={openIncident} disabled={busy || !newTitle.trim() || !newMessage.trim()}>{m.statusIncidents_open()}</Button>
			</div>
		</div>

		{#if error}<p class="text-destructive text-xs">{error}</p>{/if}

		{#if loading}
			<div class="flex justify-center py-4"><Spinner /></div>
		{:else if incidents.length === 0}
			<p class="text-muted-foreground text-sm">{m.statusIncidents_empty()}</p>
		{:else}
			{#each incidents as incident (incident.id)}
				{@const d = draft(incident)}
				<div class="flex flex-col gap-2 rounded-lg border p-3">
					<div class="flex items-center justify-between gap-2">
						<span class="font-medium">{incident.title}</span>
						<span class="flex items-center gap-1">
							<span class="text-muted-foreground text-xs">{LABEL[incident.status]()}</span>
							<Button variant="ghost" size="icon-sm" class="text-destructive hover:text-destructive" title={m.statusIncidents_delete()} onclick={() => remove(incident)}>
								<Trash2Icon />
							</Button>
						</span>
					</div>
					<ol class="flex flex-col gap-1 text-sm">
						{#each [...incident.updates].reverse() as update (update.at)}
							<li>
								<span class="font-medium">{LABEL[update.status]()}</span>
								<span class="text-muted-foreground text-xs"> - {formatDateTime(update.at)}</span>
								<p class="whitespace-pre-wrap">{update.message}</p>
							</li>
						{/each}
					</ol>
					<Textarea bind:value={d.message} rows={2} maxlength={4000} placeholder={m.statusIncidents_messageLabel()} aria-label={m.statusIncidents_messageLabel()} />
					{@render componentPicker(d.components, (v) => (d.components = v))}
					<div class="flex items-center justify-between gap-2">
						{@render statusSelect(d.status, (v) => (d.status = v))}
						<Button size="sm" variant="outline" onclick={() => postUpdate(incident)} disabled={busy || !d.message.trim()}>{m.statusIncidents_postUpdate()}</Button>
					</div>
				</div>
			{/each}
		{/if}
	</Dialog.Content>
</Dialog.Root>
