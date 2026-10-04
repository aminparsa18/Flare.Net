<script lang="ts">
	import ChoiceGroup from '$lib/components/settings/ChoiceGroup.svelte';
	import { Button } from '$lib/components/ui/button';
	import {
		explorerPrefs,
		LANDING_PAGES,
		LINES_PER_ROW_CHOICES,
		LIVE_BUFFER_CHOICES,
		BUCKET_WIDTH_OPTIONS_SECONDS,
		type LandingPage
	} from '$lib/explorer/prefs.svelte';
	import { formatBucketWidthSeconds } from '$lib/logs/bucket-width';
	import { pinnedAttributes, pinnedSpanAttributes } from '$lib/logs/pinned-attributes.svelte';
	import XIcon from '@lucide/svelte/icons/x';
	import * as m from '$lib/paraglide/messages';

	const landingLabels: Record<LandingPage, () => string> = {
		default: () => m.settingsExplorer_landingDefault(),
		logs: () => m.settingsExplorer_landingLogs(),
		traces: () => m.settingsExplorer_landingTraces(),
		metrics: () => m.settingsExplorer_landingMetrics(),
		errors: () => m.settingsExplorer_landingErrors()
	};

	const selectClass = 'border-input bg-background h-9 rounded-md border px-3 text-sm';
	const rowClass = 'flex items-center justify-between gap-4 rounded-lg border p-3 text-sm';
</script>

<svelte:head>
	<title>{m.settingsExplorer_title()}</title>
</svelte:head>

<div class="flex flex-col gap-8">
	<div class="flex items-start justify-between gap-4">
		<div>
			<h2 class="text-xl font-semibold">{m.settingsExplorer_heading()}</h2>
			<p class="text-muted-foreground text-sm">{m.settingsExplorer_description()}</p>
		</div>
		<Button variant="outline" size="sm" disabled={explorerPrefs.isDefault} onclick={() => explorerPrefs.reset()}>
			{m.settingsExplorer_reset()}
		</Button>
	</div>

	<section class="flex flex-col gap-3">
		<h3 class="font-medium">{m.settingsExplorer_landingHeading()}</h3>
		<p class="text-muted-foreground text-sm">{m.settingsExplorer_landingDescription()}</p>
		<ChoiceGroup
			label={m.settingsExplorer_landingHeading()}
			value={explorerPrefs.landingPage}
			onchange={(v) => explorerPrefs.set('landingPage', v)}
			options={LANDING_PAGES.map((value) => ({ value, label: landingLabels[value]() }))}
		/>
	</section>

	<section class="flex flex-col gap-2">
		<h3 class="font-medium">{m.settingsExplorer_logTableHeading()}</h3>
		<label class={rowClass}>
			<span>{m.settingsExplorer_linesPerRow()}</span>
			<select
				class={selectClass}
				value={explorerPrefs.linesPerRow}
				onchange={(e) => explorerPrefs.set('linesPerRow', Number(e.currentTarget.value))}
			>
				{#each LINES_PER_ROW_CHOICES as n (n)}<option value={n}>{n}</option>{/each}
			</select>
		</label>
		<label class={rowClass}>
			<span>{m.settingsExplorer_showTime()}</span>
			<input type="checkbox" checked={explorerPrefs.showTimeColumn} onchange={(e) => explorerPrefs.set('showTimeColumn', e.currentTarget.checked)} />
		</label>
		<label class={rowClass}>
			<span>{m.settingsExplorer_showMessage()}</span>
			<input type="checkbox" checked={explorerPrefs.showMessageColumn} onchange={(e) => explorerPrefs.set('showMessageColumn', e.currentTarget.checked)} />
		</label>
	</section>

	<section class="flex flex-col gap-2">
		<h3 class="font-medium">{m.settingsExplorer_liveHeading()}</h3>
		<label class={rowClass}>
			<span>{m.settingsExplorer_liveByDefault()}</span>
			<input type="checkbox" checked={explorerPrefs.liveByDefault} onchange={(e) => explorerPrefs.set('liveByDefault', e.currentTarget.checked)} />
		</label>
		<label class={rowClass}>
			<span>{m.settingsExplorer_liveBuffer()}</span>
			<select
				class={selectClass}
				value={explorerPrefs.liveBuffer}
				onchange={(e) => explorerPrefs.set('liveBuffer', Number(e.currentTarget.value))}
			>
				{#each LIVE_BUFFER_CHOICES as n (n)}<option value={n}>{n.toLocaleString()}</option>{/each}
			</select>
		</label>
	</section>

	<section class="flex flex-col gap-2">
		<h3 class="font-medium">{m.settingsExplorer_chartHeading()}</h3>
		<label class={rowClass}>
			<span>{m.settingsExplorer_bucketLabel()}</span>
			<select
				class={selectClass}
				value={explorerPrefs.bucketWidthSeconds === null ? '' : String(explorerPrefs.bucketWidthSeconds)}
				onchange={(e) => explorerPrefs.set('bucketWidthSeconds', e.currentTarget.value === '' ? null : Number(e.currentTarget.value))}
			>
				<option value="">{m.settingsExplorer_bucketAuto()}</option>
				{#each BUCKET_WIDTH_OPTIONS_SECONDS as seconds (seconds)}
					<option value={String(seconds)}>{formatBucketWidthSeconds(seconds)}</option>
				{/each}
			</select>
		</label>
		<p class="text-muted-foreground text-xs">{m.settingsExplorer_bucketNote()}</p>
	</section>

	<section class="flex flex-col gap-2">
		<h3 class="font-medium">{m.settingsExplorer_facetHeading()}</h3>
		<label class={rowClass}>
			<span>{m.settingsExplorer_facetOpen()}</span>
			<input type="checkbox" checked={explorerPrefs.facetSidebarOpen} onchange={(e) => explorerPrefs.set('facetSidebarOpen', e.currentTarget.checked)} />
		</label>
	</section>

	<section class="flex flex-col gap-3">
		<h3 class="font-medium">{m.settingsExplorer_pinnedHeading()}</h3>
		<p class="text-muted-foreground text-sm">{m.settingsExplorer_pinnedDescription()}</p>
		{#each [{ label: m.settingsExplorer_pinnedLogs(), store: pinnedAttributes }, { label: m.settingsExplorer_pinnedSpans(), store: pinnedSpanAttributes }] as group (group.label)}
			<div class="flex flex-col gap-2">
				<span class="text-sm font-medium">{group.label}</span>
				{#if group.store.keys.length === 0}
					<span class="text-muted-foreground text-sm">{m.settingsExplorer_pinnedNone()}</span>
				{:else}
					<div class="flex flex-wrap gap-2">
						{#each group.store.keys as key (key)}
							<span class="bg-muted inline-flex items-center gap-1 rounded-md px-2 py-1 font-mono text-xs">
								{key}
								<button type="button" class="hover:text-destructive" aria-label={m.settingsExplorer_unpin({ key })} onclick={() => group.store.toggle(key)}>
									<XIcon class="size-3" />
								</button>
							</span>
						{/each}
					</div>
				{/if}
			</div>
		{/each}
	</section>
</div>
