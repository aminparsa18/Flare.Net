<script lang="ts">
	// "New version available" strip under AppNav, plus the release-notes dialog it (and the
	// user menu's version line) opens - see $lib/version/update-notice.svelte.ts and ADR-0068.
	// Mounted only while the app chrome shows, so /login never fetches /api/version.
	import * as Dialog from '$lib/components/ui/dialog';
	import { Button } from '$lib/components/ui/button';
	import { notificationPrefs } from '$lib/notifications/prefs.svelte';
	import { updateNotice } from '$lib/version/update-notice.svelte';
	import { formatCalendarDay } from '$lib/time/format';
	import * as m from '$lib/paraglide/messages';
	import SparklesIcon from '@lucide/svelte/icons/sparkles';
	import XIcon from '@lucide/svelte/icons/x';
	import ExternalLinkIcon from '@lucide/svelte/icons/external-link';

	$effect(() => {
		void updateNotice.load();
	});

	const release = $derived(updateNotice.availableRelease);
</script>

{#if notificationPrefs.showUpdateNotice && updateNotice.showBanner && release}
	<div
		class="flex items-center gap-2 border-b bg-primary/5 px-4 py-1.5 text-sm"
		role="status"
		aria-live="polite"
	>
		<SparklesIcon class="size-4 shrink-0 text-primary" />
		<span class="min-w-0 truncate">
			{m.updateNotice_available({ latest: release.version, current: updateNotice.info?.current ?? '' })}
		</span>
		<Button variant="link" size="sm" class="h-auto px-1 py-0" onclick={() => (updateNotice.notesOpen = true)}>
			{m.updateNotice_whatsNew()}
		</Button>
		<Button
			variant="ghost"
			size="icon-sm"
			class="ml-auto size-6"
			aria-label={m.updateNotice_dismiss()}
			onclick={() => updateNotice.dismiss()}
		>
			<XIcon />
		</Button>
	</div>
{/if}

{#if release}
	<Dialog.Root open={updateNotice.notesOpen} onOpenChange={(v) => (updateNotice.notesOpen = v)}>
		<Dialog.Content class="sm:max-w-2xl">
			<Dialog.Header>
				<Dialog.Title>{release.name ?? m.updateNotice_dialogTitle({ latest: release.version })}</Dialog.Title>
				<Dialog.Description>
					{release.publishedAt
						? m.updateNotice_dialogDescriptionDated({
								current: updateNotice.info?.current ?? '',
								date: formatCalendarDay(release.publishedAt)
							})
						: m.updateNotice_dialogDescription({ current: updateNotice.info?.current ?? '' })}
				</Dialog.Description>
			</Dialog.Header>
			{#if release.notes}
				<!-- Markdown source shown as-is: GitHub's generated notes (a bullet list of PR
				     titles) read fine unrendered, and it avoids pulling in a Markdown renderer +
				     sanitizer for one dialog. The link below has the rendered version. -->
				<pre
					class="max-h-[60vh] overflow-auto rounded-md border bg-muted/40 p-3 font-sans text-sm whitespace-pre-wrap break-words">{release.notes}</pre>
			{:else}
				<p class="text-sm text-muted-foreground">{m.updateNotice_noNotes()}</p>
			{/if}
			<Dialog.Footer>
				<Button variant="outline" href={release.url} target="_blank" rel="noopener noreferrer">
					<ExternalLinkIcon />
					{m.updateNotice_viewOnGitHub()}
				</Button>
			</Dialog.Footer>
		</Dialog.Content>
	</Dialog.Root>
{/if}
