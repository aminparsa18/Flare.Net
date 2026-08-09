// Client for Flare.Api's Alerting API (rule CRUD, fired-alert history, evaluation
// dry-runs). Field names/casing/enum values are a hand-mirror of
// src/Flare.Api/Model/AlertModels.cs + Json/AlertsJsonContext.cs (camelCase properties,
// PascalCase string enums - same convention `api.ts` documents for LogsJsonContext).
// Keep in sync with those files by hand.

import { API_BASE_URL, type LogFilter } from './api';

// ---- Shared shapes (AlertModels.cs) ---------------------------------------

export type ThresholdComparator = 'GreaterThanOrEqual' | 'LessThan';

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

// ---- CRUD ------------------------------------------------------------------

export async function listAlertRules(signal?: AbortSignal): Promise<AlertRuleListResponse> {
	const res = await fetch(`${API_BASE_URL}/api/alerts`, { signal });
	if (!res.ok) {
		throw new Error(`GET /api/alerts failed: ${res.status} ${res.statusText}`);
	}
	return res.json();
}

export async function getAlertRule(id: string, signal?: AbortSignal): Promise<AlertRule> {
	const res = await fetch(`${API_BASE_URL}/api/alerts/${id}`, { signal });
	if (!res.ok) {
		throw new Error(`GET /api/alerts/${id} failed: ${res.status} ${res.statusText}`);
	}
	return res.json();
}

export async function createAlertRule(request: AlertRuleRequest): Promise<AlertRule> {
	const res = await fetch(`${API_BASE_URL}/api/alerts`, {
		method: 'POST',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify(request)
	});
	if (!res.ok) {
		throw new Error(`POST /api/alerts failed: ${res.status} ${res.statusText}`);
	}
	return res.json();
}

export async function updateAlertRule(id: string, request: AlertRuleRequest): Promise<AlertRule> {
	const res = await fetch(`${API_BASE_URL}/api/alerts/${id}`, {
		method: 'PUT',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify(request)
	});
	if (!res.ok) {
		throw new Error(`PUT /api/alerts/${id} failed: ${res.status} ${res.statusText}`);
	}
	return res.json();
}

/** 204 No Content on success - unlike every other function here, there's no JSON body to return. */
export async function deleteAlertRule(id: string): Promise<void> {
	const res = await fetch(`${API_BASE_URL}/api/alerts/${id}`, { method: 'DELETE' });
	if (!res.ok) {
		throw new Error(`DELETE /api/alerts/${id} failed: ${res.status} ${res.statusText}`);
	}
}

// ---- History -----------------------------------------------------------------

export async function getAlertHistory(id: string, limit = 50, signal?: AbortSignal): Promise<AlertHistoryResponse> {
	const res = await fetch(`${API_BASE_URL}/api/alerts/${id}/history?limit=${limit}`, { signal });
	if (!res.ok) {
		throw new Error(`GET /api/alerts/${id}/history failed: ${res.status} ${res.statusText}`);
	}
	return res.json();
}

// ---- Evaluation dry-runs -------------------------------------------------------

/** Dry-runs a saved rule by id - ignores cooldown, writes nothing. */
export async function testAlertRule(id: string): Promise<AlertTestResult> {
	const res = await fetch(`${API_BASE_URL}/api/alerts/${id}/test`, { method: 'POST' });
	if (!res.ok) {
		throw new Error(`POST /api/alerts/${id}/test failed: ${res.status} ${res.statusText}`);
	}
	return res.json();
}

/** Dry-runs an unsaved draft - lets the create/edit form show "would fire now" before Save. */
export async function testDraftAlertRule(request: AlertRuleRequest): Promise<AlertTestResult> {
	const res = await fetch(`${API_BASE_URL}/api/alerts/test`, {
		method: 'POST',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify(request)
	});
	if (!res.ok) {
		throw new Error(`POST /api/alerts/test failed: ${res.status} ${res.statusText}`);
	}
	return res.json();
}
