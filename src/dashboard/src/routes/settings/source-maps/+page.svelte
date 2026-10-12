<script lang="ts">
	// Source maps (ADR-0152): read-mostly inventory of what CI has uploaded, grouped by release
	// (service + version), with delete per release or per bundle. Uploading stays a CLI/CI job.
	import { onMount } from 'svelte';
	import * as Table from '$lib/components/ui/table';
	import * as Empty from '$lib/components/ui/empty';
	import { Button } from '$lib/components/ui/button';
	import { Spinner } from '$lib/components/ui/spinner';
	import { deleteSourceMaps, listSourceMaps, type SourceMapDto } from '$lib/source-maps-api';
	import { formatBytes } from '$lib/ingestion/format';
	import { formatDateTime } from '$lib/time/format';
	import { authContext } from '$lib/auth/context';
	import FileCodeIcon from '@lucide/svelte/icons/file-code';
	import ChevronRightIcon from '@lucide/svelte/icons/chevron-right';
	import Trash2Icon from '@lucide/svelte/icons/trash-2';
	import * as m from '$lib/paraglide/messages';

	const auth = authContext.get();
	const canDelete = $derived(!auth.authEnabled || auth.currentUser?.role === 'Admin');

	let maps = $state.raw<SourceMapDto[]>([]);
	let loading = $state(true);
	let error = $state<string | null>(null);
	let expanded = $state<Record<string, boolean>>({});

	interface Release {
		key: string;
		serviceName: string;
		version: string;
		bundles: SourceMapDto[];
		sizeBytes: number;
		uploadedAt: string;
	}

	const releases = $derived.by(() => {
		const byKey = new Map<string, Release>();
		for (const map of maps) {
			const key = `${map.serviceName}\n${map.version}`;
			const release = byKey.get(key) ?? { key, serviceName: map.serviceName, version: map.version, bundles: [], sizeBytes: 0, uploadedAt: map.uploadedAt };
			release.bundles.push(map);
			release.sizeBytes += map.sizeBytes;
			if (map.uploadedAt > release.uploadedAt) release.uploadedAt = map.uploadedAt;
			byKey.set(key, release);
		}
		return [...byKey.values()].sort((a, b) => b.uploadedAt.localeCompare(a.uploadedAt));
	});

	async function load(): Promise<void> {
		error = null;
		try {
			maps = await listSourceMaps();
		} catch (err) {
			error = err instanceof Error ? err.message : String(err);
		} finally {
			loading = false;
		}
	}

	async function remove(service: string, version: string, bundle?: string): Promise<void> {
		const ok = bundle
			? confirm(m.sourceMaps_confirmDeleteBundle({ bundle }))
			: confirm(m.sourceMaps_confirmDeleteRelease({ service, version }));
		if (!ok) return;
		try {
			await deleteSourceMaps(service, version, bundle);
			await load();
		} catch (err) {
			error = err instanceof Error ? err.message : String(err);
		}
	}

	onMount(() => void load());
</script>

<svelte:head>
	<title>{m.sourceMapsPage_title()}</title>
</svelte:head>

<div class="flex h-full flex-col">
	<div class="border-b px-4 py-3">
		<h1 class="text-sm font-semibold">{m.sourceMapsPage_heading()}</h1>
		<p class="text-muted-foreground text-xs">{m.sourceMaps_description()}</p>
	</div>

	{#if loading}
		<div class="flex flex-1 items-center justify-center"><Spinner /></div>
	{:else if error}
		<div class="flex flex-1 items-center justify-center"><p class="text-destructive text-sm">{error}</p></div>
	{:else if releases.length === 0}
		<Empty.Root class="flex-1">
			<Empty.Header>
				<Empty.Media><FileCodeIcon class="text-muted-foreground size-8" /></Empty.Media>
				<Empty.Title>{m.sourceMaps_emptyTitle()}</Empty.Title>
				<Empty.Description>{m.sourceMaps_emptyDescription()}</Empty.Description>
			</Empty.Header>
		</Empty.Root>
	{:else}
		<div class="min-h-0 flex-1 overflow-y-auto">
			<Table.Root>
				<Table.Header>
					<Table.Row>
						<Table.Head>{m.sourceMaps_serviceColumn()}</Table.Head>
						<Table.Head>{m.sourceMaps_versionColumn()}</Table.Head>
						<Table.Head>{m.sourceMaps_bundlesColumn()}</Table.Head>
						<Table.Head>{m.sourceMaps_sizeColumn()}</Table.Head>
						<Table.Head>{m.sourceMaps_uploadedColumn()}</Table.Head>
						{#if canDelete}<Table.Head class="text-right">{m.sourceMaps_actionsColumn()}</Table.Head>{/if}
					</Table.Row>
				</Table.Header>
				<Table.Body>
					{#each releases as release (release.key)}
						<Table.Row>
							<Table.Cell class="font-medium">
								<button
									type="button"
									class="flex items-center gap-1"
									aria-expanded={!!expanded[release.key]}
									title={m.sourceMaps_toggle()}
									onclick={() => (expanded[release.key] = !expanded[release.key])}
								>
									<ChevronRightIcon class={['size-4 transition-transform', expanded[release.key] && 'rotate-90']} />
									{release.serviceName}
								</button>
							</Table.Cell>
							<Table.Cell class="font-mono text-xs">{release.version}</Table.Cell>
							<Table.Cell class="text-muted-foreground">{m.sourceMaps_bundleCount({ count: release.bundles.length })}</Table.Cell>
							<Table.Cell class="text-muted-foreground tabular-nums">{formatBytes(release.sizeBytes)}</Table.Cell>
							<Table.Cell class="text-muted-foreground">{formatDateTime(release.uploadedAt)}</Table.Cell>
							{#if canDelete}
								<Table.Cell class="text-right">
									<Button
										variant="ghost"
										size="icon-sm"
										class="text-destructive hover:text-destructive"
										title={m.sourceMaps_deleteRelease()}
										onclick={() => remove(release.serviceName, release.version)}
									>
										<Trash2Icon />
									</Button>
								</Table.Cell>
							{/if}
						</Table.Row>
						{#if expanded[release.key]}
							{#each release.bundles as bundle (bundle.bundle)}
								<Table.Row class="bg-muted/30">
									<Table.Cell colspan={2} class="pl-9 font-mono text-xs">{bundle.bundle}</Table.Cell>
									<Table.Cell></Table.Cell>
									<Table.Cell class="text-muted-foreground tabular-nums">{formatBytes(bundle.sizeBytes)}</Table.Cell>
									<Table.Cell class="text-muted-foreground">{formatDateTime(bundle.uploadedAt)}</Table.Cell>
									{#if canDelete}
										<Table.Cell class="text-right">
											<Button
												variant="ghost"
												size="icon-sm"
												class="text-destructive hover:text-destructive"
												title={m.sourceMaps_deleteBundle()}
												onclick={() => remove(release.serviceName, release.version, bundle.bundle)}
											>
												<Trash2Icon />
											</Button>
										</Table.Cell>
									{/if}
								</Table.Row>
							{/each}
						{/if}
					{/each}
				</Table.Body>
			</Table.Root>
		</div>
	{/if}
</div>
