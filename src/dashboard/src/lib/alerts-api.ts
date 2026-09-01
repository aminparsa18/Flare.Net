// Client for Flare.Api's Alerting API (rule CRUD, fired-alert history, evaluation
// dry-runs).
//
// Migrated (Phase 2 of docs-internal/investigations/memorypack-serialization-migration-scope.md)
// to MemoryPack - see `auth-api.ts`'s header comment for the general shape.
// `AlertThreshold` has no DateTimeOffset/JsonElement/IReadOnlyList member and uses a real
// generated class; every other type here nests `LogFilter` and/or has its own
// `DateTimeOffset` member, so all are hand-written (`$lib/memorypack/`).
// `comparator`/`condition` convert through `$lib/memorypack/enums.ts`/`LogFilter.ts`'s
// helpers, same reasoning `auth-api.ts` documents for `UserRole`.

import { API_BASE_URL, apiFetch, memoryPackAcceptHeaders, memoryPackBody, memoryPackRequestHeaders, type LogFilter } from './api';
import { thresholdComparatorFromString, thresholdComparatorToString, type ThresholdComparatorName } from '$lib/memorypack/enums';
import { logFilterFromPlain, logFilterToPlain } from '$lib/memorypack/LogFilter';
import { AlertThreshold as GeneratedAlertThreshold } from '$lib/generated/memorypack/AlertThreshold.js';
import { AlertRule as GeneratedAlertRule } from '$lib/memorypack/AlertRule';
import { AlertRuleRequest as GeneratedAlertRuleRequest } from '$lib/memorypack/AlertRuleRequest';
import { AlertRuleListResponse as GeneratedAlertRuleListResponse } from '$lib/memorypack/AlertRuleListResponse';
import { AlertHistoryResponse as GeneratedAlertHistoryResponse } from '$lib/memorypack/AlertHistoryResponse';
import { AlertTestResult as GeneratedAlertTestResult } from '$lib/memorypack/AlertTestResult';
import type { AlertHistoryEntry as GeneratedAlertHistoryEntry } from '$lib/memorypack/AlertHistoryEntry';

// ---- Shared shapes (AlertModels.cs) ---------------------------------------

export type ThresholdComparator = ThresholdComparatorName;

export interface AlertThreshold {
	count: number;
	comparator: ThresholdComparator;
}

/** A saved threshold/query-based alert rule. `condition` reuses the same `LogFilter` shape the Logs Explorer filters with. */
export interface AlertRule {
	id: string;
	name: string;
	description: string;
	enabled: boolean;
	condition: LogFilter;
	threshold: AlertThreshold;
	windowSeconds: number;
	cooldownSeconds: number;
	/** Mutually exclusive with `telegramBotToken`/`telegramChatId` and `emailTo` - a rule notifies exactly one channel. */
	webhookUrl: string;
	/** Set together with `telegramChatId`, never alongside `webhookUrl`/`emailTo`. */
	telegramBotToken: string;
	telegramChatId: string;
	/** Recipient address(es), comma/semicolon-separated for more than one. The SMTP server itself is app-wide server config, not part of the rule. */
	emailTo: string;
	createdAt: string;
	updatedAt: string;
}

/** Create/update request body - same shape as `AlertRule` minus the server-assigned fields. */
export interface AlertRuleRequest {
	name: string;
	description?: string;
	enabled?: boolean;
	condition: LogFilter;
	threshold: AlertThreshold;
	windowSeconds: number;
	cooldownSeconds?: number;
	webhookUrl?: string;
	telegramBotToken?: string;
	telegramChatId?: string;
	emailTo?: string;
}

export interface AlertRuleListResponse {
	rules: AlertRule[];
}

export type NotificationStatus = 'Sent' | 'Failed';

export interface AlertHistoryEntry {
	eventId: string;
	ruleId: string;
	ruleName: string;
	firedAt: string;
	observedCount: number;
	thresholdCount: number;
	windowSeconds: number;
	notificationStatus: NotificationStatus;
	notificationStatusCode: number;
	notificationError: string;
}

export interface AlertHistoryResponse {
	events: AlertHistoryEntry[];
}

