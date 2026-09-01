// Hand-written adapters between MemoryPack's generated numeric `const enum`s and the
// string-literal union types this dashboard's components already compare against (e.g.
// `auth.currentUser.role === 'Admin'` in `+layout.svelte`/`nav-links.ts`).
//
// MemoryPack's TypeScript generator encodes a C# enum as its raw numeric ordinal (`const
// enum UserRole { Admin = 0, Member = 1, Viewer = 2 }` - see
// `$lib/generated/memorypack/UserRole.ts`), never its member name - unlike the JSON path,
// which used `UseStringEnumConverter` to put the name itself on the wire. Every one of
// this dashboard's ~20 components that reads a role/status/kind field does so as a string
// literal, not a number, and none of those components are in this migration's scope (only
// the 16 `lib/*-api.ts` + `api.ts` client files are - see
// docs-internal/investigations/memorypack-serialization-migration-scope.md's Phase 2
// section). Rather than widen the diff to every consumer, every hand-written `-api.ts`
// function that touches an enum-bearing field converts at the boundary: decode the
// generated class, map its numeric field through this file back to the exact same string
// literal the JSON path always produced, and return the same public interface shape this
// module already exported - zero change needed outside `lib/`.

import { UserRole } from '$lib/generated/memorypack/UserRole.js';
import { IngestionSignal } from '$lib/generated/memorypack/IngestionSignal.js';
import { ThresholdComparator } from '$lib/generated/memorypack/ThresholdComparator.js';
import { MetricPointType } from '$lib/generated/memorypack/MetricPointType.js';

/** Matches `AuthModels.cs`'s `Flare.Identity.Users.UserRole` member order exactly - see that file's remarks on why renaming a member is a schema/claims-breaking change, not just a rename (so this order is exactly as stable as the enum itself). */
const USER_ROLE_NAMES = ['Admin', 'Member', 'Viewer'] as const;

export type UserRoleName = (typeof USER_ROLE_NAMES)[number];

export function userRoleToString(value: UserRole): UserRoleName {
	return USER_ROLE_NAMES[value];
}

export function userRoleFromString(value: UserRoleName): UserRole {
	return USER_ROLE_NAMES.indexOf(value) as UserRole;
}

/** Matches `IngestionModels.cs`'s `IngestionSignal` member order. */
const INGESTION_SIGNAL_NAMES = ['Logs', 'Traces', 'Metrics'] as const;

export type IngestionSignalName = (typeof INGESTION_SIGNAL_NAMES)[number];

export function ingestionSignalToString(value: IngestionSignal): IngestionSignalName {
	return INGESTION_SIGNAL_NAMES[value];
}

export function ingestionSignalFromString(value: IngestionSignalName): IngestionSignal {
	return INGESTION_SIGNAL_NAMES.indexOf(value) as IngestionSignal;
}

/** Matches `IngestionModels.cs`'s `IngestionProtocol` member order. Not itself MemoryPack-TS-generated (no generated type references it - `IngestionBucketPoint`, its only consumer, is hand-written), so this is a plain hand-coded ordinal mapping, not paired with a `$lib/generated/memorypack/IngestionProtocol.js` import the way `IngestionSignal` above is. */
const INGESTION_PROTOCOL_NAMES = ['Grpc', 'Http', 'Scrape'] as const;

export type IngestionProtocolName = (typeof INGESTION_PROTOCOL_NAMES)[number];

export function ingestionProtocolToString(value: number): IngestionProtocolName {
	return INGESTION_PROTOCOL_NAMES[value];
}

export function ingestionProtocolFromString(value: IngestionProtocolName): number {
	return INGESTION_PROTOCOL_NAMES.indexOf(value);
}

/** Matches `SavedViewModels.cs`'s `SavedViewPageType` member order. Not itself MemoryPack-TS-generated (no generated type references it - every consumer, `SavedView`/`SavedViewRequest`, is hand-written because of `State`'s `JsonElement` member - see `SavedView.ts`'s header comment), so this is a plain hand-coded ordinal mapping. */
const SAVED_VIEW_PAGE_TYPE_NAMES = ['Logs', 'Traces', 'Metrics'] as const;

export type SavedViewPageTypeName = (typeof SAVED_VIEW_PAGE_TYPE_NAMES)[number];

export function savedViewPageTypeToString(value: number): SavedViewPageTypeName {
	return SAVED_VIEW_PAGE_TYPE_NAMES[value];
}

export function savedViewPageTypeFromString(value: SavedViewPageTypeName): number {
	return SAVED_VIEW_PAGE_TYPE_NAMES.indexOf(value);
}

/** Matches `LogFilter.cs`'s `AttributeBag` member order. Not itself MemoryPack-TS-generated - see `SavedViewPageTypeName`'s comment above for why (`AttributeFilter`, its only consumer, is hand-written). */
const ATTRIBUTE_BAG_NAMES = ['Log', 'Resource', 'Scope'] as const;

export type AttributeBagName = (typeof ATTRIBUTE_BAG_NAMES)[number];

export function attributeBagToString(value: number): AttributeBagName {
	return ATTRIBUTE_BAG_NAMES[value];
}

export function attributeBagFromString(value: AttributeBagName): number {
	return ATTRIBUTE_BAG_NAMES.indexOf(value);
}

