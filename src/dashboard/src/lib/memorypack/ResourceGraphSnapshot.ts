// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/ResourceGraphDto.cs`'s `ResourceGraphSnapshot`
// field-for-field, in declared order. Can't carry `[GenerateTypeScript]` itself: it has its
// own `DateTimeOffset? UpdatedAt` and nests `ResourceNodeDto`/`ProducerServiceDto` (both
// hand-written - see their own header comments). `ResourceEdgeDto` has neither problem, so
// it's a real generated class, reused here directly.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { ResourceEdgeDto } from '$lib/generated/memorypack/ResourceEdgeDto.js';
import { readNullableDateTimeOffset, writeNullableDateTimeOffset } from '$lib/memorypack/date-time-offset';
import { ResourceNodeDto } from '$lib/memorypack/ResourceNodeDto';
import { ProducerServiceDto } from '$lib/memorypack/ProducerServiceDto';

export class ResourceGraphSnapshot {
	available: boolean;
	unavailableReason: string | null;
	nodes: (ResourceNodeDto | null)[] | null;
	edges: (ResourceEdgeDto | null)[] | null;
	producers: (ProducerServiceDto | null)[] | null;
	updatedAt: Date | null;
	provider: string | null;

	constructor() {
		this.available = false;
		this.unavailableReason = null;
		this.nodes = null;
		this.edges = null;
		this.producers = null;
		this.updatedAt = null;
		this.provider = null;
	}

	static serialize(value: ResourceGraphSnapshot | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: ResourceGraphSnapshot | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(7);
		writer.writeBoolean(value.available);
		writer.writeString(value.unavailableReason);
		writer.writeArray(value.nodes, (writer, x) => ResourceNodeDto.serializeCore(writer, x));
		writer.writeArray(value.edges, (writer, x) => ResourceEdgeDto.serializeCore(writer, x));
		writer.writeArray(value.producers, (writer, x) => ProducerServiceDto.serializeCore(writer, x));
		writeNullableDateTimeOffset(writer, value.updatedAt);
		writer.writeString(value.provider);
	}

	static deserialize(buffer: ArrayBuffer): ResourceGraphSnapshot | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): ResourceGraphSnapshot | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new ResourceGraphSnapshot();
		if (count == 7) {
			value.available = reader.readBoolean();
			value.unavailableReason = reader.readString();
			value.nodes = reader.readArray((reader) => ResourceNodeDto.deserializeCore(reader));
			value.edges = reader.readArray((reader) => ResourceEdgeDto.deserializeCore(reader));
			value.producers = reader.readArray((reader) => ProducerServiceDto.deserializeCore(reader));
			value.updatedAt = readNullableDateTimeOffset(reader);
			value.provider = reader.readString();
		} else if (count > 7) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.available = reader.readBoolean();
			if (count == 1) return value;
			value.unavailableReason = reader.readString();
			if (count == 2) return value;
			value.nodes = reader.readArray((reader) => ResourceNodeDto.deserializeCore(reader));
			if (count == 3) return value;
			value.edges = reader.readArray((reader) => ResourceEdgeDto.deserializeCore(reader));
			if (count == 4) return value;
			value.producers = reader.readArray((reader) => ProducerServiceDto.deserializeCore(reader));
			if (count == 5) return value;
			value.updatedAt = readNullableDateTimeOffset(reader);
			if (count == 6) return value;
			value.provider = reader.readString();
			if (count == 7) return value;
		}
		return value;
	}
}
