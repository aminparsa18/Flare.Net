// Client for Flare.Api's Alerting API (rule CRUD, fired-alert history, evaluation
// dry-runs).
//
// Migrated (Phase 2 of docs-internal/investigations/memorypack-serialization-migration-scope.md)
// to MemoryPack - see `auth-api.ts`'s header comment for the general shape.
// `AlertThreshold` has no DateTimeOffset/JsonElement/IReadOnlyList member and uses a real
// generated class; every other type here nests `LogFilter` and/or has its own
// `DateTimeOffset` member, so all are hand-written (`$lib/memorypack/`).
// `comparator`/`condition` convert through `$lib/memorypack/enums.ts`/`LogFilter.ts`'s
// helpers, same reasoning `auth-api.ts` documents for `UserRole`. `conditionKind`/
// `aggregation`/`metricCondition.type` follow the same pattern, added for metric-threshold
// alerting (see `docs-internal/adr/0020-metric-threshold-alerting.md`) - `metricCondition`
// reuses `metrics-api.ts`'s `MetricFilter` plain type and
// `toGeneratedMetricFilter`/`fromGeneratedMetricFilter` conversions rather than re-deriving
// them. `channelIds`/`channelResults` were added for reusable notification channels (see
// `docs-internal/adr/0021-reusable-notification-channels.md`) - `channelResults`'
// `AlertChannelResult` reuses the MemoryPack-TS-*generated* class directly (no plain-type
// wrapper needed, same shape `AlertThreshold` already has), converting only its `type`
// field through `notificationChannelTypeToString`. `exceptionCondition` was added for
// exception-count alerting (see docs-internal/adr/0022-exception-count-alerting.md) -
// reuses `errors-api.ts`'s `ExceptionFilter` plain type and
// `toGeneratedExceptionFilter`/`fromGeneratedExceptionFilter` conversions, same "reuse the
// existing filter's conversions" precedent `metricCondition` set for `MetricFilter`.

import { API_BASE_URL, apiFetch, memoryPackAcceptHeaders, memoryPackBody, memoryPackRequestHeaders, type LogFilter } from './api';
import {
	thresholdComparatorFromString,
	thresholdComparatorToString,
	type ThresholdComparatorName,
	alertConditionKindFromString,
	alertConditionKindToString,
	type AlertConditionKindName,
	metricAlertAggregationFromString,
	metricAlertAggregationToString,
	type MetricAlertAggregationName,
	metricPointTypeFromString,
	metricPointTypeToString,
	notificationChannelTypeToString,
	type NotificationChannelTypeName,
} from '$lib/memorypack/enums';
import { logFilterFromPlain, logFilterToPlain } from '$lib/memorypack/LogFilter';
import { toGeneratedMetricFilter, fromGeneratedMetricFilter, type MetricFilter, type MetricPointType } from './metrics-api';
import { AlertThreshold as GeneratedAlertThreshold } from '$lib/generated/memorypack/AlertThreshold.js';
import { AlertChannelResult as GeneratedAlertChannelResult } from '$lib/generated/memorypack/AlertChannelResult.js';
import { AlertRule as GeneratedAlertRule } from '$lib/memorypack/AlertRule';
import { AlertRuleRequest as GeneratedAlertRuleRequest } from '$lib/memorypack/AlertRuleRequest';
import { AlertRuleListResponse as GeneratedAlertRuleListResponse } from '$lib/memorypack/AlertRuleListResponse';
import { AlertHistoryResponse as GeneratedAlertHistoryResponse } from '$lib/memorypack/AlertHistoryResponse';
import { AlertTestResult as GeneratedAlertTestResult } from '$lib/memorypack/AlertTestResult';
import type { AlertHistoryEntry as GeneratedAlertHistoryEntry } from '$lib/memorypack/AlertHistoryEntry';
import { AlertNotificationTestResult as GeneratedAlertNotificationTestResult } from '$lib/generated/memorypack/AlertNotificationTestResult.js';
import { MetricAlertCondition as GeneratedMetricAlertCondition } from '$lib/memorypack/MetricAlertCondition';
import { ExceptionCountCondition as GeneratedExceptionCountCondition } from '$lib/memorypack/ExceptionCountCondition';
import { type ExceptionFilter, toGeneratedExceptionFilter, fromGeneratedExceptionFilter } from './errors-api';

// ---- Shared shapes (AlertModels.cs) ---------------------------------------

export type ThresholdComparator = ThresholdComparatorName;

export type AlertConditionKind = AlertConditionKindName;

export type MetricAlertAggregation = MetricAlertAggregationName;

/** The `AlertConditionKind.MetricThreshold` counterpart to `LogFilter` - see `AlertRule.condition`'s comment. */
export interface MetricAlertCondition {
	metricName: string;
	type: MetricPointType;
	filter?: MetricFilter;
	aggregation: MetricAlertAggregation;
}

export interface AlertThreshold {
	count: number;
	comparator: ThresholdComparator;
}

