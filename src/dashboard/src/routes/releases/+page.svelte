<script lang="ts">
	// Releases (ADR-0182): versions CI (or `flare releases mark`) has marked as deployed, with the
	// exception groups each one introduced. The marking is a PUT from a pipeline; this page reads it.
	import { onMount } from 'svelte';
	import * as Table from '#lib/components/ui/table/index.ts';
	import * as Empty from '#lib/components/ui/empty/index.ts';
	import * as Select from '#lib/components/ui/select/index.ts';
	import { Button } from '#lib/components/ui/button/index.ts';
	import { safeHttpUrl } from '#lib/safe-url.ts';
	import { Badge } from '#lib/components/ui/badge/index.ts';
	import { Spinner } from '#lib/components/ui/spinner/index.ts';
	import { deleteRelease, getReleaseErrors, listReleases, type Release, type ReleaseErrors, type ReleaseNewError } from '#lib/releases-api.ts';
	import { getExceptionOccurrences } from '#lib/errors-api.ts';
	import { getSuspectCommits, listSourceLinks, type SuspectCommits } from '#lib/source-links-api.ts';
	import { frameRepoPaths, releaseCommit, type SourceLinkConfig } from '#lib/errors/source-links.ts';
	import { buildServiceVersionErrorsHref } from '#lib/deep-links.ts';
	import { formatDateTime } from '#lib/time/format.ts';
	import { authContext } from '#lib/auth/context.ts';
	import RocketIcon from '@lucide/svelte/icons/rocket';
	import ChevronRightIcon from '@lucide/svelte/icons/chevron-right';
	import Trash2Icon from '@lucide/svelte/icons/trash-2';
	import * as m from '#lib/paraglide/messages.js';

	const auth = authContext.get();

	let all = $state.raw<Release[]>([]);
	let service = $state('');
	let releases = $state.raw<Release[]>([]);
	let loading = $state(true);
	let error = $state<string | null>(null);
	let expanded = $state<Record<string, boolean>>({});
	let details = $state.raw<Record<string, ReleaseErrors | 'loading' | string>>({});
	// Suspect commits (ADR-0263), keyed like the error row they hang off.
	let sourceLinks = $state.raw<SourceLinkConfig[]>([]);
	let suspects = $state.raw<Record<string, SuspectCommits | 'loading' | { error: string }>>({});

	const services = $derived([...new Set(all.map((r) => r.service))].sort());

	async function loadAll(): Promise<void> {
		error = null;
		try {
			all = await listReleases();
			if (!service || !all.some((r) => r.service === service)) service = all[0]?.service ?? '';
			await loadService();
		} catch (err) {
			error = err instanceof Error ? err.message : String(err);
		} finally {
			loading = false;
		}
	}

	/** The scoped list carries the new-error counts, which cost a scan of spans - so only for the selected service. */
	async function loadService(): Promise<void> {
		expanded = {};
		details = {};
		releases = service ? await listReleases(service) : [];
	}

	async function pickService(next: string): Promise<void> {
		service = next;
		try {
			await loadService();
		} catch (err) {
			error = err instanceof Error ? err.message : String(err);
		}
	}

	async function toggle(release: Release): Promise<void> {
		const open = !expanded[release.id];
		expanded[release.id] = open;
		if (!open || details[release.id]) return;
		details = { ...details, [release.id]: 'loading' };
		try {
			details = { ...details, [release.id]: await getReleaseErrors(release.service, release.version) };
		} catch (err) {
			details = { ...details, [release.id]: err instanceof Error ? err.message : String(err) };
		}
	}

	async function remove(release: Release): Promise<void> {
		if (!confirm(m.releases_confirmDelete({ service: release.service, version: release.version }))) return;
		try {
			await deleteRelease(release.service, release.version);
			await loadAll();
		} catch (err) {
			error = err instanceof Error ? err.message : String(err);
		}
	}

	const suspectKey = (release: Release, e: ReleaseNewError) => `${release.id}\u001f${e.exceptionType}\u001f${e.exceptionMessage}`;

	/** The release's commit and the next-older release's: the range a new error must have come from. */
	function commitRange(release: Release): { base: string; head: string } | null {
		const head = releaseCommit(release);
		const older = releases.slice(releases.indexOf(release) + 1).find((r) => releaseCommit(r));
		const base = older ? releaseCommit(older) : null;
		return head && base ? { base, head } : null;
	}

	async function findSuspects(release: Release, e: ReleaseNewError): Promise<void> {
		const key = suspectKey(release, e);
		const setResult = (value: SuspectCommits | { error: string }): void => {
			suspects = { ...suspects, [key]: value };
		};
		const config = sourceLinks.find((c) => c.serviceName === release.service);
		const range = commitRange(release);
		if (!config || !range) {
			setResult({ error: !config ? m.releases_suspectsNoSource() : m.releases_suspectsNoCommit() });
			return;
		}
		suspects = { ...suspects, [key]: 'loading' };
		try {
			const { occurrences } = await getExceptionOccurrences({
				filter: {
					from: release.deployedAt,
					to: new Date().toISOString(),
					services: [release.service],
					resourceAttributes: [{ key: 'service.version', value: release.version }]
				},
				exceptionType: e.exceptionType,
				exceptionMessage: e.exceptionMessage
			});
			const paths = occurrences.map((o) => frameRepoPaths(o.stacktrace, config)).find((p) => p.length > 0);
			if (!paths) {
				setResult({ error: m.releases_suspectsNoFrames() });
				return;
			}
			setResult(await getSuspectCommits({ serviceName: release.service, baseRef: range.base, headRef: range.head, paths }));
		} catch (err) {
			setResult({ error: err instanceof Error ? err.message : String(err) });
		}
	}

	function errorsHref(release: Release, exceptionType: string): string {
		return buildServiceVersionErrorsHref(release.service, release.version, exceptionType, new Date(release.deployedAt).getTime() - 3_600_000, Date.now());
	}

	onMount(() => {
		void loadAll();
		// Without source-link config there is nothing to diff against; the button then explains that.
		listSourceLinks().then((links) => (sourceLinks = links), () => {});
	});
