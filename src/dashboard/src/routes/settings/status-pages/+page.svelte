<script lang="ts">
	// Settings > Workspace > Status pages (Admin): publish a read-only page of monitor and SLO health at
	// /status/{slug}. Self-contained - a handful of pages needs no shared state class. ADR-0158.
	import { onMount } from 'svelte';
	import * as Table from '$lib/components/ui/table';
	import * as Empty from '$lib/components/ui/empty';
	import * as Dialog from '$lib/components/ui/dialog';
	import * as Select from '$lib/components/ui/select';
	import { Button } from '$lib/components/ui/button';
	import { Input } from '$lib/components/ui/input';
	import { Textarea } from '$lib/components/ui/textarea';
	import { Spinner } from '$lib/components/ui/spinner';
	import { Switch } from '$lib/components/ui/switch';
	import {
		createStatusPage,
		deleteStatusPage,
		isValidSlug,
		listStatusPages,
		updateStatusPage,
		type StatusPage,
		type StatusPageComponent,
		type StatusPageRequest
	} from '$lib/status-pages-api';
	import IncidentsDialog from './IncidentsDialog.svelte';
	import { listSyntheticMonitors } from '$lib/synthetic-monitors-api';
	import { listSlos } from '$lib/slos-api';
	import { listNotificationChannels } from '$lib/notification-channels-api';
	import { withBase } from '$lib/paths';
	import * as m from '$lib/paraglide/messages';
	import PlusIcon from '@lucide/svelte/icons/plus';
	import PencilIcon from '@lucide/svelte/icons/pencil';
	import Trash2Icon from '@lucide/svelte/icons/trash-2';
	import ExternalLinkIcon from '@lucide/svelte/icons/external-link';
	import GlobeIcon from '@lucide/svelte/icons/globe';
	import XIcon from '@lucide/svelte/icons/x';
	import MegaphoneIcon from '@lucide/svelte/icons/megaphone';

	let pages = $state<StatusPage[]>([]);
	let loading = $state(true);
	let error = $state<string | null>(null);
	// What a component can point at: [kind:id] -> label.
	let options = $state<{ key: string; kind: 'Monitor' | 'Slo'; refId: string; label: string }[]>([]);
	// Channels that suit an announcement (not PagerDuty, Jira and the like): id -> name.
	let channelOptions = $state<{ id: string; name: string }[]>([]);
	const SUBSCRIBER_TYPES = ['Webhook', 'Telegram', 'Email', 'Teams', 'Discord'];

	// Form: null = closed, 'new' = create, otherwise the page being edited.
	let target = $state<StatusPage | 'new' | null>(null);
	let slug = $state('');
	let title = $state('');
	let description = $state('');
	let enabled = $state(false);
	let components = $state<StatusPageComponent[]>([]);
	let subscribers = $state<string[]>([]);
	let saving = $state(false);
	let saveError = $state<string | null>(null);
	let incidentsFor = $state<StatusPage | null>(null);

	const slugValid = $derived(isValidSlug(slug));
	const canSave = $derived(slugValid && title.trim().length > 0 && components.every((c) => c.name.trim().length > 0));
	const unsubscribed = $derived(channelOptions.filter((c) => !subscribers.includes(c.id)));
	const unused = $derived(options.filter((o) => !components.some((c) => c.kind === o.kind && c.refId === o.refId)));

	async function load(): Promise<void> {
		loading = true;
		try {
			const [list, monitors, slos, channels] = await Promise.all([listStatusPages(), listSyntheticMonitors(), listSlos(), listNotificationChannels()]);
			pages = list;
			channelOptions = channels.channels.filter((c) => SUBSCRIBER_TYPES.includes(c.type)).map((c) => ({ id: c.id, name: c.name }));
			options = [
				...monitors.map((x) => ({ key: `Monitor:${x.id}`, kind: 'Monitor' as const, refId: x.id, label: x.name })),
				...slos.map((x) => ({ key: `Slo:${x.id}`, kind: 'Slo' as const, refId: x.id, label: x.name }))
			];
			error = null;
		} catch (err) {
			error = err instanceof Error ? err.message : String(err);
		} finally {
			loading = false;
		}
	}

	onMount(() => void load());

	function openForm(next: StatusPage | 'new'): void {
		target = next;
		saveError = null;
		const page = next === 'new' ? null : next;
		slug = page?.slug ?? '';
		title = page?.title ?? '';
		description = page?.description ?? '';
		enabled = page?.enabled ?? false;
		components = page ? page.components.map((c) => ({ ...c })) : [];
		subscribers = page ? [...page.subscriberChannelIds] : [];
	}

	function labelFor(c: StatusPageComponent): string {
		return options.find((o) => o.kind === c.kind && o.refId === c.refId)?.label ?? m.statusPages_missingSource();
	}

	async function save(): Promise<void> {
		if (target === null) return;
		const request: StatusPageRequest = { slug: slug.trim(), title: title.trim(), description: description.trim(), enabled, components, subscriberChannelIds: subscribers };
		saving = true;
		saveError = null;
		try {
			if (target === 'new') await createStatusPage(request);
			else await updateStatusPage(target.id, request);
			target = null;
			await load();
		} catch (err) {
			saveError = err instanceof Error ? err.message : String(err);
		} finally {
			saving = false;
		}
	}

	async function toggle(page: StatusPage, value: boolean): Promise<void> {
		try {
			await updateStatusPage(page.id, { slug: page.slug, title: page.title, description: page.description, enabled: value, components: page.components });
			await load();
		} catch (err) {
			error = err instanceof Error ? err.message : String(err);
		}
	}

	async function remove(page: StatusPage): Promise<void> {
		if (!confirm(m.statusPages_deleteConfirm({ title: page.title }))) return;
		try {
			await deleteStatusPage(page.id);
			await load();
		} catch (err) {
			error = err instanceof Error ? err.message : String(err);
		}
	}

	function channelName(id: string): string {
		return channelOptions.find((c) => c.id === id)?.name ?? m.statusPages_missingChannel();
	}

	function addComponent(key: string): void {
		const option = options.find((o) => o.key === key);
		if (option) components = [...components, { name: option.label, kind: option.kind, refId: option.refId }];
	}
