// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/LogQlQueryRequest.cs`'s `LogQlQueryResponse`
// field-for-field, in declared order. Can't carry `[GenerateTypeScript]` itself because it
// nests `LogAggregateBucket`/`LogEventDto`, both blocked (`DateTimeOffset` members) - see
// their own header comments. `kind` is a raw MemoryPack numeric ordinal (converted to
// string at `api.ts`'s module boundary via `$lib/memorypack/enums.ts`'s
// `logQlResultKindToString`).

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { LogAggregateBucket } from '$lib/memorypack/LogAggregateBucket';
import { LogEventDto } from '$lib/memorypack/LogEventDto';

export class LogQlQueryResponse {
	kind: number;
	count: number | null;
	buckets: (LogAggregateBucket | null)[] | null;
	events: (LogEventDto | null)[] | null;
	columns: (string | null)[] | null;
	rows: ((string | null)[] | null)[] | null;
	hasMoreRows: boolean;

	constructor() {
		this.kind = 0;
		this.count = null;
		this.buckets = null;
		this.events = null;
		this.columns = null;
		this.rows = null;
		this.hasMoreRows = false;
	}

	static serialize(value: LogQlQueryResponse | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: LogQlQueryResponse | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(7);
		writer.writeInt32(value.kind);
		writer.writeNullableFloat64(value.count);
		writer.writeArray(value.buckets, (writer, x) => LogAggregateBucket.serializeCore(writer, x));
		writer.writeArray(value.events, (writer, x) => LogEventDto.serializeCore(writer, x));
		writer.writeArray(value.columns, (writer, x) => writer.writeString(x));
		writer.writeArray(value.rows, (writer, row) => writer.writeArray(row, (writer, x) => writer.writeString(x)));
		writer.writeBoolean(value.hasMoreRows);
	}

	static deserialize(buffer: ArrayBuffer): LogQlQueryResponse | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): LogQlQueryResponse | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new LogQlQueryResponse();
		if (count == 7) {
			value.kind = reader.readInt32();
			value.count = reader.readNullableFloat64();
			value.buckets = reader.readArray((reader) => LogAggregateBucket.deserializeCore(reader));
			value.events = reader.readArray((reader) => LogEventDto.deserializeCore(reader));
			value.columns = reader.readArray((reader) => reader.readString());
			value.rows = reader.readArray((reader) => reader.readArray((reader) => reader.readString()));
			value.hasMoreRows = reader.readBoolean();
		} else if (count > 7) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.kind = reader.readInt32();
			if (count == 1) return value;
			value.count = reader.readNullableFloat64();
			if (count == 2) return value;
			value.buckets = reader.readArray((reader) => LogAggregateBucket.deserializeCore(reader));
			if (count == 3) return value;
			value.events = reader.readArray((reader) => LogEventDto.deserializeCore(reader));
			if (count == 4) return value;
			value.columns = reader.readArray((reader) => reader.readString());
			if (count == 5) return value;
			value.rows = reader.readArray((reader) => reader.readArray((reader) => reader.readString()));
			if (count == 6) return value;
			value.hasMoreRows = reader.readBoolean();
			if (count == 7) return value;
		}
		return value;
	}
}
