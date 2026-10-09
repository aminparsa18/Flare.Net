using System.Buffers.Binary;
using System.Text;

namespace Flare.Mcp.DotnetSymbols;

/// <summary>
/// Builds <see cref="NativeSymbols"/> from a 64-bit Mach-O debug file (the <c>DWARF/{name}</c> file inside a
/// <c>.dSYM</c> bundle, which is what Native AOT for iOS and macOS produces): function addresses come from
/// the symbol table, source lines from the DWARF 2-4 <c>.debug_line</c> program. Functions defined in the
/// .NET runtime's own sources (<c>/_/src/runtime/</c>) are left out to keep the file small.
/// </summary>
public static class NativeSymbolsBuilder
{
    private const string RuntimeSourcePrefix = "/_/src/runtime/";

    /// <summary><paramref name="path"/> is a <c>.dSYM</c> bundle directory or the DWARF file inside it.</summary>
    public static (byte[]? Json, string? Uuid, string? Error) Build(string path)
    {
        if (Directory.Exists(path))
        {
            var dwarf = Path.Combine(path, "Contents", "Resources", "DWARF");
            var file = Directory.Exists(dwarf) ? Directory.EnumerateFiles(dwarf).FirstOrDefault() : null;
            if (file is null)
            {
                return (null, null, "no Contents/Resources/DWARF file in the .dSYM bundle");
            }

            path = file;
        }

        try
        {
            return Build(File.ReadAllBytes(path));
        }
        catch (Exception ex) when (ex is FormatException or IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            return (null, null, "unreadable debug file: " + ex.Message);
        }
    }

    internal static (byte[]? Json, string? Uuid, string? Error) Build(byte[] image)
    {
        var macho = MachO.Read(image);
        if (macho is null)
        {
            return (null, null, "not a 64-bit little-endian Mach-O file");
        }

        if (macho.DebugLine.Length == 0 || macho.Symbols.Count == 0)
        {
            return (null, macho.Uuid, "no DWARF line table or symbols (is this the .dSYM's DWARF file, not the stripped binary?)");
        }

        var rows = DwarfLines.Decode(macho.DebugLine, out var error);
        if (error is not null)
        {
            return (null, macho.Uuid, error);
        }

        // Function ends are the next symbol of any kind, so a function never swallows following data.
        var addresses = macho.Symbols.Select(s => s.Address).Distinct().Order().ToArray();
        var files = new List<string>();
        var fileIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        var functions = new List<(string, (int, int, int)[])>();
        foreach (var symbol in macho.Symbols.Where(s => IsFunctionName(s.Name)).DistinctBy(s => s.Address))
        {
            var next = addresses.FirstOrDefault(a => a > symbol.Address);
            var end = next == 0 ? ulong.MaxValue : next;
            var inRange = rows.Where(r => r.Address >= symbol.Address && r.Address < end).ToList();
            if (inRange.Count == 0 || inRange.All(r => r.File.StartsWith(RuntimeSourcePrefix, StringComparison.Ordinal)))
            {
                continue;
            }

            var compact = new List<(int, int, int)>();
            foreach (var r in inRange)
            {
                if (!fileIndex.TryGetValue(r.File, out var index))
                {
                    index = files.Count;
                    fileIndex[r.File] = index;
                    files.Add(r.File);
                }

                var entry = ((int)(r.Address - symbol.Address), r.Line, index);
                if (compact.Count == 0 || compact[^1].Item2 != entry.Line || compact[^1].Item3 != entry.index)
                {
                    compact.Add(entry);
                }
            }

            functions.Add((symbol.Name.TrimStart('_'), [.. compact]));
        }

        return functions.Count == 0
            ? (null, macho.Uuid, "no application functions with line information")
            : (NativeSymbols.Serialize(macho.Uuid, files, functions), macho.Uuid, null);
    }

    // Exception-handling tables, frame records, vtables and runtime helpers share the symbol table with code.
    private static bool IsFunctionName(string name) =>
        name.Length > 2 && name[0] == '_' && name[1] != '_'
        && !name.StartsWith("_lsda", StringComparison.Ordinal) && !name.StartsWith("_fram", StringComparison.Ordinal);

    private sealed record Symbol(string Name, ulong Address);

    private sealed class MachO
    {
        public string Uuid { get; private init; } = "";
        public List<Symbol> Symbols { get; } = [];
        public byte[] DebugLine { get; private set; } = [];

