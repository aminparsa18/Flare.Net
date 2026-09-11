// Client for Flare.Api's notification-channel API (channel CRUD, send-test) - the
// reusable-channel entity an `AlertRule` references by `channelIds` instead of embedding
// a destination inline. See `docs-internal/adr/0021-reusable-notification-channels.md`.
//
// MemoryPack over the wire, same shape as `alerts-api.ts`'s header comment describes.
// `NotificationChannelRequest` has no `DateTimeOffset`/nested generator-ineligible member
// (flat strings + the plain `type` enum) so it's a real generated class, reused here
// directly - `NotificationChannel` itself has `createdAt`/`updatedAt`, so it (and the list
// response nesting it) are hand-written (`$lib/memorypack/`), same reasoning `AlertRule.ts`
// documents. `type` converts through `$lib/memorypack/enums.ts`'s
// `notificationChannelTypeToString`/`FromString`, same pattern `alerts-api.ts` uses for
// `conditionKind`.

import { API_BASE_URL, apiFetch, memoryPackAcceptHeaders, memoryPackBody, memoryPackRequestHeaders } from './api';
import { notificationChannelTypeFromString, notificationChannelTypeToString, type NotificationChannelTypeName } from '$lib/memorypack/enums';
import { NotificationChannelRequest as GeneratedNotificationChannelRequest } from '$lib/generated/memorypack/NotificationChannelRequest.js';
import { NotificationChannel as GeneratedNotificationChannel } from '$lib/memorypack/NotificationChannel';
import { NotificationChannelListResponse as GeneratedNotificationChannelListResponse } from '$lib/memorypack/NotificationChannelListResponse';
import { AlertNotificationTestResult as GeneratedAlertNotificationTestResult } from '$lib/generated/memorypack/AlertNotificationTestResult.js';
import type { AlertNotificationTestResult } from './alerts-api';

export type NotificationChannelType = NotificationChannelTypeName;

/** A saved, reusable notification destination - referenced by ID from zero or more `AlertRule.channelIds`. */
export interface NotificationChannel {
	id: string;
	name: string;
	description: string;
	type: NotificationChannelType;
	/** Meaningful only when `type` is `'Webhook'`. */
	webhookUrl: string;
	/** Meaningful only when `type` is `'Telegram'`. */
	telegramBotToken: string;
	telegramChatId: string;
	/** Meaningful only when `type` is `'Email'`. */
	emailTo: string;
	/** Meaningful only when `type` is `'PagerDuty'`. */
	pagerDutyRoutingKey: string;
	createdAt: string;
	updatedAt: string;
}

/** Create/update request body - requires exactly the destination field(s) matching `type`. */
export interface NotificationChannelRequest {
	name: string;
	description?: string;
	type: NotificationChannelType;
	webhookUrl?: string;
	telegramBotToken?: string;
	telegramChatId?: string;
	emailTo?: string;
	pagerDutyRoutingKey?: string;
}

export interface NotificationChannelListResponse {
	channels: NotificationChannel[];
}

function toNotificationChannel(dto: GeneratedNotificationChannel): NotificationChannel {
	return {
		id: dto.id,
		name: dto.name ?? '',
		description: dto.description ?? '',
		type: notificationChannelTypeToString(dto.type),
		webhookUrl: dto.webhookUrl ?? '',
		telegramBotToken: dto.telegramBotToken ?? '',
		telegramChatId: dto.telegramChatId ?? '',
		emailTo: dto.emailTo ?? '',
		pagerDutyRoutingKey: dto.pagerDutyRoutingKey ?? '',
		createdAt: dto.createdAt.toISOString(),
		updatedAt: dto.updatedAt.toISOString()
	};
}

function toGeneratedNotificationChannelRequest(request: NotificationChannelRequest): GeneratedNotificationChannelRequest {
	const dto = new GeneratedNotificationChannelRequest();
	dto.name = request.name;
	dto.description = request.description ?? null;
	dto.type = notificationChannelTypeFromString(request.type);
	dto.webhookUrl = request.webhookUrl ?? null;
	dto.telegramBotToken = request.telegramBotToken ?? null;
	dto.telegramChatId = request.telegramChatId ?? null;
	dto.emailTo = request.emailTo ?? null;
	dto.pagerDutyRoutingKey = request.pagerDutyRoutingKey ?? null;
	return dto;
}

