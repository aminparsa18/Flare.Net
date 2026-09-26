// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/MessagingModels.cs`'s
// `MessagingDestinationDetailResponse`, in declared order. Can't carry `[GenerateTypeScript]`
// itself: its list members are `IReadOnlyList<T>` (see ADR-0016). The row types are generated.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { MessagingServiceStats } from '$lib/generated/memorypack/MessagingServiceStats.js';
import { MessagingPartitionStats } from '$lib/generated/memorypack/MessagingPartitionStats.js';
import { MessagingConsumerLag } from '$lib/generated/memorypack/MessagingConsumerLag.js';
import { MessagingQueueDepth } from '$lib/generated/memorypack/MessagingQueueDepth.js';

export class MessagingDestinationDetailResponse {
	system: string | null;
	destination: string | null;
	windowMinutes: number;
	producers: (MessagingServiceStats | null)[] | null;
	consumers: (MessagingServiceStats | null)[] | null;
	partitions: (MessagingPartitionStats | null)[] | null;
	consumerLag: (MessagingConsumerLag | null)[] | null;
	queueDepth: (MessagingQueueDepth | null)[] | null;

	constructor() {
		this.system = null;
		this.destination = null;
		this.windowMinutes = 0;
		this.producers = null;
		this.consumers = null;
		this.partitions = null;
		this.consumerLag = null;
		this.queueDepth = null;
	}

	static serialize(value: MessagingDestinationDetailResponse | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: MessagingDestinationDetailResponse | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(8);
		writer.writeString(value.system);
		writer.writeString(value.destination);
		writer.writeInt32(value.windowMinutes);
		writer.writeArray(value.producers, (writer, x) => MessagingServiceStats.serializeCore(writer, x));
		writer.writeArray(value.consumers, (writer, x) => MessagingServiceStats.serializeCore(writer, x));
		writer.writeArray(value.partitions, (writer, x) => MessagingPartitionStats.serializeCore(writer, x));
		writer.writeArray(value.consumerLag, (writer, x) => MessagingConsumerLag.serializeCore(writer, x));
		writer.writeArray(value.queueDepth, (writer, x) => MessagingQueueDepth.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): MessagingDestinationDetailResponse | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): MessagingDestinationDetailResponse | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new MessagingDestinationDetailResponse();
		if (count > 8) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		}
		if (count == 0) return value;
		value.system = reader.readString();
		if (count == 1) return value;
		value.destination = reader.readString();
		if (count == 2) return value;
		value.windowMinutes = reader.readInt32();
		if (count == 3) return value;
		value.producers = reader.readArray((reader) => MessagingServiceStats.deserializeCore(reader));
		if (count == 4) return value;
		value.consumers = reader.readArray((reader) => MessagingServiceStats.deserializeCore(reader));
		if (count == 5) return value;
		value.partitions = reader.readArray((reader) => MessagingPartitionStats.deserializeCore(reader));
		if (count == 6) return value;
		value.consumerLag = reader.readArray((reader) => MessagingConsumerLag.deserializeCore(reader));
		if (count == 7) return value;
		value.queueDepth = reader.readArray((reader) => MessagingQueueDepth.deserializeCore(reader));
		return value;
	}
}