        public static MachO? Read(byte[] data)
        {
            if (data.Length < 32 || BinaryPrimitives.ReadUInt32LittleEndian(data) != 0xfeedfacf)
            {
                return null;
            }

            var commandCount = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(16));
            var uuid = "";
            uint symoff = 0, nsyms = 0, stroff = 0;
            byte[] debugLine = [];
            var at = 32;
            for (var i = 0; i < commandCount; i++)
            {
                var cmd = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at));
                var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at + 4));
                switch (cmd)
                {
                    case 0x1b: // LC_UUID
                        uuid = new Guid(data.AsSpan(at + 8, 16), bigEndian: true).ToString("D");
                        break;
                    case 0x2: // LC_SYMTAB
                        symoff = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at + 8));
                        nsyms = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at + 12));
                        stroff = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at + 16));
                        break;
                    case 0x19: // LC_SEGMENT_64
                        var sections = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at + 64));
                        for (var s = 0; s < sections; s++)
                        {
                            var section = at + 72 + (s * 80);
                            if (Encoding.ASCII.GetString(data, section, 16).TrimEnd('\0') == "__debug_line")
                            {
                                var length = (int)BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(section + 40));
                                var offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(section + 48));
                                debugLine = data.AsSpan(offset, length).ToArray();
                            }
                        }

                        break;
                }

                at += size;
            }

            var macho = new MachO { Uuid = uuid, DebugLine = debugLine };
            for (var i = 0; i < nsyms; i++)
            {
                var entry = (int)symoff + (i * 16);
                var type = data[entry + 4];
                if ((type & 0xe0) != 0 || (type & 0x0e) != 0x0e)
                {
                    continue; // debugging (stab) entries and undefined/absolute symbols
                }

                var nameStart = (int)stroff + (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(entry));
                var nameEnd = Array.IndexOf(data, (byte)0, nameStart);
                macho.Symbols.Add(new Symbol(Encoding.UTF8.GetString(data, nameStart, nameEnd - nameStart), BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(entry + 8))));
            }

            return macho;
        }
    }

    internal readonly record struct LineRow(ulong Address, string File, int Line);

    /// <summary>Runs the DWARF version 2-4 line-number programs of every unit in a <c>.debug_line</c> section.</summary>
    internal static class DwarfLines
    {
        public static List<LineRow> Decode(byte[] section, out string? error)
        {
            var rows = new List<LineRow>();
            error = null;
            var pos = 0;
            while (pos < section.Length)
            {
                var length = BinaryPrimitives.ReadUInt32LittleEndian(section.AsSpan(pos));
                if (length == 0xffffffff)
                {
                    error = "64-bit DWARF is not supported";
                    return rows;
                }

                var end = pos + 4 + (int)length;
                var version = BinaryPrimitives.ReadUInt16LittleEndian(section.AsSpan(pos + 4));
                if (version is < 2 or > 4)
                {
                    error = $"DWARF line table version {version} is not supported (use -gdwarf-4)";
                    return rows;
                }

                RunUnit(section, pos + 6, end, version, rows);
                pos = end;
            }

            return rows;
        }

        private static void RunUnit(byte[] d, int p, int end, int version, List<LineRow> rows)
        {
            var headerLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(p));
            p += 4;
            var programStart = p + headerLength;
            var minInstruction = d[p++];
            if (version >= 4)
            {
                p++; // maximum_operations_per_instruction
            }

            p++; // default_is_stmt
            var lineBase = (sbyte)d[p++];
            var lineRange = d[p++];
            var opcodeBase = d[p++];
            var standardLengths = d.AsSpan(p, opcodeBase - 1).ToArray();
            p += opcodeBase - 1;

            var directories = new List<string> { "" };
            while (d[p] != 0)
            {
                directories.Add(ReadString(d, ref p));
            }

            p++;
            var files = new List<string> { "" };
            while (d[p] != 0)
            {
                var name = ReadString(d, ref p);
                var dir = (int)ReadUleb(d, ref p);
                ReadUleb(d, ref p);
                ReadUleb(d, ref p);
                files.Add(name.StartsWith('/') || dir <= 0 || dir >= directories.Count ? name : directories[dir] + "/" + name);
            }

            p = programStart;
            ulong address = 0;
            long line = 1;
            var file = 1;
            void Emit()
            {
                if (file > 0 && file < files.Count)
                {
                    rows.Add(new LineRow(address, files[file], (int)line));
                }
            }

            while (p < end)
            {
                var op = d[p++];
                if (op >= opcodeBase)
                {
                    var adjusted = op - opcodeBase;
                    address += (ulong)(adjusted / lineRange * minInstruction);
                    line += lineBase + (adjusted % lineRange);
                    Emit();
                    continue;
                }

                switch (op)
                {
                    case 0:
                        var extendedLength = (int)ReadUleb(d, ref p);
                        var next = p + extendedLength;
                        var sub = d[p++];
                        if (sub == 2)
                        {
                            address = BinaryPrimitives.ReadUInt64LittleEndian(d.AsSpan(p));
                        }
                        else if (sub == 1)
                        {
                            address = 0;
                            line = 1;
                            file = 1;
                        }

                        p = next;
                        break;
                    case 1:
                        Emit();
                        break;
                    case 2:
                        address += ReadUleb(d, ref p) * minInstruction;
                        break;
                    case 3:
                        line += ReadSleb(d, ref p);
                        break;
                    case 4:
                        file = (int)ReadUleb(d, ref p);
                        break;
                    case 8:
                        address += (ulong)((255 - opcodeBase) / lineRange * minInstruction);
                        break;
                    case 9:
                        address += BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(p));
                        p += 2;
                        break;
                    default:
                        for (var a = 0; a < standardLengths[op - 1]; a++)
                        {
                            ReadUleb(d, ref p);
                        }

                        break;
                }
            }
        }

        private static string ReadString(byte[] d, ref int p)
        {
            var end = Array.IndexOf(d, (byte)0, p);
            var s = Encoding.UTF8.GetString(d, p, end - p);
            p = end + 1;
            return s;
        }

        private static ulong ReadUleb(byte[] d, ref int p)
        {
            ulong result = 0;
            var shift = 0;
            byte b;
            do
            {
                b = d[p++];
                result |= (ulong)(b & 0x7f) << shift;
                shift += 7;
            }
            while ((b & 0x80) != 0);
            return result;
        }

        private static long ReadSleb(byte[] d, ref int p)
        {
            long result = 0;
            var shift = 0;
            byte b;
            do
            {
                b = d[p++];
                result |= (long)(b & 0x7f) << shift;
                shift += 7;
            }
            while ((b & 0x80) != 0);
            if (shift < 64 && (b & 0x40) != 0)
            {
                result |= -1L << shift;
            }

            return result;
        }
    }
}
