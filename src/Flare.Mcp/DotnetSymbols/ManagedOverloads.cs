using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Flare.Mcp.DotnetSymbols;

/// <summary>
/// One managed method as the Native AOT compiler names it: <paramref name="Symbol"/> is its linkage name without the
/// leading underscore (<c>aot_My_Shop_Cart__Add_1</c>), <paramref name="Base"/> the same without the overload number
/// and assembly prefix (<c>My_Shop_Cart__Add</c>, what a stack frame mangles to), <paramref name="Parameters"/> the
/// parameter types as a frame prints them (<c>String, Int32</c>, null when one can't be printed) and
/// <paramref name="Spans"/> the source lines its sequence points cover, per document file name.
/// </summary>
internal sealed record ManagedMethod(string Symbol, string Base, string? Parameters, IReadOnlyList<ManagedMethod.Span> Spans)
{
    internal readonly record struct Span(string Document, int FirstLine, int LastLine);
}

/// <summary>
/// Works out, from an assembly's metadata, which overload each numbered Native AOT symbol is (ADR-0171). The
/// compiler names every method of a type in metadata order, sanitized, and gives a name that is already taken the
/// first free <c>_0</c>, <c>_1</c>, ... suffix, so <c>Add</c>, <c>Add</c>, <c>Add</c> become <c>Add</c>, <c>Add_0</c>,
/// <c>Add_1</c>. The order does not depend on what gets compiled or trimmed, so it is reproducible from the dll.
/// </summary>
internal static class ManagedOverloads
{
    /// <summary>The methods of one assembly that have sequence points; null with a reason when the dll or its PDB can't be used.</summary>
    public static (List<ManagedMethod>? Methods, string? Error) Read(string dllPath)
    {
        using var stream = File.OpenRead(dllPath);
        using var pe = new PEReader(stream);
        if (!pe.HasMetadata)
        {
            return (null, "not a .NET assembly");
        }

        var provider = DotnetSymbolsBuilder.OpenPdb(pe, dllPath, out var error);
        if (provider is null)
        {
            return (null, error);
        }

        using (provider)
        {
            return (Read(pe.GetMetadataReader(), provider.GetMetadataReader()), null);
        }
    }

    internal static List<ManagedMethod> Read(MetadataReader md, MetadataReader pdb)
    {
        var assembly = NativeSymbols.Sanitize(md.GetString(md.GetAssemblyDefinition().Name));
        var result = new List<ManagedMethod>();
        foreach (var typeHandle in md.TypeDefinitions)
        {
            var type = md.GetTypeDefinition(typeHandle);
            var handles = type.GetMethods().ToList();
            if (handles.Count == 0)
            {
                continue;
            }

            var typeName = NativeSymbols.Sanitize(DotnetSymbolsBuilder.TypeName(md, typeHandle));
            var names = AssignNames(handles.Select(h => md.GetString(md.GetMethodDefinition(h).Name)));
            var provider = new FrameTypeProvider(md);
            for (var i = 0; i < handles.Count; i++)
            {
                var def = md.GetMethodDefinition(handles[i]);
                var spans = Spans(pdb, handles[i]);
                if (spans.Count == 0)
                {
                    continue; // without lines there is nothing to check a native function against
                }

                var baseName = typeName + "__" + NativeSymbols.Sanitize(md.GetString(def.Name));
                result.Add(new ManagedMethod($"{assembly}_{typeName}__{names[i]}", baseName, Parameters(md, provider, typeHandle, def), spans));
            }
        }

        return result;
    }

    /// <summary>
    /// The compiler's names for the methods of one type, in metadata order: each name is sanitized, and one that
    /// is already taken gets the first free <c>_0</c>, <c>_1</c>, ... suffix (so a real method called
    /// <c>Add_0</c> takes part in the numbering too).
    /// </summary>
    internal static string[] AssignNames(IEnumerable<string> methodNames)
    {
        var taken = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var raw in methodNames)
        {
            var name = NativeSymbols.Sanitize(raw);
            if (taken.Contains(name))
            {
                var i = 0;
                while (taken.Contains($"{name}_{i}"))
                {
                    i++;
                }

                name = $"{name}_{i}";
            }

            taken.Add(name);
            result.Add(name);
        }

