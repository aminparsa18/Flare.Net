// Minimal ZIP writer (STORE method - no compression) for `export --include-*`'s incident
// bundle. Hand-rolled rather than adding a dependency: the archive is a handful of text
// entries the user opens right after downloading, so the size win from DEFLATE isn't worth
// a new package. Entries are UTF-8 names/contents; no ZIP64 (a bundle is capped well under 4 GB).

const CRC_TABLE = (() => {
	const table = new Uint32Array(256);
	for (let n = 0; n < 256; n++) {
		let c = n;
		for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
		table[n] = c >>> 0;
	}
	return table;
})();

function crc32(bytes: Uint8Array): number {
	let crc = 0xffffffff;
	for (const b of bytes) crc = CRC_TABLE[(crc ^ b) & 0xff] ^ (crc >>> 8);
	return (crc ^ 0xffffffff) >>> 0;
}

export interface ZipEntry {
	name: string;
	content: string;
}

export function createZip(entries: ZipEntry[], modified = new Date()): Blob {
	const encoder = new TextEncoder();
	const dosTime = (modified.getHours() << 11) | (modified.getMinutes() << 5) | (modified.getSeconds() >> 1);
	const dosDate = ((modified.getFullYear() - 1980) << 9) | ((modified.getMonth() + 1) << 5) | modified.getDate();

	const parts: Uint8Array[] = [];
	const central: Uint8Array[] = [];
	let offset = 0;

	for (const entry of entries) {
		const name = encoder.encode(entry.name);
		const data = encoder.encode(entry.content);
		const crc = crc32(data);

		const local = new DataView(new ArrayBuffer(30));
		local.setUint32(0, 0x04034b50, true);
		local.setUint16(4, 20, true); // version needed
		local.setUint16(6, 0x0800, true); // UTF-8 names
		local.setUint16(8, 0, true); // STORE
		local.setUint16(10, dosTime, true);
		local.setUint16(12, dosDate, true);
		local.setUint32(14, crc, true);
		local.setUint32(18, data.length, true);
		local.setUint32(22, data.length, true);
		local.setUint16(26, name.length, true);
		local.setUint16(28, 0, true);
		parts.push(new Uint8Array(local.buffer), name, data);

		const header = new DataView(new ArrayBuffer(46));
		header.setUint32(0, 0x02014b50, true);
		header.setUint16(4, 20, true); // version made by
		header.setUint16(6, 20, true); // version needed
		header.setUint16(8, 0x0800, true);
		header.setUint16(10, 0, true);
		header.setUint16(12, dosTime, true);
		header.setUint16(14, dosDate, true);
		header.setUint32(16, crc, true);
		header.setUint32(20, data.length, true);
		header.setUint32(24, data.length, true);
		header.setUint16(28, name.length, true);
		header.setUint32(42, offset, true);
		central.push(new Uint8Array(header.buffer), name);

		offset += 30 + name.length + data.length;
	}

	const centralSize = central.reduce((sum, c) => sum + c.length, 0);
	const end = new DataView(new ArrayBuffer(22));
	end.setUint32(0, 0x06054b50, true);
	end.setUint16(8, entries.length, true);
	end.setUint16(10, entries.length, true);
	end.setUint32(12, centralSize, true);
	end.setUint32(16, offset, true);

	return new Blob([...parts, ...central, new Uint8Array(end.buffer)] as BlobPart[], { type: 'application/zip' });
}
