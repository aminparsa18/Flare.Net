// Client for Flare.Api's /messaging page endpoints (src/Flare.Api/Endpoints/
// MessagingEndpoints.cs): the topic/queue list (`POST /api/messaging/destinations`) and one
// destination's drill-down (`POST /api/messaging/destination-detail`), derived from spans'
// OTel `messaging.*` attributes plus the `kafka.consumer_group.lag` gauge - see
// MessagingQueryBuilder.cs and docs-internal/adr/0056-messaging-queue-monitoring.md.
//
// MemoryPack over the wire, same shape as `hosts-api.ts`: both requests and every row type
// are real generated classes; the two responses carry `IReadOnlyList` members, so those are
// hand-written under `$lib/memorypack/`.

import { API_BASE_URL, apiFetch, memoryPackBody, memoryPackRequestHeaders } from './api';
import { MessagingDestinationsRequest as GeneratedDestinationsRequest } from '$lib/generated/memorypack/MessagingDestinationsRequest.js';
import { MessagingDestinationDetailRequest as GeneratedDetailRequest } from '$lib/generated/memorypack/MessagingDestinationDetailRequest.js';
import type { MessagingServiceStats as GeneratedServiceStats } from '$lib/generated/memorypack/MessagingServiceStats.js';
import { MessagingDestinationsResponse as GeneratedDestinationsResponse } from '$lib/memorypack/MessagingDestinationsResponse';
import { MessagingDestinationDetailResponse as GeneratedDetailResponse } from '$lib/memorypack/MessagingDestinationDetailResponse';

/** One `(system, destination)` row. Latencies are milliseconds and 0 when that side has no spans; `consumerLag` is null when the lag metric isn't collected - never 0. */
export interface MessagingDestination {
	system: string;
	destination: string;
	publishCount: number;
	publishErrorCount: number;
	publishPerSecond: number;
	publishP50Ms: number;
	publishP99Ms: number;
	consumeCount: number;
	consumeErrorCount: number;
	consumePerSecond: number;
	consumeP50Ms: number;
	consumeP99Ms: number;
	producerServiceCount: number;
	consumerServiceCount: number;
	avgMessageBytes: number | null;
	consumerLag: number | null;
}

export interface MessagingDestinationsResponse {
	windowMinutes: number;
	destinations: MessagingDestination[];
	systems: string[];
	services: string[];
}

export interface MessagingServiceStats {
	serviceName: string;
	/** Empty for producers and for systems without consumer groups. */
	consumerGroup: string;
	count: number;
	errorCount: number;
	perSecond: number;
	p50Ms: number;
	p99Ms: number;
	avgMessageBytes: number | null;
}

export interface MessagingPartitionStats {
	partition: string;
	publishCount: number;
	publishPerSecond: number;
	consumeCount: number;
	consumePerSecond: number;
	errorCount: number;
}

export interface MessagingConsumerLag {
	consumerGroup: string;
	partition: string;
	lag: number;
}

export interface MessagingDestinationDetailResponse {
	system: string;
	destination: string;
	windowMinutes: number;
	producers: MessagingServiceStats[];
	consumers: MessagingServiceStats[];
	partitions: MessagingPartitionStats[];
	consumerLag: MessagingConsumerLag[];
}

export interface MessagingFilter {
	/** '' = all services. */
	service?: string;
	/** '' = all systems. */
	system?: string;
}

function strings(values: (string | null)[] | null): string[] {
	return (values ?? []).filter((v): v is string => v != null);
}

function toServiceStats(dto: GeneratedServiceStats): MessagingServiceStats {
	return {
		serviceName: dto.serviceName ?? '',
		consumerGroup: dto.consumerGroup ?? '',
		count: Number(dto.count),
		errorCount: Number(dto.errorCount),
		perSecond: dto.perSecond,
		p50Ms: dto.p50Ms,
		p99Ms: dto.p99Ms,
		avgMessageBytes: dto.avgMessageBytes
	};
}

