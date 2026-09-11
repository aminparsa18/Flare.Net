// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/NotificationChannelModels.cs`'s
// `NotificationChannelListResponse`. Can't carry `[GenerateTypeScript]` itself because its
// one member's type, `NotificationChannel`, is hand-written - see
// `NotificationChannel.ts`'s header comment.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { NotificationChannel } from '$lib/memorypack/NotificationChannel';

export class NotificationChannelListResponse {
	channels: (NotificationChannel | null)[] | null;

	constructor() {
		this.channels = null;
	}

	static serialize(value: NotificationChannelListResponse | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: NotificationChannelListResponse | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(1);
		writer.writeArray(value.channels, (writer, x) => NotificationChannel.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): NotificationChannelListResponse | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): NotificationChannelListResponse | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new NotificationChannelListResponse();
		if (count == 1) {
			value.channels = reader.readArray((reader) => NotificationChannel.deserializeCore(reader));
		} else if (count > 1) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.channels = reader.readArray((reader) => NotificationChannel.deserializeCore(reader));
		}
		return value;
	}
}