        return [.. result];
    }

    private static List<ManagedMethod.Span> Spans(MetadataReader pdb, MethodDefinitionHandle handle)
    {
        var debug = pdb.GetMethodDebugInformation(handle.ToDebugInformationHandle());
        var spans = new Dictionary<string, (int First, int Last)>(StringComparer.Ordinal);
        if (debug.SequencePointsBlob.IsNil)
        {
            return [];
        }

        foreach (var point in debug.GetSequencePoints())
        {
            if (point.IsHidden)
            {
                continue;
            }

            var document = point.Document.IsNil ? debug.Document : point.Document;
            var name = Path.GetFileName(DotnetSymbolsBuilder.DocumentName(pdb, pdb.GetDocument(document).Name).Replace('\\', '/'));
            spans[name] = spans.TryGetValue(name, out var span)
                ? (Math.Min(span.First, point.StartLine), Math.Max(span.Last, point.EndLine))
                : (point.StartLine, point.EndLine);
        }

        return [.. spans.Select(s => new ManagedMethod.Span(s.Key, s.Value.First, s.Value.Last))];
    }

    private static string? Parameters(MetadataReader md, FrameTypeProvider provider, TypeDefinitionHandle owner, MethodDefinition method)
    {
        var signature = method.DecodeSignature(provider, new GenericContext(md, owner, method));
        return signature.ParameterTypes.Any(p => p is null) ? null : string.Join(", ", signature.ParameterTypes);
    }

    private sealed record GenericContext(MetadataReader Reader, TypeDefinitionHandle Owner, MethodDefinition Method);

    /// <summary>
    /// Prints a signature the way a Native AOT stack frame does: the simple type name (nested types as
    /// <c>Outer.Inner</c>, generic types with their arity only, <c>List`1</c>) followed by <c>[]</c>, <c>[,]</c>,
    /// <c>*</c> and <c>&amp;</c>; a generic parameter by its name. Null for what has no known printed form.
    /// </summary>
    private sealed class FrameTypeProvider(MetadataReader reader) : ISignatureTypeProvider<string?, GenericContext>
    {
        public string? GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();

        public string? GetTypeFromDefinition(MetadataReader md, TypeDefinitionHandle handle, byte rawTypeKind)
        {
            var type = md.GetTypeDefinition(handle);
            var name = md.GetString(type.Name);
            var declaring = type.GetDeclaringType();
            return declaring.IsNil ? name : GetTypeFromDefinition(md, declaring, rawTypeKind) + "." + name;
        }

        public string? GetTypeFromReference(MetadataReader md, TypeReferenceHandle handle, byte rawTypeKind)
        {
            var type = md.GetTypeReference(handle);
            var name = md.GetString(type.Name);
            return type.ResolutionScope.Kind == HandleKind.TypeReference
                ? GetTypeFromReference(md, (TypeReferenceHandle)type.ResolutionScope, rawTypeKind) + "." + name
                : name;
        }

        public string? GetTypeFromSpecification(MetadataReader md, GenericContext context, TypeSpecificationHandle handle, byte rawTypeKind)
        {
            var blob = md.GetBlobReader(md.GetTypeSpecification(handle).Signature);
            return new SignatureDecoder<string?, GenericContext>(this, md, context).DecodeType(ref blob);
        }

        public string? GetSZArrayType(string? elementType) => elementType is null ? null : elementType + "[]";

        public string? GetArrayType(string? elementType, ArrayShape shape) =>
            elementType is null ? null : elementType + "[" + new string(',', shape.Rank - 1) + "]";

        public string? GetByReferenceType(string? elementType) => elementType is null ? null : elementType + "&";

        public string? GetPointerType(string? elementType) => elementType is null ? null : elementType + "*";

        public string? GetPinnedType(string? elementType) => elementType;

        public string? GetModifiedType(string? modifier, string? unmodifiedType, bool isRequired) => unmodifiedType;

        public string? GetGenericInstantiation(string? genericType, ImmutableArray<string?> typeArguments) => genericType;

        public string? GetFunctionPointerType(MethodSignature<string?> signature) => null;

        public string? GetGenericTypeParameter(GenericContext context, int index)
        {
            var parameters = reader.GetTypeDefinition(context.Owner).GetGenericParameters();
            return index < parameters.Count ? reader.GetString(reader.GetGenericParameter(parameters[index]).Name) : null;
        }

        public string? GetGenericMethodParameter(GenericContext context, int index)
        {
            var parameters = context.Method.GetGenericParameters();
            return index < parameters.Count ? reader.GetString(reader.GetGenericParameter(parameters[index]).Name) : null;
        }
    }
}