</script>

<svelte:head>
	<title>{m.releasesPage_title()}</title>
</svelte:head>

<div class="flex h-full flex-col">
	<div class="flex flex-wrap items-center justify-between gap-2 border-b px-4 py-3">
		<div>
			<h1 class="text-sm font-semibold">{m.releasesPage_heading()}</h1>
			<p class="text-muted-foreground text-xs">{m.releases_description()}</p>
		</div>
		{#if services.length > 0}
			<Select.Root type="single" value={service} onValueChange={(v) => v && pickService(v)}>
				<Select.Trigger class="h-8 w-56">{service}</Select.Trigger>
				<Select.Content>
					{#each services as name (name)}
						<Select.Item value={name} label={name} />
					{/each}
				</Select.Content>
			</Select.Root>
		{/if}
	</div>

	{#if loading}
		<div class="flex flex-1 items-center justify-center"><Spinner /></div>
	{:else if error}
		<div class="flex flex-1 items-center justify-center"><p class="text-destructive text-sm">{error}</p></div>
	{:else if all.length === 0}
		<Empty.Root class="flex-1">
			<Empty.Header>
				<Empty.Media><RocketIcon class="text-muted-foreground size-8" /></Empty.Media>
				<Empty.Title>{m.releases_emptyTitle()}</Empty.Title>
				<Empty.Description>{m.releases_emptyDescription()}</Empty.Description>
			</Empty.Header>
		</Empty.Root>
	{:else}
		<div class="min-h-0 flex-1 overflow-y-auto">
			<Table.Root>
				<Table.Header>
					<Table.Row>
						<Table.Head>{m.releases_versionColumn()}</Table.Head>
						<Table.Head>{m.releases_deployedColumn()}</Table.Head>
						<Table.Head>{m.releases_commitColumn()}</Table.Head>
						<Table.Head class="text-right">{m.releases_newErrorsColumn()}</Table.Head>
						<Table.Head class="text-right">{m.releases_regressionsColumn()}</Table.Head>
						{#if auth.canMutate}<Table.Head class="text-right">{m.releases_actionsColumn()}</Table.Head>{/if}
					</Table.Row>
				</Table.Header>
				<Table.Body>
					{#each releases as release (release.id)}
						{@const detail = details[release.id]}
						<Table.Row>
							<Table.Cell class="font-mono text-xs font-medium">
								<button
									type="button"
									class="flex items-center gap-1"
									aria-expanded={!!expanded[release.id]}
									title={m.releases_toggle()}
									onclick={() => toggle(release)}
								>
									<ChevronRightIcon class={['size-4 transition-transform', expanded[release.id] && 'rotate-90']} />
									{release.version}
								</button>
							</Table.Cell>
							<Table.Cell class="text-muted-foreground">{formatDateTime(release.deployedAt)}</Table.Cell>
							<Table.Cell class="font-mono text-xs">
								{#if safeHttpUrl(release.url)}
									<a href={safeHttpUrl(release.url)} target="_blank" rel="noopener noreferrer" class="underline-offset-2 hover:underline">
										{release.commit ? release.commit.slice(0, 8) : release.url}
									</a>
								{:else}
									{release.commit.slice(0, 8)}
								{/if}
							</Table.Cell>
							<Table.Cell class="text-right tabular-nums">
								{#if release.newErrorCount}
									<Badge variant="destructive">{release.newErrorCount}</Badge>
								{:else}
									<span class="text-muted-foreground">{release.newErrorCount ?? '-'}</span>
								{/if}
							</Table.Cell>
							<Table.Cell class="text-right tabular-nums">
								{#if release.regressionCount}
									<Badge variant="warning">{release.regressionCount}</Badge>
								{:else}
									<span class="text-muted-foreground">{release.regressionCount ?? '-'}</span>
								{/if}
							</Table.Cell>
							{#if auth.canMutate}
								<Table.Cell class="text-right">
									<Button
										variant="ghost"
										size="icon-sm"
										class="text-destructive hover:text-destructive"
										title={m.releases_delete()}
										onclick={() => remove(release)}
									>
										<Trash2Icon />
									</Button>
								</Table.Cell>
							{/if}
						</Table.Row>
						{#if expanded[release.id]}
							<Table.Row class="bg-muted/30">
								<Table.Cell colspan={auth.canMutate ? 6 : 5} class="pl-9">
									{#if release.notes}
										<p class="text-muted-foreground mb-2 text-xs whitespace-pre-wrap">{release.notes}</p>
									{/if}
									{#if detail === 'loading' || detail === undefined}
										<Spinner />
									{:else if typeof detail === 'string'}
										<p class="text-destructive text-xs">{detail}</p>
									{:else}
										{#if detail.regressions.length > 0}
											<p class="mb-1 text-xs font-medium">{m.releases_regressionsHeading()}</p>
											<ul class="mb-3 space-y-1">
												{#each detail.regressions as r (r.exceptionType + '\u001f' + r.exceptionMessage)}
													<li class="flex items-baseline gap-2 text-xs">
														<a href={errorsHref(release, r.exceptionType)} class="font-mono font-medium underline-offset-2 hover:underline">{r.exceptionType}</a>
														<span class="text-muted-foreground min-w-0 flex-1 truncate">{r.exceptionMessage}</span>
														<span class="text-muted-foreground">{m.releases_resolvedOn({ date: formatDateTime(new Date(r.resolvedAtUnixMs).toISOString()) })}</span>
													</li>
												{/each}
											</ul>
										{/if}
										{#if detail.errors.length === 0}
											<p class="text-muted-foreground text-xs">{m.releases_noNewErrors({ days: detail.historyDays })}</p>
										{:else}
										<ul class="space-y-1">
											{#each detail.errors as e (e.exceptionType + '\u001f' + e.exceptionMessage)}
												<li class="flex items-baseline gap-2 text-xs">
													<a href={errorsHref(release, e.exceptionType)} class="font-mono font-medium underline-offset-2 hover:underline">{e.exceptionType}</a>
													<span class="text-muted-foreground min-w-0 flex-1 truncate">{e.exceptionMessage}</span>
													<span class="text-muted-foreground tabular-nums">{m.releases_occurrences({ count: e.occurrences })}</span>
													<Button variant="link" size="sm" class="h-auto p-0 text-xs" onclick={() => findSuspects(release, e)}>{m.releases_suspectsButton()}</Button>
												</li>
												{@const found = suspects[suspectKey(release, e)]}
												{#if found === 'loading'}
													<li><Spinner /></li>
												{:else if found && 'error' in found}
													<li class="text-muted-foreground pl-4 text-xs">{found.error}</li>
												{:else if found}
													<li class="pl-4">
														{#if found.commits.length === 0}
															<p class="text-muted-foreground text-xs">{m.releases_suspectsNone()}</p>
														{:else}
															<ul class="space-y-1">
																{#each found.commits as c (c.sha)}
																	<li class="text-xs">
																		<a href={safeHttpUrl(c.url)} target="_blank" rel="noopener noreferrer" class="font-mono underline-offset-2 hover:underline">{c.sha.slice(0, 8)}</a>
																		<span class="font-medium">{c.message}</span>
																		<span class="text-muted-foreground">- {c.author}{c.date ? `, ${formatDateTime(c.date)}` : ''}</span>
																		<span class="text-muted-foreground block truncate font-mono text-[11px]">{m.releases_suspectsChanged({ paths: c.matchedPaths.join(', ') })}</span>
																	</li>
																{/each}
															</ul>
														{/if}
														{#if found.truncated}
															<p class="text-muted-foreground mt-1 text-[11px]">{m.releases_suspectsTruncated()}</p>
														{/if}
														<a href={safeHttpUrl(found.compareUrl)} target="_blank" rel="noopener noreferrer" class="text-muted-foreground text-[11px] underline-offset-2 hover:underline">
															{m.releases_suspectsCompare({ base: commitRange(release)?.base.slice(0, 8) ?? '', head: commitRange(release)?.head.slice(0, 8) ?? '' })}
														</a>
													</li>
												{/if}
											{/each}
										</ul>
										<p class="text-muted-foreground mt-2 text-[11px]">{m.releases_historyNote({ days: detail.historyDays })}</p>
										{/if}
									{/if}
								</Table.Cell>
							</Table.Row>
						{/if}
					{/each}
				</Table.Body>
			</Table.Root>
		</div>
	{/if}
</div>