</script>

<svelte:head>
	<title>{m.statusPages_heading()}</title>
</svelte:head>

<div class="flex items-center justify-between border-b px-4 py-3">
	<div>
		<h1 class="text-sm font-semibold">{m.statusPages_heading()}</h1>
		<p class="text-muted-foreground text-xs">{m.statusPages_description()}</p>
	</div>
	<Button size="sm" onclick={() => openForm('new')}>
		<PlusIcon data-icon="inline-start" />
		{m.statusPages_new()}
	</Button>
</div>

{#if loading && pages.length === 0}
	<div class="flex flex-1 items-center justify-center"><Spinner /></div>
{:else if error}
	<div class="flex flex-1 items-center justify-center"><p class="text-destructive text-sm">{error}</p></div>
{:else if pages.length === 0}
	<Empty.Root class="flex-1">
		<Empty.Header>
			<Empty.Media><GlobeIcon class="text-muted-foreground size-8" /></Empty.Media>
			<Empty.Title>{m.statusPages_emptyTitle()}</Empty.Title>
			<Empty.Description>{m.statusPages_emptyDescription()}</Empty.Description>
		</Empty.Header>
		<Empty.Content>
			<Button size="sm" onclick={() => openForm('new')}>
				<PlusIcon data-icon="inline-start" />
				{m.statusPages_new()}
			</Button>
		</Empty.Content>
	</Empty.Root>
{:else}
	<div class="min-h-0 flex-1 overflow-y-auto">
		<Table.Root>
			<Table.Header>
				<Table.Row>
					<Table.Head>{m.statusPages_colTitle()}</Table.Head>
					<Table.Head>{m.statusPages_colUrl()}</Table.Head>
					<Table.Head>{m.statusPages_colComponents()}</Table.Head>
					<Table.Head>{m.statusPages_colPublished()}</Table.Head>
					<Table.Head class="text-right">{m.statusPages_colActions()}</Table.Head>
				</Table.Row>
			</Table.Header>
			<Table.Body>
				{#each pages as page (page.id)}
					<Table.Row>
						<Table.Cell class="font-medium">
							{page.title}
							{#if page.description}<p class="text-muted-foreground font-normal">{page.description}</p>{/if}
						</Table.Cell>
						<Table.Cell class="font-mono text-xs">/status/{page.slug}</Table.Cell>
						<Table.Cell class="text-muted-foreground">{page.components.length}</Table.Cell>
						<Table.Cell><Switch size="sm" checked={page.enabled} onCheckedChange={(v) => toggle(page, v)} /></Table.Cell>
						<Table.Cell class="text-right">
							<Button variant="ghost" size="icon-sm" title={m.statusPages_actionOpen()} href={withBase(`/status/${page.slug}`)} target="_blank" rel="noopener">
								<ExternalLinkIcon />
							</Button>
							<Button variant="ghost" size="icon-sm" title={m.statusPages_actionIncidents()} onclick={() => (incidentsFor = page)}><MegaphoneIcon /></Button>
							<Button variant="ghost" size="icon-sm" title={m.statusPages_actionEdit()} onclick={() => openForm(page)}><PencilIcon /></Button>
							<Button variant="ghost" size="icon-sm" class="text-destructive hover:text-destructive" title={m.statusPages_actionDelete()} onclick={() => remove(page)}>
								<Trash2Icon />
							</Button>
						</Table.Cell>
					</Table.Row>
				{/each}
			</Table.Body>
		</Table.Root>
	</div>
{/if}

{#if incidentsFor}
	<IncidentsDialog page={incidentsFor} onclose={() => (incidentsFor = null)} />
{/if}

<Dialog.Root open={target !== null} onOpenChange={(o) => !o && (target = null)}>
	<Dialog.Content class="max-h-[90vh] overflow-y-auto sm:max-w-xl">
		<Dialog.Header>
			<Dialog.Title>{target === 'new' ? m.statusPages_new() : m.statusPages_edit()}</Dialog.Title>
			<Dialog.Description>{m.statusPages_formDescription()}</Dialog.Description>
		</Dialog.Header>

		<div class="flex flex-col gap-4">
			<div class="flex flex-col gap-1">
				<span class="text-xs font-medium">{m.statusPages_titleLabel()}</span>
				<Input bind:value={title} maxlength={120} />
			</div>
			<div class="flex flex-col gap-1">
				<span class="text-xs font-medium">{m.statusPages_slugLabel()}</span>
				<Input bind:value={slug} maxlength={64} placeholder="status" class="font-mono" aria-invalid={slug !== '' && !slugValid} />
				<span class="text-xs {slug === '' || slugValid ? 'text-muted-foreground' : 'text-destructive'}">{m.statusPages_slugHint()}</span>
			</div>
			<div class="flex flex-col gap-1">
				<span class="text-xs font-medium">{m.statusPages_descriptionLabel()}</span>
				<Textarea bind:value={description} rows={2} maxlength={1000} />
			</div>

			<div class="flex flex-col gap-2">
				<span class="text-xs font-medium">{m.statusPages_componentsLabel()}</span>
				{#each components as component, i (component.kind + component.refId)}
					<div class="flex items-center gap-2">
						<Input bind:value={component.name} maxlength={100} class="flex-1" aria-label={m.statusPages_componentName()} />
						<span class="text-muted-foreground w-40 truncate text-xs" title={labelFor(component)}>
							{component.kind === 'Monitor' ? m.statusPages_kindMonitor() : m.statusPages_kindSlo()}: {labelFor(component)}
						</span>
						<Button variant="ghost" size="icon-sm" title={m.statusPages_removeComponent()} onclick={() => (components = components.filter((_, j) => j !== i))}>
							<XIcon />
						</Button>
					</div>
				{/each}
				{#if unused.length > 0}
					<Select.Root type="single" value="" onValueChange={addComponent}>
						<Select.Trigger class="w-full">{m.statusPages_addComponent()}</Select.Trigger>
						<Select.Content>
							{#each unused as option (option.key)}
								<Select.Item value={option.key} label={option.label}>
									{option.kind === 'Monitor' ? m.statusPages_kindMonitor() : m.statusPages_kindSlo()}: {option.label}
								</Select.Item>
							{/each}
						</Select.Content>
					</Select.Root>
				{:else if options.length === 0}
					<span class="text-muted-foreground text-xs">{m.statusPages_noSources()}</span>
				{/if}
				<span class="text-muted-foreground text-xs">{m.statusPages_componentsHint()}</span>
			</div>

			<div class="flex flex-col gap-2">
				<span class="text-xs font-medium">{m.statusPages_subscribersLabel()}</span>
				{#each subscribers as id (id)}
					<div class="flex items-center gap-2">
						<span class="flex-1 truncate text-sm">{channelName(id)}</span>
						<Button variant="ghost" size="icon-sm" title={m.statusPages_removeSubscriber()} onclick={() => (subscribers = subscribers.filter((s) => s !== id))}>
							<XIcon />
						</Button>
					</div>
				{/each}
				{#if unsubscribed.length > 0}
					<Select.Root type="single" value="" onValueChange={(id) => id && (subscribers = [...subscribers, id])}>
						<Select.Trigger class="w-full">{m.statusPages_addSubscriber()}</Select.Trigger>
						<Select.Content>
							{#each unsubscribed as channel (channel.id)}
								<Select.Item value={channel.id} label={channel.name}>{channel.name}</Select.Item>
							{/each}
						</Select.Content>
					</Select.Root>
				{:else if channelOptions.length === 0}
					<span class="text-muted-foreground text-xs">{m.statusPages_noChannels()}</span>
				{/if}
				<span class="text-muted-foreground text-xs">{m.statusPages_subscribersHint()}</span>
			</div>

			<label class="flex items-center gap-2 text-sm">
				<Switch size="sm" checked={enabled} onCheckedChange={(v) => (enabled = v)} />
				{m.statusPages_publishLabel()}
			</label>
			{#if enabled}
				<p class="text-xs text-amber-600 dark:text-amber-400">{m.statusPages_publicWarning()}</p>
			{/if}

			{#if saveError}<p class="text-destructive text-xs">{saveError}</p>{/if}
		</div>

		<Dialog.Footer>
			<Button variant="outline" size="sm" onclick={() => (target = null)}>{m.alertRuleForm_cancel()}</Button>
			<Button size="sm" onclick={save} disabled={!canSave || saving}>
				{#if saving}<Spinner class="size-3.5" />{/if}
				{target === 'new' ? m.statusPages_create() : m.alertRuleForm_saveChanges()}
			</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>
