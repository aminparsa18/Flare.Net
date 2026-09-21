// Client for Flare.Api's pipeline-rule API (field-extraction/redaction rule CRUD, applied
// by Flare.Ingest at flush time - see docs-internal/adr/0033-pipeline-rules-extraction-redaction.md).
// Deliberately distinct name/module from `pipeline-api.ts` (the Ingestion page's
// pipeline-*health*-stats client) - different concern despite the similar name.
//
// MemoryPack over the wire, same shape as `alerts-api.ts`'s header comment describes.
// `condition` reuses `$lib/memorypack/LogFilter.ts`'s existing `LogFilter`/`logFilterFromPlain`/
// `logFilterToPlain` directly, same as `alerts-api.ts` does for `AlertRule.condition`.
// `ExtractRegexAction`/`RedactRegexAction`/`PipelineRuleAction` have no `DateTimeOffset`/
// `IReadOnlyList<T>` member (even nested), so all three are real MemoryPack-TS-*generated*
// classes, reused here directly - `kind` converts through `$lib/memorypack/enums.ts`'s
// `ruleActionKindToString`/`FromString`, same pattern `alerts-api.ts` uses for `conditionKind`.
// `PipelineRule`/`PipelineRuleRequest`/`PipelineRuleListResponse` themselves nest `LogFilter`
// and/or an `IReadOnlyList<PipelineRuleAction>`, so all three are hand-written
// (`$lib/memorypack/`), same reasoning `AlertRule.ts` documents.
//
// `previewPipelineRule`/`previewDraftPipelineRule` (Phase 2, see
// docs-internal/adr/0034-pipeline-rules-preview.md) mirror `alerts-api.ts`'s
// `testAlertRule`/`testDraftAlertRule` saved/draft pair. `PipelineRulePreviewMatch` has its
// own `DateTimeOffset` `timestamp`, so it and the `PipelineRulePreviewResult` that nests a
// list of it are both hand-written too, same reasoning as `PipelineRule` above.

import { API_BASE_URL, apiFetch, memoryPackAcceptHeaders, memoryPackBody, memoryPackRequestHeaders, type LogFilter } from './api';
import { ruleActionKindFromString, ruleActionKindToString, type RuleActionKindName } from '$lib/memorypack/enums';
import { logFilterFromPlain, logFilterToPlain } from '$lib/memorypack/LogFilter';
import { ExtractRegexAction as GeneratedExtractRegexAction } from '$lib/generated/memorypack/ExtractRegexAction.js';
import { RedactRegexAction as GeneratedRedactRegexAction } from '$lib/generated/memorypack/RedactRegexAction.js';
import { PipelineRuleAction as GeneratedPipelineRuleAction } from '$lib/generated/memorypack/PipelineRuleAction.js';
import { PipelineRule as GeneratedPipelineRule } from '$lib/memorypack/PipelineRule';
import { PipelineRuleRequest as GeneratedPipelineRuleRequest } from '$lib/memorypack/PipelineRuleRequest';
import { PipelineRuleListResponse as GeneratedPipelineRuleListResponse } from '$lib/memorypack/PipelineRuleListResponse';
import { PipelineRulePreviewMatch as GeneratedPipelineRulePreviewMatch } from '$lib/memorypack/PipelineRulePreviewMatch';
import { PipelineRulePreviewResult as GeneratedPipelineRulePreviewResult } from '$lib/memorypack/PipelineRulePreviewResult';

// ---- Shared shapes (PipelineRuleModels.cs) ---------------------------------

export type RuleActionKind = RuleActionKindName;

/** `RuleActionKind.ExtractRegex`'s config - see `ExtractRegexAction.cs`'s doc comment for the named-capture-group extraction rule. */
export interface ExtractRegexAction {
	/** Attribute key in the Log bag to read from; undefined reads Body instead. */
	sourceAttributeKey?: string;
	pattern: string;
}

/** `RuleActionKind.RedactRegex`'s config - see `RedactRegexAction.cs`'s doc comment. */
export interface RedactRegexAction {
	/** Attribute key in the Log bag to redact; undefined redacts Body instead. */
	sourceAttributeKey?: string;
	pattern: string;
	replacement: string;
}

/** One step of a `PipelineRule`'s ordered action list - meaningful sub-object depends on `kind`, same discriminator shape `AlertRule.conditionKind` uses. */
export interface PipelineRuleAction {
	kind: RuleActionKind;
	extractRegex?: ExtractRegexAction;
	redactRegex?: RedactRegexAction;
}

/** A saved field-extraction/redaction rule, applied by Flare.Ingest at flush time. `condition` reuses the same `LogFilter` shape the Logs Explorer/Alert rules already use - an empty condition deliberately matches every log (see the create/edit form's "matches all logs" notice). */
export interface PipelineRule {
	id: string;
	name: string;
	description: string;
	enabled: boolean;
	condition: LogFilter;
	/** Applied in list order against a matching event. */
	actions: PipelineRuleAction[];
	createdAt: string;
	updatedAt: string;
}