export async function getMessagingDestinations(
	windowMinutes: number,
	filter: MessagingFilter,
	signal?: AbortSignal
): Promise<MessagingDestinationsResponse> {
	const request = new GeneratedDestinationsRequest();
	request.windowMinutes = windowMinutes;
	request.service = filter.service || null;
	request.system = filter.system || null;

	const res = await apiFetch(`${API_BASE_URL}/api/messaging/destinations`, {
		method: 'POST',
		headers: memoryPackRequestHeaders(),
		body: memoryPackBody(GeneratedDestinationsRequest.serialize(request)),
		signal
	});
	if (!res.ok) {
		throw new Error(`POST /api/messaging/destinations failed: ${res.status} ${res.statusText}`);
	}
	const dto = GeneratedDestinationsResponse.deserialize(await res.arrayBuffer());
	if (dto == null) {
		throw new Error('Empty response body decoding MessagingDestinationsResponse.');
	}
	return {
		windowMinutes: dto.windowMinutes,
		systems: strings(dto.systems),
		services: strings(dto.services),
		destinations: (dto.destinations ?? [])
			.filter((d) => d != null)
			.map((d) => ({
				system: d.system ?? '',
				destination: d.destination ?? '',
				publishCount: Number(d.publishCount),
				publishErrorCount: Number(d.publishErrorCount),
				publishPerSecond: d.publishPerSecond,
				publishP50Ms: d.publishP50Ms,
				publishP99Ms: d.publishP99Ms,
				consumeCount: Number(d.consumeCount),
				consumeErrorCount: Number(d.consumeErrorCount),
				consumePerSecond: d.consumePerSecond,
				consumeP50Ms: d.consumeP50Ms,
				consumeP99Ms: d.consumeP99Ms,
				producerServiceCount: Number(d.producerServiceCount),
				consumerServiceCount: Number(d.consumerServiceCount),
				avgMessageBytes: d.avgMessageBytes,
				consumerLag: d.consumerLag == null ? null : Number(d.consumerLag)
			}))
	};
}

export async function getMessagingDestinationDetail(
	system: string,
	destination: string,
	windowMinutes: number,
	service: string,
	signal?: AbortSignal
): Promise<MessagingDestinationDetailResponse> {
	const request = new GeneratedDetailRequest();
	request.system = system;
	request.destination = destination;
	request.windowMinutes = windowMinutes;
	request.service = service || null;

	const res = await apiFetch(`${API_BASE_URL}/api/messaging/destination-detail`, {
		method: 'POST',
		headers: memoryPackRequestHeaders(),
		body: memoryPackBody(GeneratedDetailRequest.serialize(request)),
		signal
	});
	if (!res.ok) {
		throw new Error(`POST /api/messaging/destination-detail failed: ${res.status} ${res.statusText}`);
	}
	const dto = GeneratedDetailResponse.deserialize(await res.arrayBuffer());
	if (dto == null) {
		throw new Error('Empty response body decoding MessagingDestinationDetailResponse.');
	}
	return {
		system: dto.system ?? system,
		destination: dto.destination ?? destination,
		windowMinutes: dto.windowMinutes,
		producers: (dto.producers ?? []).filter((p) => p != null).map(toServiceStats),
		consumers: (dto.consumers ?? []).filter((c) => c != null).map(toServiceStats),
		partitions: (dto.partitions ?? [])
			.filter((p) => p != null)
			.map((p) => ({
				partition: p.partition ?? '',
				publishCount: Number(p.publishCount),
				publishPerSecond: p.publishPerSecond,
				consumeCount: Number(p.consumeCount),
				consumePerSecond: p.consumePerSecond,
				errorCount: Number(p.errorCount)
			})),
		consumerLag: (dto.consumerLag ?? [])
			.filter((l) => l != null)
			.map((l) => ({ consumerGroup: l.consumerGroup ?? '', partition: l.partition ?? '', lag: Number(l.lag) }))
	};
}