function toAlertNotificationTestResult(dto: GeneratedAlertNotificationTestResult): AlertNotificationTestResult {
	return { success: dto.success, statusCode: dto.statusCode, error: dto.error ?? '' };
}

async function decodeNotificationChannel(res: Response): Promise<NotificationChannel> {
	const dto = GeneratedNotificationChannel.deserialize(await res.arrayBuffer());
	if (dto == null) {
		throw new Error('Empty response body decoding NotificationChannel.');
	}
	return toNotificationChannel(dto);
}

// ---- CRUD ------------------------------------------------------------------

export async function listNotificationChannels(signal?: AbortSignal): Promise<NotificationChannelListResponse> {
	const res = await apiFetch(`${API_BASE_URL}/api/notification-channels`, { headers: memoryPackAcceptHeaders(), signal });
	if (!res.ok) {
		throw new Error(`GET /api/notification-channels failed: ${res.status} ${res.statusText}`);
	}
	const dto = GeneratedNotificationChannelListResponse.deserialize(await res.arrayBuffer());
	return { channels: (dto?.channels ?? []).filter((c): c is GeneratedNotificationChannel => c != null).map(toNotificationChannel) };
}

export async function getNotificationChannel(id: string, signal?: AbortSignal): Promise<NotificationChannel> {
	const res = await apiFetch(`${API_BASE_URL}/api/notification-channels/${id}`, { headers: memoryPackAcceptHeaders(), signal });
	if (!res.ok) {
		throw new Error(`GET /api/notification-channels/${id} failed: ${res.status} ${res.statusText}`);
	}
	return decodeNotificationChannel(res);
}

export async function createNotificationChannel(request: NotificationChannelRequest): Promise<NotificationChannel> {
	const dto = toGeneratedNotificationChannelRequest(request);
	const res = await apiFetch(`${API_BASE_URL}/api/notification-channels`, {
		method: 'POST',
		headers: memoryPackRequestHeaders(),
		body: memoryPackBody(GeneratedNotificationChannelRequest.serialize(dto))
	});
	if (!res.ok) {
		throw new Error(`POST /api/notification-channels failed: ${res.status} ${res.statusText}`);
	}
	return decodeNotificationChannel(res);
}

export async function updateNotificationChannel(id: string, request: NotificationChannelRequest): Promise<NotificationChannel> {
	const dto = toGeneratedNotificationChannelRequest(request);
	const res = await apiFetch(`${API_BASE_URL}/api/notification-channels/${id}`, {
		method: 'PUT',
		headers: memoryPackRequestHeaders(),
		body: memoryPackBody(GeneratedNotificationChannelRequest.serialize(dto))
	});
	if (!res.ok) {
		throw new Error(`PUT /api/notification-channels/${id} failed: ${res.status} ${res.statusText}`);
	}
	return decodeNotificationChannel(res);
}

/** 204 No Content on success - unlike every other function here, there's no body to decode. */
export async function deleteNotificationChannel(id: string): Promise<void> {
	const res = await apiFetch(`${API_BASE_URL}/api/notification-channels/${id}`, { method: 'DELETE' });
	if (!res.ok) {
		throw new Error(`DELETE /api/notification-channels/${id} failed: ${res.status} ${res.statusText}`);
	}
}

// ---- Send test ---------------------------------------------------------------

/** Sends a real test notification through this one channel, independent of any alert rule. */
export async function sendTestNotificationChannel(id: string): Promise<AlertNotificationTestResult> {
	const res = await apiFetch(`${API_BASE_URL}/api/notification-channels/${id}/send-test`, { method: 'POST', headers: memoryPackAcceptHeaders() });
	if (!res.ok) {
		throw new Error(`POST /api/notification-channels/${id}/send-test failed: ${res.status} ${res.statusText}`);
	}
	const dto = GeneratedAlertNotificationTestResult.deserialize(await res.arrayBuffer());
	if (dto == null) {
		throw new Error('Empty response body decoding AlertNotificationTestResult.');
	}
	return toAlertNotificationTestResult(dto);
}