/**
 * The `AlertConditionKind.ExceptionCount` counterpart to `LogFilter` - see
 * `AlertRule.condition`'s comment. `exceptionMessage` empty/undefined matches every message
 * for `exceptionType` - see `ExceptionCountCondition.ExceptionMessage`'s C#-side doc comment.
 */
export interface ExceptionCountCondition {
	exceptionType: string;
	exceptionMessage?: string;
	filter?: ExceptionFilter;
}

/**
 * A saved threshold/query-based alert rule. `condition` reuses the same `LogFilter` shape
 * the Logs Explorer filters with - meaningful only when `conditionKind` is `'LogCount'`
 * (the default/original behavior); `metricCondition`/`metricThresholdValue` carry a
 * `'MetricThreshold'` rule's condition instead - see
 * `docs-internal/adr/0020-metric-threshold-alerting.md`. `exceptionCondition` carries an
 * `'ExceptionCount'` rule's condition instead - see
 * `docs-internal/adr/0022-exception-count-alerting.md`.
 */
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
	/** A PagerDuty Events API v2 integration/routing key. Unlike `emailTo`, there's no app-wide server config to go with it. */
	pagerDutyRoutingKey: string;
	createdAt: string;
	updatedAt: string;
	conditionKind: AlertConditionKind;
	/** Set only when `conditionKind` is `'MetricThreshold'`. */
	metricCondition?: MetricAlertCondition;
	/** Set only when `conditionKind` is `'MetricThreshold'` - compared against via `threshold.comparator` (`threshold.count` is ignored/a placeholder for this kind). */
	metricThresholdValue?: number;
	/** Saved `NotificationChannel` IDs this rule fans out to - mutually exclusive with `webhookUrl`/`telegramBotToken`+`telegramChatId`/`emailTo`/`pagerDutyRoutingKey` above. Empty for a rule still using its legacy inline channel. */
	channelIds: string[];
	/** Set only when `conditionKind` is `'ExceptionCount'`. */
	exceptionCondition?: ExceptionCountCondition;
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
	pagerDutyRoutingKey?: string;
	/** Omitted/undefined defaults to `'LogCount'`, same as the saved-rule default. */
	conditionKind?: AlertConditionKind;
	metricCondition?: MetricAlertCondition;
	metricThresholdValue?: number;
	/** See `AlertRule.channelIds`'s doc comment. */
	channelIds?: string[];
	exceptionCondition?: ExceptionCountCondition;
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
	/** Snapshot of the firing rule's condition kind at fire time. */
	conditionKind: AlertConditionKind;
	/** Set only for a `'MetricThreshold'` event; undefined for a `'LogCount'` one, which uses `observedCount` instead. */
	observedValue?: number;
	/** Set only for a `'MetricThreshold'` event; undefined for a `'LogCount'` one, which uses `thresholdCount` instead. */
	thresholdValue?: number;
	/** Per-channel outcome of this fire - one entry per channel the rule fanned out to. `notificationStatus`/`notificationStatusCode`/`notificationError` above stay as the backward-compatible summary across all of them. */
	channelResults: AlertChannelResult[];
}

/** One channel's outcome within a fan-out fire - `AlertHistoryEntry.channelResults`'s element shape. */
export interface AlertChannelResult {
	/** Null for a fire through a legacy inline channel (never a saved, named `NotificationChannel`) - `channelName` still carries a human-readable label ("Webhook"/"Telegram"/"Email"/"PagerDuty") in that case. */
	channelId?: string;
	channelName: string;
	type: NotificationChannelTypeName;
	success: boolean;
	statusCode: number;
	error: string;
}

export interface AlertHistoryResponse {
	events: AlertHistoryEntry[];
}

/** Dry-run result: evaluates a rule/draft's condition+threshold against current data without touching cooldown state or sending a notification. */
export interface AlertTestResult {
	/** Meaningful only for a `'LogCount'` rule/draft - 0 for `'MetricThreshold'`, which reports its result via `observedValue` instead. */
	observedCount: number;
	wouldFire: boolean;
	evaluatedAt: string;
	windowSeconds: number;
	conditionKind: AlertConditionKind;
	/** Set only when `conditionKind` is `'MetricThreshold'`. */
	observedValue?: number;
}