/** Create/update request body - same shape as `PipelineRule` minus the server-assigned fields. */
export interface PipelineRuleRequest {
	name: string;
	description?: string;
	enabled?: boolean;
	condition: LogFilter;
	actions: PipelineRuleAction[];
}

export interface PipelineRuleListResponse {
	rules: PipelineRule[];
}

/**
 * One sampled log's before/after preview - see `PipelineRulePreviewResult.matches`.
 * `beforeAttributes`/`afterAttributes` carry only the `Log` attribute bag, the one an
 * extraction/redaction action can ever touch.
 */
export interface PipelineRulePreviewMatch {
	eventId: string;
	timestamp: string;
	serviceName: string;
	beforeBody: string;
	afterBody: string;
	beforeAttributes: Record<string, string>;
	afterAttributes: Record<string, string>;
	/** True when at least one action actually changed `Body` or a `Log` attribute for this event - a rule can match a log without any action having an effect on this particular sample. */
	changed: boolean;
}

/**
 * Dry-run result from `POST /api/pipeline-rules/{id}/preview` or
 * `POST /api/pipeline-rules/preview`: a saved or draft rule's actions applied (in-process,
 * nothing written) against a bounded, most-recent-first sample of logs already matching its
 * own `condition`.
 */
export interface PipelineRulePreviewResult {
	/** How many logs the sample pulled - fewer than the server's cap if the condition (and its default lookback window, when `condition.from`/`to` are unset) doesn't match that many. */
	sampledCount: number;
	/** How many of `sampledCount` had at least one `PipelineRulePreviewMatch.changed` effect. */
	changedCount: number;
	matches: PipelineRulePreviewMatch[];
}

// ---- Conversions -------------------------------------------------------------

function toExtractRegexAction(dto: GeneratedExtractRegexAction): ExtractRegexAction {
	return { sourceAttributeKey: dto.sourceAttributeKey ?? undefined, pattern: dto.pattern ?? '' };
}

function toGeneratedExtractRegexAction(action: ExtractRegexAction): GeneratedExtractRegexAction {
	const dto = new GeneratedExtractRegexAction();
	dto.sourceAttributeKey = action.sourceAttributeKey ?? null;
	dto.pattern = action.pattern;
	return dto;
}

function toRedactRegexAction(dto: GeneratedRedactRegexAction): RedactRegexAction {
	return { sourceAttributeKey: dto.sourceAttributeKey ?? undefined, pattern: dto.pattern ?? '', replacement: dto.replacement ?? '***' };
}

function toGeneratedRedactRegexAction(action: RedactRegexAction): GeneratedRedactRegexAction {
	const dto = new GeneratedRedactRegexAction();
	dto.sourceAttributeKey = action.sourceAttributeKey ?? null;
	dto.pattern = action.pattern;
	dto.replacement = action.replacement;
	return dto;
}

function toPipelineRuleAction(dto: GeneratedPipelineRuleAction): PipelineRuleAction {
	return {
		kind: ruleActionKindToString(dto.kind),
		extractRegex: dto.extractRegex == null ? undefined : toExtractRegexAction(dto.extractRegex),
		redactRegex: dto.redactRegex == null ? undefined : toRedactRegexAction(dto.redactRegex)
	};
}

function toGeneratedPipelineRuleAction(action: PipelineRuleAction): GeneratedPipelineRuleAction {
	const dto = new GeneratedPipelineRuleAction();
	dto.kind = ruleActionKindFromString(action.kind);
	dto.extractRegex = action.extractRegex == null ? null : toGeneratedExtractRegexAction(action.extractRegex);
	dto.redactRegex = action.redactRegex == null ? null : toGeneratedRedactRegexAction(action.redactRegex);
	return dto;
}

function toPipelineRule(dto: GeneratedPipelineRule): PipelineRule {
	return {
		id: dto.id,
		name: dto.name ?? '',
		description: dto.description ?? '',
		enabled: dto.enabled,
		condition: logFilterToPlain(dto.condition!),
		actions: (dto.actions ?? []).filter((a): a is GeneratedPipelineRuleAction => a != null).map(toPipelineRuleAction),
		createdAt: dto.createdAt.toISOString(),
		updatedAt: dto.updatedAt.toISOString()
	};
}

async function decodePipelineRule(res: Response): Promise<PipelineRule> {
	const dto = GeneratedPipelineRule.deserialize(await res.arrayBuffer());
	if (dto == null) {
		throw new Error('Empty response body decoding PipelineRule.');
	}
	return toPipelineRule(dto);
}

function toGeneratedPipelineRuleRequest(request: PipelineRuleRequest): GeneratedPipelineRuleRequest {
	const dto = new GeneratedPipelineRuleRequest();
	dto.name = request.name;
	dto.description = request.description ?? null;
	dto.enabled = request.enabled ?? null;
	dto.condition = logFilterFromPlain(request.condition);
	dto.actions = request.actions.map(toGeneratedPipelineRuleAction);
	return dto;
}

// ---- CRUD ------------------------------------------------------------------

