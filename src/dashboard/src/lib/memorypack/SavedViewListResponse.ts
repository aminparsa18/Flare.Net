// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/SavedViewModels.cs`'s `SavedViewListResponse`.
// Can't carry `[GenerateTypeScript]` itself because its one member's type, `SavedView`, has
// a `JsonElement` member - see `SavedView.ts`'s header comment.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { SavedView } from '$lib/memorypack/SavedView';

export class SavedViewListResponse {
	views: (SavedView | null)[] | null;

	constructor() {
		this.views = null;
	}

	static serialize(value: SavedViewListResponse | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: SavedViewListResponse | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(1);
		writer.writeArray(value.views, (writer, x) => SavedView.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): SavedViewListResponse | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): SavedViewListResponse | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new SavedViewListResponse();
		if (count == 1) {
			value.views = reader.readArray((reader) => SavedView.deserializeCore(reader));
		} else if (count > 1) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.views = reader.readArray((reader) => SavedView.deserializeCore(reader));
		}
		return value;
	}
}
