// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/ServiceDependencyModels.cs`'s
// `ServiceDependencyNode` field-for-field, in declared order. Can't carry
// `[GenerateTypeScript]` itself: `topOperations` is an `IReadOnlyList<string>` member,
// which blocks the generator (MEMPACK031 - no list-of-primitive member is a supported
// TypeScript-generation type, the same way `ServiceOverviewResponse`'s list-of-object
// member blocks it there - see that file's own header comment).

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';

export class ServiceDependencyNode {
	service: string | null;
	spanCount: bigint;
	errorCount: bigint;
	totalDurationNano: bigint;
	topOperations: (string | null)[] | null;

	constructor() {
		this.service = null;
		this.spanCount = 0n;
		this.errorCount = 0n;
		this.totalDurationNano = 0n;
		this.topOperations = null;
	}

	static serialize(value: ServiceDependencyNode | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: ServiceDependencyNode | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(5);
		writer.writeString(value.service);
		writer.writeUint64(value.spanCount);
		writer.writeUint64(value.errorCount);
		writer.writeUint64(value.totalDurationNano);
		writer.writeArray(value.topOperations, (writer, x) => writer.writeString(x));
	}

	static deserialize(buffer: ArrayBuffer): ServiceDependencyNode | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): ServiceDependencyNode | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new ServiceDependencyNode();
		if (count == 5) {
			value.service = reader.readString();
			value.spanCount = reader.readUint64();
			value.errorCount = reader.readUint64();
			value.totalDurationNano = reader.readUint64();
			value.topOperations = reader.readArray((reader) => reader.readString());
		} else if (count > 5) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.service = reader.readString();
			if (count == 1) return value;
			value.spanCount = reader.readUint64();
			if (count == 2) return value;
			value.errorCount = reader.readUint64();
			if (count == 3) return value;
			value.totalDurationNano = reader.readUint64();
			if (count == 4) return value;
			value.topOperations = reader.readArray((reader) => reader.readString());
			if (count == 5) return value;
		}
		return value;
	}
}
