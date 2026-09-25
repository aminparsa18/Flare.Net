// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/MaintenanceWindowModels.cs`'s
// `MaintenanceWindowListResponse` - hand-written because `MaintenanceWindow` is. Decode-only.

import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { MaintenanceWindow } from '$lib/memorypack/MaintenanceWindow';

export class MaintenanceWindowListResponse {
	windows: (MaintenanceWindow | null)[] | null = null;
	activeWindowIds: (string | null)[] | null = null;

	static deserialize(buffer: ArrayBuffer): MaintenanceWindowListResponse | null {
		const reader = new MemoryPackReader(buffer);
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		if (count != 2) {
			throw new Error(`MaintenanceWindowListResponse: unexpected property count ${count}.`);
		}

		const value = new MaintenanceWindowListResponse();
		value.windows = reader.readArray((reader) => MaintenanceWindow.deserializeCore(reader));
		value.activeWindowIds = reader.readArray((reader) => reader.readGuid());
		return value;
	}
}