/** "Send test alert" result: actually notified through the rule/draft's configured channel - unlike `AlertTestResult`, which never notifies. */
export interface AlertNotificationTestResult {
	success: boolean;
	/** The channel's own status code where one exists (HTTP status for webhook/Slack/Telegram/PagerDuty); 0 for Email, which has none. */
	statusCode: number;
	error: string;
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

function toMetricAlertCondition(dto: GeneratedMetricAlertCondition | null): MetricAlertCondition | undefined {
	if (dto == null) return undefined;
	return {
		metricName: dto.metricName ?? '',
		type: metricPointTypeToString(dto.type),
		filter: fromGeneratedMetricFilter(dto.filter),
		aggregation: metricAlertAggregationToString(dto.aggregation)
	};
}

function toGeneratedMetricAlertCondition(condition: MetricAlertCondition | undefined): GeneratedMetricAlertCondition | null {
	if (condition == null) return null;
	const dto = new GeneratedMetricAlertCondition();
	dto.metricName = condition.metricName;
	dto.type = metricPointTypeFromString(condition.type);
	dto.filter = toGeneratedMetricFilter(condition.filter);
	dto.aggregation = metricAlertAggregationFromString(condition.aggregation);
	return dto;
}

function toExceptionCountCondition(dto: GeneratedExceptionCountCondition | null): ExceptionCountCondition | undefined {
	if (dto == null) return undefined;
	return {
		exceptionType: dto.exceptionType ?? '',
		exceptionMessage: dto.exceptionMessage || undefined,
		filter: fromGeneratedExceptionFilter(dto.filter)
	};
}

function toGeneratedExceptionCountCondition(condition: ExceptionCountCondition | undefined): GeneratedExceptionCountCondition | null {
	if (condition == null) return null;
	const dto = new GeneratedExceptionCountCondition();
	dto.exceptionType = condition.exceptionType;
	dto.exceptionMessage = condition.exceptionMessage ?? '';
	dto.filter = toGeneratedExceptionFilter(condition.filter);
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
		pagerDutyRoutingKey: dto.pagerDutyRoutingKey ?? '',
		createdAt: dto.createdAt.toISOString(),
		updatedAt: dto.updatedAt.toISOString(),
		conditionKind: alertConditionKindToString(dto.conditionKind),
		metricCondition: toMetricAlertCondition(dto.metricCondition),
		metricThresholdValue: dto.metricThresholdValue ?? undefined,
		channelIds: (dto.channelIds ?? []).filter((id): id is string => id != null),
		exceptionCondition: toExceptionCountCondition(dto.exceptionCondition)
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
	dto.pagerDutyRoutingKey = request.pagerDutyRoutingKey ?? null;
	dto.conditionKind = request.conditionKind == null ? null : alertConditionKindFromString(request.conditionKind);
	dto.metricCondition = toGeneratedMetricAlertCondition(request.metricCondition);
	dto.metricThresholdValue = request.metricThresholdValue ?? null;
	dto.channelIds = request.channelIds ?? null;
	dto.exceptionCondition = toGeneratedExceptionCountCondition(request.exceptionCondition);
	return dto;
}

function toAlertChannelResult(dto: GeneratedAlertChannelResult): AlertChannelResult {
	return {
		channelId: dto.channelId ?? undefined,
		channelName: dto.channelName ?? '',
		type: notificationChannelTypeToString(dto.type),
		success: dto.success,
		statusCode: dto.statusCode,
		error: dto.error ?? ''
	};
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
		notificationError: dto.notificationError ?? '',
		conditionKind: alertConditionKindToString(dto.conditionKind),
		observedValue: dto.observedValue ?? undefined,
		thresholdValue: dto.thresholdValue ?? undefined,
		channelResults: (dto.channelResults ?? []).filter((r): r is GeneratedAlertChannelResult => r != null).map(toAlertChannelResult)
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
		windowSeconds: dto.windowSeconds,
		conditionKind: alertConditionKindToString(dto.conditionKind),
		observedValue: dto.observedValue ?? undefined
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

// ---- "Send test alert" (actually notifies, unlike the dry-runs above) -----------

function toAlertNotificationTestResult(dto: GeneratedAlertNotificationTestResult): AlertNotificationTestResult {
	return {
		success: dto.success,
		statusCode: dto.statusCode,
		error: dto.error ?? ''
	};
}

/** Sends a real test notification through a saved rule's configured channel - ignores cooldown, writes nothing to history. */
export async function sendTestAlertRule(id: string): Promise<AlertNotificationTestResult> {
	const res = await apiFetch(`${API_BASE_URL}/api/alerts/${id}/send-test`, { method: 'POST', headers: memoryPackAcceptHeaders() });
	if (!res.ok) {
		throw new Error(`POST /api/alerts/${id}/send-test failed: ${res.status} ${res.statusText}`);
	}
	const dto = GeneratedAlertNotificationTestResult.deserialize(await res.arrayBuffer());
	if (dto == null) {
		throw new Error('Empty response body decoding AlertNotificationTestResult.');
	}
	return toAlertNotificationTestResult(dto);
}

/** Sends a real test notification through an unsaved draft's configured channel - lets the create/edit form verify a channel before Save. */
export async function sendTestDraftAlertRule(request: AlertRuleRequest): Promise<AlertNotificationTestResult> {
	const dto = toGeneratedAlertRuleRequest(request);
	const res = await apiFetch(`${API_BASE_URL}/api/alerts/send-test`, {
		method: 'POST',
		headers: memoryPackRequestHeaders(),
		body: memoryPackBody(GeneratedAlertRuleRequest.serialize(dto))
	});
	if (!res.ok) {
		throw new Error(`POST /api/alerts/send-test failed: ${res.status} ${res.statusText}`);
	}
	const dtoResult = GeneratedAlertNotificationTestResult.deserialize(await res.arrayBuffer());
	if (dtoResult == null) {
		throw new Error('Empty response body decoding AlertNotificationTestResult.');
	}
	return toAlertNotificationTestResult(dtoResult);
}
