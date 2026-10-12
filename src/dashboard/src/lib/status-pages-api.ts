// Client for Flare.Api's status page API: admin CRUD under `/api/status-pages`, and the unauthenticated
// `GET /api/public/status/{slug}` the public /status/{slug} route renders. JSON over the wire.
// See `docs-internal/adr/0158-status-pages.md`.

import { API_BASE_URL, apiFetch } from './api';

export type StatusComponentKind = 'Monitor' | 'Slo';
export type StatusState = 'Unknown' | 'Operational' | 'Degraded' | 'Outage';

export interface StatusPageComponent {
	name: string;
	kind: StatusComponentKind;
	refId: string;
}

export interface StatusPage {
	id: string;
	slug: string;
	title: string;
	description: string;
	enabled: boolean;
	components: StatusPageComponent[];
	/** Notification channel ids told about every incident on this page (ADR-0161). */
	subscriberChannelIds: string[];
	/** A host the page is also served on (ADR-0165); empty for none. */
	domain: string;
	logoUrl: string;
	/** `#rrggbb`, or empty for the default. */
	accentColor: string;
	supportUrl: string;
	createdAt: string;
	updatedAt: string;
}

export interface StatusPageRequest {
	slug: string;
	title: string;
	description: string;
	enabled: boolean;
	components: StatusPageComponent[];
	/** Omit to leave an existing page's subscribers as they are. */
	subscriberChannelIds?: string[];
	/** Omit to leave the existing value as it is; an empty string clears it. */
	domain?: string;
	logoUrl?: string;
	accentColor?: string;
	supportUrl?: string;
}

export interface StatusDay {
	/** UTC `yyyy-MM-dd`. */
	date: string;
	/** Null when nothing was recorded that day. */
	uptimePercent: number | null;
}

export interface PublicStatusComponent {
	/** Opaque per-page handle to subscribe to this component. */
	key: string;
	name: string;
	state: StatusState;
	uptimePercent: number | null;
	days: StatusDay[];
}

export type StatusIncidentStatus = 'Investigating' | 'Identified' | 'Monitoring' | 'Resolved';
export const INCIDENT_STATUSES: StatusIncidentStatus[] = ['Investigating', 'Identified', 'Monitoring', 'Resolved'];

export interface StatusIncidentUpdate {
	at: string;
	status: StatusIncidentStatus;
	message: string;
}

/** Admin view: the timeline is oldest first. */
export interface StatusIncident {
	id: string;
	pageId: string;
	title: string;
	updates: StatusIncidentUpdate[];
	/** `refId`s of the page components this incident affects. */
	components: string[];
	createdAt: string;
	updatedAt: string;
	status: StatusIncidentStatus;
	resolvedAt: string | null;
}

/** Public view: the timeline is newest first. */
export interface PublicStatusIncident {
	title: string;
	status: StatusIncidentStatus;
	startedAt: string;
	resolvedAt: string | null;
	updates: StatusIncidentUpdate[];
	/** Display names of the affected components. */
	components: string[];
}

export interface PublicStatusPage {
	title: string;
	description: string;
	overall: StatusState;
	generatedAt: string;
	components: PublicStatusComponent[];
	incidents: PublicStatusIncident[];
	/** Whether visitors can sign up for incident emails (the server has SMTP and a public URL). */
	subscribable: boolean;
	logoUrl: string;
	accentColor: string;
	supportUrl: string;
}

/** Lowercase letters, digits and inner hyphens, 1-64 characters. Mirrors `StatusPageRequest.IsValidSlug`. */
export function isValidSlug(slug: string): boolean {
	return /^[a-z0-9]([a-z0-9-]{0,62}[a-z0-9])?$/.test(slug);
}

async function failure(res: Response, what: string): Promise<Error> {
	let message = `${what} failed: ${res.status} ${res.statusText}`;
	try {
		const problem = await res.json();
		message = problem?.detail || problem?.title || message;
	} catch {
		// Not JSON - keep the generic message.
	}
	return new Error(message);
}

const jsonHeaders = { 'Content-Type': 'application/json', Accept: 'application/json' };

export async function listStatusPages(signal?: AbortSignal): Promise<StatusPage[]> {
	const res = await apiFetch(`${API_BASE_URL}/api/status-pages`, { headers: { Accept: 'application/json' }, signal });
	if (!res.ok) throw await failure(res, 'GET /api/status-pages');
	return ((await res.json()) as { pages?: StatusPage[] }).pages ?? [];
}

export async function createStatusPage(request: StatusPageRequest): Promise<StatusPage> {
	const res = await apiFetch(`${API_BASE_URL}/api/status-pages`, { method: 'POST', headers: jsonHeaders, body: JSON.stringify(request) });
	if (!res.ok) throw await failure(res, 'POST /api/status-pages');
	return (await res.json()) as StatusPage;
}

export async function updateStatusPage(id: string, request: StatusPageRequest): Promise<StatusPage> {
	const res = await apiFetch(`${API_BASE_URL}/api/status-pages/${id}`, { method: 'PUT', headers: jsonHeaders, body: JSON.stringify(request) });
	if (!res.ok) throw await failure(res, `PUT /api/status-pages/${id}`);
	return (await res.json()) as StatusPage;
}

/** 204 No Content on success. */
export async function deleteStatusPage(id: string): Promise<void> {
	const res = await apiFetch(`${API_BASE_URL}/api/status-pages/${id}`, { method: 'DELETE' });
	if (!res.ok) throw await failure(res, `DELETE /api/status-pages/${id}`);
}

