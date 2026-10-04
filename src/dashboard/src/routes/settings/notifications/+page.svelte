<script lang="ts">
	import { Button } from '$lib/components/ui/button';
	import { notificationPrefs } from '$lib/notifications/prefs.svelte';
	import { browserNotificationsSupported } from '$lib/notifications/alert-watcher.svelte';
	import * as m from '$lib/paraglide/messages';

	const rowClass = 'flex items-center justify-between gap-4 rounded-lg border p-3 text-sm';
	const supported = browserNotificationsSupported();
	let permission = $state<NotificationPermission>(supported ? Notification.permission : 'denied');

	async function toggleBrowserAlerts(on: boolean) {
		if (on && permission === 'default') permission = await Notification.requestPermission();
		notificationPrefs.set('browserAlerts', on && permission === 'granted');
	}
</script>

<svelte:head>
	<title>{m.settingsNotifications_title()}</title>
</svelte:head>

<div class="flex flex-col gap-8">
	<div class="flex items-start justify-between gap-4">
		<div>
			<h2 class="text-xl font-semibold">{m.settingsNotifications_heading()}</h2>
			<p class="text-muted-foreground text-sm">{m.settingsNotifications_description()}</p>
		</div>
		<Button variant="outline" size="sm" disabled={notificationPrefs.isDefault} onclick={() => notificationPrefs.reset()}>
			{m.settingsNotifications_reset()}
		</Button>
	</div>

	<section class="flex flex-col gap-2">
		<h3 class="font-medium">{m.settingsNotifications_updateHeading()}</h3>
		<label class={rowClass}>
			<span>
				{m.settingsNotifications_updateNotice()}
				<span class="text-muted-foreground block text-xs">{m.settingsNotifications_updateNoticeHint()}</span>
			</span>
			<input type="checkbox" checked={notificationPrefs.showUpdateNotice} onchange={(e) => notificationPrefs.set('showUpdateNotice', e.currentTarget.checked)} />
		</label>
	</section>

	<section class="flex flex-col gap-2">
		<h3 class="font-medium">{m.settingsNotifications_browserHeading()}</h3>
		<label class={rowClass}>
			<span>
				{m.settingsNotifications_browserAlerts()}
				<span class="text-muted-foreground block text-xs">{m.settingsNotifications_browserHint()}</span>
			</span>
			<input
				type="checkbox"
				disabled={!supported || permission === 'denied'}
				checked={notificationPrefs.browserAlerts && permission === 'granted'}
				onchange={(e) => void toggleBrowserAlerts(e.currentTarget.checked)}
			/>
		</label>
		{#if !supported}
			<p class="text-muted-foreground text-xs">{m.settingsNotifications_browserUnsupported()}</p>
		{:else if permission === 'denied'}
			<p class="text-muted-foreground text-xs">{m.settingsNotifications_browserDenied()}</p>
		{/if}
		<p class="text-muted-foreground text-xs">{m.settingsNotifications_emailNote()}</p>
	</section>
</div>