export async function listPipelineRules(signal?: AbortSignal): Promise<PipelineRuleListResponse> {
	const res = await apiFetch(`${API_BASE_URL}/api/pipeline-rules`, { headers: memoryPackAcceptHeaders(), signal });
	if (!res.ok) {
		throw new Error(`GET /api/pipeline-rules failed: ${res.status} ${res.statusText}`);
	}
	const dto = GeneratedPipelineRuleListResponse.deserialize(await res.arrayBuffer());
	return { rules: (dto?.rules ?? []).map((r) => toPipelineRule(r!)) };
}

export async function getPipelineRule(id: string, signal?: AbortSignal): Promise<PipelineRule> {
	const res = await apiFetch(`${API_BASE_URL}/api/pipeline-rules/${id}`, { headers: memoryPackAcceptHeaders(), signal });
	if (!res.ok) {
		throw new Error(`GET /api/pipeline-rules/${id} failed: ${res.status} ${res.statusText}`);
	}
	return decodePipelineRule(res);
}

export async function createPipelineRule(request: PipelineRuleRequest): Promise<PipelineRule> {
	const dto = toGeneratedPipelineRuleRequest(request);
	const res = await apiFetch(`${API_BASE_URL}/api/pipeline-rules`, {
		method: 'POST',
		headers: memoryPackRequestHeaders(),
		body: memoryPackBody(GeneratedPipelineRuleRequest.serialize(dto))
	});
	if (!res.ok) {
		throw new Error(`POST /api/pipeline-rules failed: ${res.status} ${res.statusText}`);
	}
	return decodePipelineRule(res);
}

export async function updatePipelineRule(id: string, request: PipelineRuleRequest): Promise<PipelineRule> {
	const dto = toGeneratedPipelineRuleRequest(request);
	const res = await apiFetch(`${API_BASE_URL}/api/pipeline-rules/${id}`, {
		method: 'PUT',
		headers: memoryPackRequestHeaders(),
		body: memoryPackBody(GeneratedPipelineRuleRequest.serialize(dto))
	});
	if (!res.ok) {
		throw new Error(`PUT /api/pipeline-rules/${id} failed: ${res.status} ${res.statusText}`);
	}
	return decodePipelineRule(res);
}

/** 204 No Content on success - unlike every other function here, there's no body to decode. */
export async function deletePipelineRule(id: string): Promise<void> {
	const res = await apiFetch(`${API_BASE_URL}/api/pipeline-rules/${id}`, { method: 'DELETE' });
	if (!res.ok) {
		throw new Error(`DELETE /api/pipeline-rules/${id} failed: ${res.status} ${res.statusText}`);
	}
}

// ---- Preview dry-runs --------------------------------------------------------

function toPipelineRulePreviewMatch(dto: GeneratedPipelineRulePreviewMatch): PipelineRulePreviewMatch {
	return {
		eventId: dto.eventId,
		timestamp: dto.timestamp.toISOString(),
		serviceName: dto.serviceName ?? '',
		beforeBody: dto.beforeBody ?? '',
		afterBody: dto.afterBody ?? '',
		beforeAttributes: dto.beforeAttributes ?? {},
		afterAttributes: dto.afterAttributes ?? {},
		changed: dto.changed
	};
}

function toPipelineRulePreviewResult(dto: GeneratedPipelineRulePreviewResult): PipelineRulePreviewResult {
	return {
		sampledCount: dto.sampledCount,
		changedCount: dto.changedCount,
		matches: (dto.matches ?? []).filter((m): m is GeneratedPipelineRulePreviewMatch => m != null).map(toPipelineRulePreviewMatch)
	};
}

/** Dry-runs a saved rule by id against a live sample of currently-matching logs - writes nothing. */
export async function previewPipelineRule(id: string): Promise<PipelineRulePreviewResult> {
	const res = await apiFetch(`${API_BASE_URL}/api/pipeline-rules/${id}/preview`, { method: 'POST', headers: memoryPackAcceptHeaders() });
	if (!res.ok) {
		throw new Error(`POST /api/pipeline-rules/${id}/preview failed: ${res.status} ${res.statusText}`);
	}
	const dto = GeneratedPipelineRulePreviewResult.deserialize(await res.arrayBuffer());
	if (dto == null) {
		throw new Error('Empty response body decoding PipelineRulePreviewResult.');
	}
	return toPipelineRulePreviewResult(dto);
}

/** Dry-runs an unsaved draft - lets the create/edit form show before/after sample matches before Save. */
export async function previewDraftPipelineRule(request: PipelineRuleRequest): Promise<PipelineRulePreviewResult> {
	const dto = toGeneratedPipelineRuleRequest(request);
	const res = await apiFetch(`${API_BASE_URL}/api/pipeline-rules/preview`, {
		method: 'POST',
		headers: memoryPackRequestHeaders(),
		body: memoryPackBody(GeneratedPipelineRuleRequest.serialize(dto))
	});
	if (!res.ok) {
		throw new Error(`POST /api/pipeline-rules/preview failed: ${res.status} ${res.statusText}`);
	}
	const dtoResult = GeneratedPipelineRulePreviewResult.deserialize(await res.arrayBuffer());
	if (dtoResult == null) {
		throw new Error('Empty response body decoding PipelineRulePreviewResult.');
	}
	return toPipelineRulePreviewResult(dtoResult);
}