/** Dry-run result: evaluates a rule/draft's condition+threshold against current data without touching cooldown state or sending a notification. */
export interface AlertTestResult {
	observedCount: number;
	wouldFire: boolean;
	evaluatedAt: string;
	windowSeconds: number;
}

function toAlertThreshold(dto: GeneratedAlertThreshold): AlertThreshold {
	return { count: Number(dto.count), comparator: thresholdComparatorToString(dto.comparator) };
}

function toGeneratedAlertThreshold(threshold: AlertThreshold): GeneratedAlertThreshold {
	const dto = new GeneratedAlertThreshold();
	dto.count = BigInt(threshold.count);
	dto.comparator = thresholdComparatorFromString(threshold.comparator);
	return dto;
}

function toAlertRule(dto: GeneratedAlertRule): AlertRule {
	return {
		id: dto.id,
		name: dto.name ?? '',
		description: dto.description ?? '',
		enabled: dto.enabled,
		condition: logFilterToPlain(dto.condition!),
		threshold: toAlertThreshold(dto.threshold!),
		windowSeconds: dto.windowSeconds,
		cooldownSeconds: dto.cooldownSeconds,
		webhookUrl: dto.webhookUrl ?? '',
		telegramBotToken: dto.telegramBotToken ?? '',
		telegramChatId: dto.telegramChatId ?? '',
		emailTo: dto.emailTo ?? '',
		createdAt: dto.createdAt.toISOString(),
		updatedAt: dto.updatedAt.toISOString()
	};
}

async function decodeAlertRule(res: Response): Promise<AlertRule> {
	const dto = GeneratedAlertRule.deserialize(await res.arrayBuffer());
	if (dto == null) {
		throw new Error('Empty response body decoding AlertRule.');
	}
	return toAlertRule(dto);
}

function toGeneratedAlertRuleRequest(request: AlertRuleRequest): GeneratedAlertRuleRequest {
	const dto = new GeneratedAlertRuleRequest();
	dto.name = request.name;
	dto.description = request.description ?? null;
	dto.enabled = request.enabled ?? null;
	dto.condition = logFilterFromPlain(request.condition);
	dto.threshold = toGeneratedAlertThreshold(request.threshold);
	dto.windowSeconds = request.windowSeconds;
	dto.cooldownSeconds = request.cooldownSeconds ?? null;
	dto.webhookUrl = request.webhookUrl ?? null;
	dto.telegramBotToken = request.telegramBotToken ?? null;
	dto.telegramChatId = request.telegramChatId ?? null;
	dto.emailTo = request.emailTo ?? null;
	return dto;
}

function toAlertHistoryEntry(dto: GeneratedAlertHistoryEntry): AlertHistoryEntry {
	return {
		eventId: dto.eventId,
		ruleId: dto.ruleId,
		ruleName: dto.ruleName ?? '',
		firedAt: dto.firedAt.toISOString(),
		observedCount: Number(dto.observedCount),
		thresholdCount: Number(dto.thresholdCount),
		windowSeconds: dto.windowSeconds,
		notificationStatus: (dto.notificationStatus ?? 'Failed') as NotificationStatus,
		notificationStatusCode: dto.notificationStatusCode,
		notificationError: dto.notificationError ?? ''
	};
}

// ---- CRUD ------------------------------------------------------------------

export async function listAlertRules(signal?: AbortSignal): Promise<AlertRuleListResponse> {
	const res = await apiFetch(`${API_BASE_URL}/api/alerts`, { headers: memoryPackAcceptHeaders(), signal });
	if (!res.ok) {
		throw new Error(`GET /api/alerts failed: ${res.status} ${res.statusText}`);
	}
	const dto = GeneratedAlertRuleListResponse.deserialize(await res.arrayBuffer());
	return { rules: (dto?.rules ?? []).map((r) => toAlertRule(r!)) };
}

export async function getAlertRule(id: string, signal?: AbortSignal): Promise<AlertRule> {
	const res = await apiFetch(`${API_BASE_URL}/api/alerts/${id}`, { headers: memoryPackAcceptHeaders(), signal });
	if (!res.ok) {
		throw new Error(`GET /api/alerts/${id} failed: ${res.status} ${res.statusText}`);
	}
	return decodeAlertRule(res);
}

