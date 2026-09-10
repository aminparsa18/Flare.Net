// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/ServiceDependencyModels.cs`'s
// `ServiceDependencyGraphResponse` field-for-field, in declared order. Can't carry
// `[GenerateTypeScript]` itself: it nests `IReadOnlyList<ServiceDependencyNode>` and
// `IReadOnlyList<ServiceDependencyEdge>`, which blocks the generator the same way
// `ServiceOverviewResponse`'s own list member does (see that file's own header comment).
// `ServiceDependencyEdge` has no such member, so it's a real generated class, reused here
// directly; `ServiceDependencyNode` is itself hand-written (see its own header comment).

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { ServiceDependencyNode } from './ServiceDependencyNode.js';
import { ServiceDependencyEdge } from '$lib/generated/memorypack/ServiceDependencyEdge.js';

export class ServiceDependencyGraphResponse {
	windowMinutes: number;
	nodes: (ServiceDependencyNode | null)[] | null;
	edges: (ServiceDependencyEdge | null)[] | null;

	constructor() {
		this.windowMinutes = 0;
		this.nodes = null;
		this.edges = null;
	}

	static serialize(value: ServiceDependencyGraphResponse | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: ServiceDependencyGraphResponse | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(3);
		writer.writeInt32(value.windowMinutes);
		writer.writeArray(value.nodes, (writer, x) => ServiceDependencyNode.serializeCore(writer, x));
		writer.writeArray(value.edges, (writer, x) => ServiceDependencyEdge.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): ServiceDependencyGraphResponse | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): ServiceDependencyGraphResponse | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new ServiceDependencyGraphResponse();
		if (count == 3) {
			value.windowMinutes = reader.readInt32();
			value.nodes = reader.readArray((reader) => ServiceDependencyNode.deserializeCore(reader));
			value.edges = reader.readArray((reader) => ServiceDependencyEdge.deserializeCore(reader));
		} else if (count > 3) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.windowMinutes = reader.readInt32();
			if (count == 1) return value;
			value.nodes = reader.readArray((reader) => ServiceDependencyNode.deserializeCore(reader));
			if (count == 2) return value;
			value.edges = reader.readArray((reader) => ServiceDependencyEdge.deserializeCore(reader));
			if (count == 3) return value;
		}
		return value;
	}
}
