// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/MetricCatalogModels.cs`'s
// `MetricCatalogDetailResponse`, in declared order. Can't carry `[GenerateTypeScript]`
// itself: three `IReadOnlyList<T>` members (see ADR-0016). `MetricCatalogServiceInfo`/
// `MetricCatalogRelatedMetric` are real generated classes; `MetricCatalogAttributeInfo` is
// hand-written alongside this file.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { MetricCatalogServiceInfo } from '$lib/generated/memorypack/MetricCatalogServiceInfo.js';
import { MetricCatalogRelatedMetric } from '$lib/generated/memorypack/MetricCatalogRelatedMetric.js';
import { MetricCatalogAttributeInfo } from '$lib/memorypack/MetricCatalogAttributeInfo';

export class MetricCatalogDetailResponse {
	metricName: string;
	/** Raw MemoryPack ordinal, converted at `metric-catalog-api.ts` - same as `MetricAttributeKeysRequest.type`. */
	type: number;
	unit: string | null;
	description: string | null;
	windowMinutes: number;
	services: (MetricCatalogServiceInfo | null)[] | null;
	attributes: (MetricCatalogAttributeInfo | null)[] | null;
	related: (MetricCatalogRelatedMetric | null)[] | null;

	constructor() {
		this.metricName = '';
		this.type = 0;
		this.unit = null;
		this.description = null;
		this.windowMinutes = 0;
		this.services = null;
		this.attributes = null;
		this.related = null;
	}

	static serialize(value: MetricCatalogDetailResponse | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: MetricCatalogDetailResponse | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(8);
		writer.writeString(value.metricName);
		writer.writeInt32(value.type);
		writer.writeString(value.unit);
		writer.writeString(value.description);
		writer.writeInt32(value.windowMinutes);
		writer.writeArray(value.services, (writer, x) => MetricCatalogServiceInfo.serializeCore(writer, x));
		writer.writeArray(value.attributes, (writer, x) => MetricCatalogAttributeInfo.serializeCore(writer, x));
		writer.writeArray(value.related, (writer, x) => MetricCatalogRelatedMetric.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): MetricCatalogDetailResponse | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): MetricCatalogDetailResponse | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new MetricCatalogDetailResponse();
		if (count == 8) {
			value.metricName = reader.readString() ?? '';
			value.type = reader.readInt32();
			value.unit = reader.readString();
			value.description = reader.readString();
			value.windowMinutes = reader.readInt32();
			value.services = reader.readArray((reader) => MetricCatalogServiceInfo.deserializeCore(reader));
			value.attributes = reader.readArray((reader) => MetricCatalogAttributeInfo.deserializeCore(reader));
			value.related = reader.readArray((reader) => MetricCatalogRelatedMetric.deserializeCore(reader));
		} else if (count > 8) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.metricName = reader.readString() ?? '';
			if (count == 1) return value;
			value.type = reader.readInt32();
			if (count == 2) return value;
			value.unit = reader.readString();
			if (count == 3) return value;
			value.description = reader.readString();
			if (count == 4) return value;
			value.windowMinutes = reader.readInt32();
			if (count == 5) return value;
			value.services = reader.readArray((reader) => MetricCatalogServiceInfo.deserializeCore(reader));
			if (count == 6) return value;
			value.attributes = reader.readArray((reader) => MetricCatalogAttributeInfo.deserializeCore(reader));
			if (count == 7) return value;
			value.related = reader.readArray((reader) => MetricCatalogRelatedMetric.deserializeCore(reader));
		}
		return value;
	}
}
