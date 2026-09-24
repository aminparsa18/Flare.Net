// Central reactive state for the Notification Channels page - mirrors AlertsState's shape
// (`$lib/alerts/state.svelte.ts`): a class with `$state` fields, provided via
// `notificationChannelsContext` rather than passed as props. Create/edit/delete, same
// shape as alert rules (unlike access tokens, which are create-only) - plus a "send test"
// action, mirroring the alert form's own send-test button but scoped to one channel with
// no rule involved.

import {
	listNotificationChannels,
	createNotificationChannel,
	updateNotificationChannel,
	deleteNotificationChannel,
	sendTestNotificationChannel,
	type NotificationChannel,
	type NotificationChannelRequest
} from '$lib/notification-channels-api';

export class NotificationChannelsState {
	channels = $state.raw<NotificationChannel[]>([]);
	loading = $state(false);
	error = $state<string | null>(null);

	/** Drives the create/edit dialog - `null` closed, `'new'` creating, a `NotificationChannel` editing that channel. Same `open={x !== null}` pattern `AlertsState.formTarget` uses. */
	formTarget = $state<NotificationChannel | 'new' | null>(null);
	saving = $state(false);
	saveError = $state<string | null>(null);

	/** Which channel's "send test" is in flight, or null - keyed by id so the table can disable just that row's button rather than every row at once. */
	testingId = $state<string | null>(null);
	testResult = $state<{ id: string; success: boolean; error: string } | null>(null);

	/** Set by `openCreate(onCreated)` - lets a caller outside the Channels tab (AlertRuleFormDialog's "new channel" action) act on the channel it just created, e.g. auto-select it. Cleared whenever the form closes. */
	private onCreated: ((channel: NotificationChannel) => void) | null = null;

	async load(): Promise<void> {
		this.loading = true;
		this.error = null;
		try {
			const res = await listNotificationChannels();
			this.channels = res.channels;
		} catch (err) {
			this.error = err instanceof Error ? err.message : String(err);
		} finally {
			this.loading = false;
		}
	}

	openCreate(onCreated?: (channel: NotificationChannel) => void): void {
		this.saveError = null;
		this.onCreated = onCreated ?? null;
		this.formTarget = 'new';
	}

	openEdit(channel: NotificationChannel): void {
		this.saveError = null;
		this.onCreated = null;
		this.formTarget = channel;
	}

	closeForm(): void {
		this.onCreated = null;
		this.formTarget = null;
	}

	async create(request: NotificationChannelRequest): Promise<void> {
		this.saving = true;
		this.saveError = null;
		try {
			const created = await createNotificationChannel(request);
			const onCreated = this.onCreated;
			this.closeForm();
			await this.load();
			onCreated?.(created);
		} catch (err) {
			this.saveError = err instanceof Error ? err.message : String(err);
		} finally {
			this.saving = false;
		}
	}

	async update(id: string, request: NotificationChannelRequest): Promise<void> {
		this.saving = true;
		this.saveError = null;
		try {
			await updateNotificationChannel(id, request);
			this.formTarget = null;
			await this.load();
		} catch (err) {
			this.saveError = err instanceof Error ? err.message : String(err);
		} finally {
			this.saving = false;
		}
	}

	async remove(id: string): Promise<void> {
		try {
			await deleteNotificationChannel(id);
			await this.load();
		} catch (err) {
			this.error = err instanceof Error ? err.message : String(err);
		}
	}

	async sendTest(id: string): Promise<void> {
		this.testingId = id;
		this.testResult = null;
		try {
			const result = await sendTestNotificationChannel(id);
			this.testResult = { id, success: result.success, error: result.error };
		} catch (err) {
			this.testResult = { id, success: false, error: err instanceof Error ? err.message : String(err) };
		} finally {
			this.testingId = null;
		}
	}
}
