// Client for Flare.Api's `GET /api/audit-events` (src/Flare.Api/Endpoints/AuditLogEndpoints.cs,
// admin-only, ADR-0079). Plain JSON, not MemoryPack - a low-volume admin page whose response
// carries DateTimeOffsets.

import { API_BASE_URL, apiFetch } from "./api";

/** One changed field of an update. Secret values arrive already redacted (`[redacted]`). */
export interface AuditFieldChange {
  field: string;
  before: string | null;
  after: string | null;
}

export interface AuditEvent {
  id: number;
  timestamp: string;
  actorId: string | null;
  actorName: string;
  /** `session` or `pat` - how the actor authenticated. */
  actorKind: string;
  action: string;
  resourceType: string;
  resourceId: string | null;
  /** HTTP method + route template, e.g. `PUT /api/alerts/{id:guid}`. */
  route: string;
  statusCode: number;
  sourceIp: string | null;
  /** Changed fields for an update; empty for creates, deletes and unreported updates. */
  changes: AuditFieldChange[];
  /** Hash chain link (ADR-0202); null for events written before the chain existed. */
  prevHash: string | null;
  hash: string | null;
}

export interface AuditChainVerification {
  valid: boolean;
  chainedEvents: number;
  legacyEvents: number;
  firstBrokenId: number | null;
  reason: string | null;
  headHash: string | null;
}

export interface AuditEventPage {
  events: AuditEvent[];
  /** Pass back as `before` for the next (older) page; null on the last page. */
  nextBefore: number | null;
}

export interface AuditEventQuery {
  resourceType?: string;
  action?: string;
  before?: number;
  limit?: number;
}

/** Resource types the backend records - mirrors AuditActionClassifier's rule table. */
export const AUDIT_RESOURCE_TYPES = [
  "alert",
  "notification-channel",
  "maintenance-window",
  "pipeline-rule",
  "dashboard",
  "saved-view",
  "user",
  "access-token",
  "ingest-key",
  "auth-settings",
  "entra-settings",
  "ldap-settings",
  "oidc-settings",
  "proxy-auth-settings",
  "apdex-threshold",
  "source-link",
  "metric-metadata",
  "promoted-attribute",
] as const;

export async function listAuditEvents(
  query: AuditEventQuery,
  signal?: AbortSignal,
): Promise<AuditEventPage> {
  const params = new URLSearchParams();
  if (query.resourceType) params.set("resourceType", query.resourceType);
  if (query.action) params.set("action", query.action);
  if (query.before != null) params.set("before", String(query.before));
  if (query.limit != null) params.set("limit", String(query.limit));
  const res = await apiFetch(`${API_BASE_URL}/api/audit-events?${params}`, {
    signal,
  });
  if (!res.ok) {
    throw new Error(
      `GET /api/audit-events failed: ${res.status} ${res.statusText}`,
    );
  }
  return (await res.json()) as AuditEventPage;
}

export type AuditExportFormat = "csv" | "json" | "ndjson";

/** Walks the whole hash chain server-side (ADR-0202). */
export async function verifyAuditChain(
  signal?: AbortSignal,
): Promise<AuditChainVerification> {
  const res = await apiFetch(`${API_BASE_URL}/api/audit-events/verify`, {
    signal,
  });
  if (!res.ok) {
    throw new Error(
      `GET /api/audit-events/verify failed: ${res.status} ${res.statusText}`,
    );
  }
  return (await res.json()) as AuditChainVerification;
}

/** Downloads the filtered trail as a file via a temporary blob link (the export needs the session cookie / bearer, so a plain <a href> would not work cross-origin). */
export async function downloadAuditExport(
  format: AuditExportFormat,
  query: Pick<AuditEventQuery, "resourceType" | "action">,
): Promise<void> {
  const params = new URLSearchParams({ format });
  if (query.resourceType) params.set("resourceType", query.resourceType);
  if (query.action) params.set("action", query.action);
  const res = await apiFetch(
    `${API_BASE_URL}/api/audit-events/export?${params}`,
  );
  if (!res.ok) {
    throw new Error(
      `GET /api/audit-events/export failed: ${res.status} ${res.statusText}`,
    );
  }
  const blob = await res.blob();
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = `flare-audit.${format}`;
  link.click();
  URL.revokeObjectURL(url);
}
