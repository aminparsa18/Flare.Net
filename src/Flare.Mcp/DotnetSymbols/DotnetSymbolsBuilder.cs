using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;

namespace Flare.Mcp.DotnetSymbols;

/// <summary>Builds the <see cref="DotnetSymbols"/> JSON for one assembly from the dll and its portable PDB (sibling file or embedded).</summary>
public static class DotnetSymbolsBuilder
{
    /// <summary>Returns the symbols JSON and the assembly's MVID, or an error message when the pair can't be used.</summary>
    public static (byte[]? Json, Guid Mvid, string? Error) Build(string dllPath)
    {
        using var peStream = File.OpenRead(dllPath);
        using var pe = new PEReader(peStream);
        if (!pe.HasMetadata)
        {
            return (null, default, "not a .NET assembly");
        }

        var md = pe.GetMetadataReader();
        var mvid = md.GetGuid(md.GetModuleDefinition().Mvid);

        MetadataReaderProvider? provider = null;
        try
        {
            var entries = pe.ReadDebugDirectory();
            var embedded = entries.FirstOrDefault(e => e.Type == DebugDirectoryEntryType.EmbeddedPortablePdb);
            if (embedded.Type == DebugDirectoryEntryType.EmbeddedPortablePdb)
            {
                provider = pe.ReadEmbeddedPortablePdbDebugDirectoryData(embedded);
            }
            else
            {
                var pdbPath = Path.ChangeExtension(dllPath, ".pdb");
                if (!File.Exists(pdbPath))
                {
                    return (null, mvid, "no embedded PDB and no sibling .pdb");
                }

                provider = MetadataReaderProvider.FromPortablePdbStream(new MemoryStream(File.ReadAllBytes(pdbPath)));
            }

            var pdb = provider.GetMetadataReader();
            var codeView = entries.FirstOrDefault(e => e.Type == DebugDirectoryEntryType.CodeView);
            if (codeView.Type == DebugDirectoryEntryType.CodeView
                && pe.ReadCodeViewDebugDirectoryData(codeView).Guid != new Guid(pdb.DebugMetadataHeader!.Id.AsSpan(0, 16)))
            {
                return (null, mvid, "the PDB does not belong to this build of the assembly");
            }

            var documents = new List<string>();
            var documentIndex = new Dictionary<DocumentHandle, int>();
            var methods = new List<(string, string[], (int, int, int)[])>();
            foreach (var handle in md.MethodDefinitions)
            {
                var debug = pdb.GetMethodDebugInformation(handle.ToDebugInformationHandle());
                if (debug.SequencePointsBlob.IsNil)
                {
                    continue;
                }

                var points = new List<(int, int, int)>();
                foreach (var sp in debug.GetSequencePoints())
                {
                    if (sp.IsHidden)
                    {
                        continue;
                    }

                    var doc = sp.Document.IsNil ? debug.Document : sp.Document;
                    if (!documentIndex.TryGetValue(doc, out var index))
                    {
                        index = documents.Count;
                        documentIndex[doc] = index;
                        documents.Add(DocumentName(pdb, pdb.GetDocument(doc).Name));
                    }

                    points.Add((sp.Offset, sp.StartLine, index));
                }

                if (points.Count == 0)
                {
                    continue;
                }

                var def = md.GetMethodDefinition(handle);
                var parameters = def.GetParameters()
                    .Select(md.GetParameter)
                    .Where(p => p.SequenceNumber > 0)
                    .OrderBy(p => p.SequenceNumber)
                    .Select(p => md.GetString(p.Name))
                    .ToArray();
                methods.Add(($"{TypeName(md, def.GetDeclaringType())}.{md.GetString(def.Name)}", parameters, [.. points]));
            }

            return (DotnetSymbols.Serialize(mvid, documents, methods), mvid, null);
        }
        catch (BadImageFormatException)
        {
            return (null, mvid, "the PDB is not a portable PDB (set <DebugType>portable</DebugType>)");
        }
        finally
        {
            provider?.Dispose();
        }
    }

    private static string TypeName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var type = reader.GetTypeDefinition(handle);
        var name = reader.GetString(type.Name);
        var declaring = type.GetDeclaringType();
        if (!declaring.IsNil)
        {
            return TypeName(reader, declaring) + "+" + name;
        }

        var ns = reader.GetString(type.Namespace);
        return ns.Length == 0 ? name : ns + "." + name;
    }

    private static string DocumentName(MetadataReader reader, DocumentNameBlobHandle handle)
    {
        var blob = reader.GetBlobReader(handle);
        var separator = (char)blob.ReadByte();
        var builder = new StringBuilder();
        var first = true;
        while (blob.RemainingBytes > 0)
        {
            if (!first && separator != 0)
            {
                builder.Append(separator);
            }

            first = false;
            var part = blob.ReadBlobHandle();
            if (!part.IsNil)
            {
                var partReader = reader.GetBlobReader(part);
                builder.Append(Encoding.UTF8.GetString(partReader.ReadBytes(partReader.Length)));
            }
        }

        return builder.ToString();
    }
}
