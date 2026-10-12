// Client for Flare.Api's shared alert-notification-template API (`/api/alert-templates` CRUD,
// ADR-0148). Plain JSON, like `projects-api.ts` - small flat records, no MemoryPack adapter.

import { API_BASE_URL, apiFetch } from './api';

export interface AlertTemplate {
	id: string;
	name: string;
	description: string;
	/** True for the instance-wide default, which applies to rules that pick no template. At most one. */
	isDefault: boolean;
	titleTemplate: string;
	bodyTemplate: string;
	/** Body for a resolved notification; empty falls back to `bodyTemplate`. */
	resolvedBodyTemplate: string;
	/** Fired-body override per notification channel type name (`Telegram`, `Email`, ...). */
	channelBodies: Record<string, string>;
	createdAt: string;
	updatedAt: string;
}

export type AlertTemplateRequest = Omit<AlertTemplate, 'id' | 'createdAt' | 'updatedAt'>;

/** `NotificationChannelType` names a template may carry a body for. */
export const TEMPLATE_CHANNEL_TYPES = ['Webhook', 'Telegram', 'Email', 'PagerDuty', 'Teams', 'Discord', 'Jira', 'IncidentIo', 'JsmOps'] as const;

const jsonHeaders = { 'Content-Type': 'application/json' };

async function expectOk(res: Response, what: string): Promise<void> {
	if (res.ok) return;
	let detail = '';
	try {
		detail = ((await res.json()) as { detail?: string }).detail ?? '';
	} catch {
		// not a problem+json body
	}
	throw new Error(detail || `${what} failed: ${res.status} ${res.statusText}`);
}

export async function listAlertTemplates(signal?: AbortSignal): Promise<AlertTemplate[]> {
	const res = await apiFetch(`${API_BASE_URL}/api/alert-templates`, { signal });
	await expectOk(res, 'GET /api/alert-templates');
	return (await res.json()) as AlertTemplate[];
}

export async function createAlertTemplate(request: AlertTemplateRequest): Promise<AlertTemplate> {
	const res = await apiFetch(`${API_BASE_URL}/api/alert-templates`, { method: 'POST', headers: jsonHeaders, body: JSON.stringify(request) });
	await expectOk(res, 'POST /api/alert-templates');
	return (await res.json()) as AlertTemplate;
}

export async function updateAlertTemplate(id: string, request: AlertTemplateRequest): Promise<AlertTemplate> {
	const res = await apiFetch(`${API_BASE_URL}/api/alert-templates/${id}`, { method: 'PUT', headers: jsonHeaders, body: JSON.stringify(request) });
	await expectOk(res, 'PUT /api/alert-templates/{id}');
	return (await res.json()) as AlertTemplate;
}

export async function deleteAlertTemplate(id: string): Promise<void> {
	const res = await apiFetch(`${API_BASE_URL}/api/alert-templates/${id}`, { method: 'DELETE' });
	await expectOk(res, 'DELETE /api/alert-templates/{id}');
}