export async function listStatusIncidents(pageId: string): Promise<StatusIncident[]> {
	const res = await apiFetch(`${API_BASE_URL}/api/status-pages/${pageId}/incidents`, { headers: { Accept: 'application/json' } });
	if (!res.ok) throw await failure(res, 'GET /api/status-pages/incidents');
	return ((await res.json()) as { incidents?: StatusIncident[] }).incidents ?? [];
}

export async function openStatusIncident(pageId: string, request: { title: string; status: StatusIncidentStatus; message: string; components: string[] }): Promise<StatusIncident> {
	const res = await apiFetch(`${API_BASE_URL}/api/status-pages/${pageId}/incidents`, { method: 'POST', headers: jsonHeaders, body: JSON.stringify(request) });
	if (!res.ok) throw await failure(res, 'POST /api/status-pages/incidents');
	return (await res.json()) as StatusIncident;
}

export async function postStatusIncidentUpdate(pageId: string, incidentId: string, request: { status: StatusIncidentStatus; message: string; components: string[] }): Promise<StatusIncident> {
	const res = await apiFetch(`${API_BASE_URL}/api/status-pages/${pageId}/incidents/${incidentId}/updates`, { method: 'POST', headers: jsonHeaders, body: JSON.stringify(request) });
	if (!res.ok) throw await failure(res, 'POST /api/status-pages/incidents/updates');
	return (await res.json()) as StatusIncident;
}

/** 204 No Content on success. */
export async function deleteStatusIncident(pageId: string, incidentId: string): Promise<void> {
	const res = await apiFetch(`${API_BASE_URL}/api/status-pages/${pageId}/incidents/${incidentId}`, { method: 'DELETE' });
	if (!res.ok) throw await failure(res, 'DELETE /api/status-pages/incidents');
}

/** Null when no enabled page has this slug. Sends no credentials: the endpoint is public. */
export async function getPublicStatus(slug: string, signal?: AbortSignal): Promise<PublicStatusPage | null> {
	const res = await fetch(`${API_BASE_URL}/api/public/status/${encodeURIComponent(slug)}`, { headers: { Accept: 'application/json' }, signal });
	if (res.status === 404) return null;
	if (!res.ok) throw await failure(res, 'GET /api/public/status');
	return (await res.json()) as PublicStatusPage;
}

/** `POST /api/public/status/{slug}/subscribe` - asks for a confirmation email. `components` are component keys; empty means every component. Always succeeds for a valid address, whether or not it was already subscribed. */
export async function subscribeToStatus(slug: string, email: string, components: string[] = []): Promise<void> {
	const res = await fetch(`${API_BASE_URL}/api/public/status/${encodeURIComponent(slug)}/subscribe`, {
		method: 'POST',
		headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
		body: JSON.stringify({ email, components })
	});
	if (!res.ok) throw await failure(res, 'POST /api/public/status/subscribe');
}

export type SubscriptionAction = 'confirm' | 'unsubscribe';

export interface SubscriptionInfo {
	pageTitle: string;
	email: string;
}

/** Describes what a signed subscription link is for. Changes nothing, so a mail scanner fetching the link is harmless. */
export async function getSubscriptionInfo(action: SubscriptionAction, token: string, signal?: AbortSignal): Promise<SubscriptionInfo> {
	const res = await fetch(`${API_BASE_URL}/api/public/status/subscriptions/${action}?token=${encodeURIComponent(token)}`, { headers: { Accept: 'application/json' }, signal });
	if (!res.ok) throw await failure(res, `GET /api/public/status/subscriptions/${action}`);
	return (await res.json()) as SubscriptionInfo;
}

/** Confirms or removes the subscription the signed link was sent for. Unauthenticated: the token is the credential. */
export async function redeemSubscription(action: SubscriptionAction, token: string): Promise<SubscriptionInfo> {
	const res = await fetch(`${API_BASE_URL}/api/public/status/subscriptions/${action}`, {
		method: 'POST',
		headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
		body: JSON.stringify({ token })
	});
	if (!res.ok) throw await failure(res, `POST /api/public/status/subscriptions/${action}`);
	return (await res.json()) as SubscriptionInfo;
}

export interface SubscriptionPreferences {
	pageTitle: string;
	email: string;
	components: { key: string; name: string }[];
	/** Keys currently chosen; empty means every component. */
	selected: string[];
}

/** Describes a verified subscriber's component selection for a signed preferences link. Changes nothing. */
export async function getSubscriptionPreferences(token: string, signal?: AbortSignal): Promise<SubscriptionPreferences> {
	const res = await fetch(`${API_BASE_URL}/api/public/status/subscriptions/preferences?token=${encodeURIComponent(token)}`, { headers: { Accept: 'application/json' }, signal });
	if (!res.ok) throw await failure(res, 'GET /api/public/status/subscriptions/preferences');
	return (await res.json()) as SubscriptionPreferences;
}

/** Saves the components a subscriber hears about; empty means every component. Unauthenticated: the token is the credential. */
export async function saveSubscriptionPreferences(token: string, components: string[]): Promise<SubscriptionPreferences> {
	const res = await fetch(`${API_BASE_URL}/api/public/status/subscriptions/preferences`, {
		method: 'POST',
		headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
		body: JSON.stringify({ token, components })
	});
	if (!res.ok) throw await failure(res, 'POST /api/public/status/subscriptions/preferences');
	return (await res.json()) as SubscriptionPreferences;
}