/** Matches `AlertModels.cs`'s `ThresholdComparator` member order. */
const THRESHOLD_COMPARATOR_NAMES = ['GreaterThanOrEqual', 'LessThan'] as const;

export type ThresholdComparatorName = (typeof THRESHOLD_COMPARATOR_NAMES)[number];

export function thresholdComparatorToString(value: ThresholdComparator): ThresholdComparatorName {
	return THRESHOLD_COMPARATOR_NAMES[value];
}

export function thresholdComparatorFromString(value: ThresholdComparatorName): ThresholdComparator {
	return THRESHOLD_COMPARATOR_NAMES.indexOf(value) as ThresholdComparator;
}

/** Matches `MetricModels.cs`'s `MetricPointType` member order. */
const METRIC_POINT_TYPE_NAMES = ['Gauge', 'Sum', 'Histogram'] as const;

export type MetricPointTypeName = (typeof METRIC_POINT_TYPE_NAMES)[number];

export function metricPointTypeToString(value: MetricPointType): MetricPointTypeName {
	return METRIC_POINT_TYPE_NAMES[value];
}

export function metricPointTypeFromString(value: MetricPointTypeName): MetricPointType {
	return METRIC_POINT_TYPE_NAMES.indexOf(value) as MetricPointType;
}

/** Matches `SpanFilter.cs`'s `SpanAttributeBag` member order. Not itself MemoryPack-TS-generated - see `SavedViewPageTypeName`'s comment for why (`SpanAttributeFilter`, its only consumer, is hand-written). */
const SPAN_ATTRIBUTE_BAG_NAMES = ['Span', 'Resource', 'Scope'] as const;

export type SpanAttributeBagName = (typeof SPAN_ATTRIBUTE_BAG_NAMES)[number];

export function spanAttributeBagToString(value: number): SpanAttributeBagName {
	return SPAN_ATTRIBUTE_BAG_NAMES[value];
}

export function spanAttributeBagFromString(value: SpanAttributeBagName): number {
	return SPAN_ATTRIBUTE_BAG_NAMES.indexOf(value);
}

/** Matches `ResourceGraphDto.cs`'s `ResourceState` member order. Not itself MemoryPack-TS-generated - see `SavedViewPageTypeName`'s comment for why (`ResourceNodeDto`, its only consumer, is hand-written - its `Urls: IReadOnlyList<string>` member alone blocks `[GenerateTypeScript]`, same reasoning as `PipelineServiceBreakdown.ts`). */
const RESOURCE_STATE_NAMES = ['Unknown', 'Running', 'Exited', 'Restarting', 'Paused'] as const;

export type ResourceStateName = (typeof RESOURCE_STATE_NAMES)[number];

export function resourceStateToString(value: number): ResourceStateName {
	return RESOURCE_STATE_NAMES[value];
}

export function resourceStateFromString(value: ResourceStateName): number {
	return RESOURCE_STATE_NAMES.indexOf(value);
}

/** Matches `ResourceGraphDto.cs`'s `ResourceHealth` member order. */
const RESOURCE_HEALTH_NAMES = ['Starting', 'Healthy', 'Unhealthy'] as const;

export type ResourceHealthName = (typeof RESOURCE_HEALTH_NAMES)[number];

export function resourceHealthToString(value: number): ResourceHealthName {
	return RESOURCE_HEALTH_NAMES[value];
}

export function resourceHealthFromString(value: ResourceHealthName): number {
	return RESOURCE_HEALTH_NAMES.indexOf(value);
}

/** Matches `LogAggregateRequest.cs`'s `LogAggregateGroupBy` member order. Not itself MemoryPack-TS-generated - see `SavedViewPageTypeName`'s comment for why (`LogAggregateRequest`, its only consumer, is hand-written - it nests `LogFilter`). */
const LOG_AGGREGATE_GROUP_BY_NAMES = ['None', 'Service', 'Level'] as const;

export type LogAggregateGroupByName = (typeof LOG_AGGREGATE_GROUP_BY_NAMES)[number];

export function logAggregateGroupByToString(value: number): LogAggregateGroupByName {
	return LOG_AGGREGATE_GROUP_BY_NAMES[value];
}

export function logAggregateGroupByFromString(value: LogAggregateGroupByName): number {
	return LOG_AGGREGATE_GROUP_BY_NAMES.indexOf(value);
}

/** Matches `LogQlQueryRequest.cs`'s `LogQlResultKind` member order. Not itself MemoryPack-TS-generated - see `SavedViewPageTypeName`'s comment for why (`LogQlQueryResponse`, its only consumer, is hand-written - it nests `LogEventDto`/`LogAggregateBucket`). */
const LOG_QL_RESULT_KIND_NAMES = ['Count', 'Series', 'Rows', 'Table'] as const;

export type LogQlResultKindName = (typeof LOG_QL_RESULT_KIND_NAMES)[number];

export function logQlResultKindToString(value: number): LogQlResultKindName {
	return LOG_QL_RESULT_KIND_NAMES[value];
}

export function logQlResultKindFromString(value: LogQlResultKindName): number {
	return LOG_QL_RESULT_KIND_NAMES.indexOf(value);
}
