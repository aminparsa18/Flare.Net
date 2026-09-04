// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/IndexingModels.cs`'s `ClusterStatusResponse`
// field-for-field, in declared order. Can't carry `[GenerateTypeScript]` itself: its `Nodes`
// member is an `IReadOnlyList<ClusterNodeInfo>` - see `PipelineServiceBreakdown.ts`'s header
// comment for why `IReadOnlyList<T>` alone blocks `[GenerateTypeScript]` (confirmed by
// actually attaching the attribute here and hitting `MEMPACK031` before switching to
// hand-writing it). `ClusterNodeInfo` itself has no such member, so it's a real generated
// class, reused here directly.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { ClusterNodeInfo } from '$lib/generated/memorypack/ClusterNodeInfo.js';

export class ClusterStatusResponse {
	clusterModeEnabled: boolean;
	sharedPatternStoreEnabled: boolean;
	replicationInfoAvailable: boolean;
	nodes: (ClusterNodeInfo | null)[] | null;

	constructor() {
		this.clusterModeEnabled = false;
		this.sharedPatternStoreEnabled = false;
		this.replicationInfoAvailable = false;
		this.nodes = null;
	}

	static serialize(value: ClusterStatusResponse | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: ClusterStatusResponse | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(4);
		writer.writeBoolean(value.clusterModeEnabled);
		writer.writeBoolean(value.sharedPatternStoreEnabled);
		writer.writeBoolean(value.replicationInfoAvailable);
		writer.writeArray(value.nodes, (writer, x) => ClusterNodeInfo.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): ClusterStatusResponse | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): ClusterStatusResponse | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new ClusterStatusResponse();
		if (count == 4) {
			value.clusterModeEnabled = reader.readBoolean();
			value.sharedPatternStoreEnabled = reader.readBoolean();
			value.replicationInfoAvailable = reader.readBoolean();
			value.nodes = reader.readArray((reader) => ClusterNodeInfo.deserializeCore(reader));
		} else if (count > 4) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.clusterModeEnabled = reader.readBoolean();
			if (count == 1) return value;
			value.sharedPatternStoreEnabled = reader.readBoolean();
			if (count == 2) return value;
			value.replicationInfoAvailable = reader.readBoolean();
			if (count == 3) return value;
			value.nodes = reader.readArray((reader) => ClusterNodeInfo.deserializeCore(reader));
			if (count == 4) return value;
		}
		return value;
	}
}