export async function createAlertRule(request: AlertRuleRequest): Promise<AlertRule> {
	const dto = toGeneratedAlertRuleRequest(request);
	const res = await apiFetch(`${API_BASE_URL}/api/alerts`, {
		method: 'POST',
		headers: memoryPackRequestHeaders(),
		body: memoryPackBody(GeneratedAlertRuleRequest.serialize(dto))
	});
	if (!res.ok) {
		throw new Error(`POST /api/alerts failed: ${res.status} ${res.statusText}`);
	}
	return decodeAlertRule(res);
}

export async function updateAlertRule(id: string, request: AlertRuleRequest): Promise<AlertRule> {
	const dto = toGeneratedAlertRuleRequest(request);
	const res = await apiFetch(`${API_BASE_URL}/api/alerts/${id}`, {
		method: 'PUT',
		headers: memoryPackRequestHeaders(),
		body: memoryPackBody(GeneratedAlertRuleRequest.serialize(dto))
	});
	if (!res.ok) {
		throw new Error(`PUT /api/alerts/${id} failed: ${res.status} ${res.statusText}`);
	}
	return decodeAlertRule(res);
}

/** 204 No Content on success - unlike every other function here, there's no body to decode. */
export async function deleteAlertRule(id: string): Promise<void> {
	const res = await apiFetch(`${API_BASE_URL}/api/alerts/${id}`, { method: 'DELETE' });
	if (!res.ok) {
		throw new Error(`DELETE /api/alerts/${id} failed: ${res.status} ${res.statusText}`);
	}
}

// ---- History -----------------------------------------------------------------

export async function getAlertHistory(id: string, limit = 50, signal?: AbortSignal): Promise<AlertHistoryResponse> {
	const res = await apiFetch(`${API_BASE_URL}/api/alerts/${id}/history?limit=${limit}`, { headers: memoryPackAcceptHeaders(), signal });
	if (!res.ok) {
		throw new Error(`GET /api/alerts/${id}/history failed: ${res.status} ${res.statusText}`);
	}
	const dto = GeneratedAlertHistoryResponse.deserialize(await res.arrayBuffer());
	return { events: (dto?.events ?? []).map((e) => toAlertHistoryEntry(e!)) };
}

// ---- Evaluation dry-runs -------------------------------------------------------

function toAlertTestResult(dto: GeneratedAlertTestResult): AlertTestResult {
	return {
		observedCount: Number(dto.observedCount),
		wouldFire: dto.wouldFire,
		evaluatedAt: dto.evaluatedAt.toISOString(),
		windowSeconds: dto.windowSeconds
	};
}

/** Dry-runs a saved rule by id - ignores cooldown, writes nothing. */
export async function testAlertRule(id: string): Promise<AlertTestResult> {
	const res = await apiFetch(`${API_BASE_URL}/api/alerts/${id}/test`, { method: 'POST', headers: memoryPackAcceptHeaders() });
	if (!res.ok) {
		throw new Error(`POST /api/alerts/${id}/test failed: ${res.status} ${res.statusText}`);
	}
	const dto = GeneratedAlertTestResult.deserialize(await res.arrayBuffer());
	if (dto == null) {
		throw new Error('Empty response body decoding AlertTestResult.');
	}
	return toAlertTestResult(dto);
}

/** Dry-runs an unsaved draft - lets the create/edit form show "would fire now" before Save. */
export async function testDraftAlertRule(request: AlertRuleRequest): Promise<AlertTestResult> {
	const dto = toGeneratedAlertRuleRequest(request);
	const res = await apiFetch(`${API_BASE_URL}/api/alerts/test`, {
		method: 'POST',
		headers: memoryPackRequestHeaders(),
		body: memoryPackBody(GeneratedAlertRuleRequest.serialize(dto))
	});
	if (!res.ok) {
		throw new Error(`POST /api/alerts/test failed: ${res.status} ${res.statusText}`);
	}
	const dtoResult = GeneratedAlertTestResult.deserialize(await res.arrayBuffer());
	if (dtoResult == null) {
		throw new Error('Empty response body decoding AlertTestResult.');
	}
	return toAlertTestResult(dtoResult);
}
