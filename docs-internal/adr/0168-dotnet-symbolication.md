# ADR-0168: Symbolicating trimmed and AOT .NET stack traces

Status: Accepted

Date: 2026-10-09

## Context

A release MAUI build on Android or iOS runs on Mono, usually trimmed and often AOT-compiled, and
ships no PDBs. Its `Exception.ToString()` frames then read
`at Ns.Type.Method (System.String s) [0x0001a] in <8e3f…32 hex>:0`: a method, an **IL offset** and
the assembly **MVID**, but no file or line. [ADR-0152](0152-source-map-upload.md) built the upload
surface and read-time symbolication for browser maps and left mobile for later because the frame
parser and a mapping format did not exist.

## Decision

- **Same table, same API, new bundle kind.** An assembly's symbols are stored in `SourceMaps` under
  the bundle name `{mvid}.dotnet.json` and uploaded through `PUT /api/source-maps`. The upload
  validates the file, and requires its name to equal its own MVID so a frame can find it. Version
  fallback (`service.version`, then the revision) and Admin-only writes are unchanged.
- **A compact JSON instead of the PDB.** A portable PDB has sequence points but no method names; the
  names live in the dll. Storing both would mean shipping assemblies to the server. Instead
  `flare sourcemaps upload-dotnet <dll|dir>` reads the dll's metadata and its portable PDB (a sibling
  `.pdb` or an embedded one) with `System.Reflection.Metadata` and uploads
  `{version, mvid, documents[], methods[{n: "Ns.Outer+Inner.Method", p: [paramNames], s: [[il,line,doc]…]}]}`.
  Hidden sequence points are dropped. The CLI refuses a PDB whose id does not match the dll's
  CodeView entry, since a stale PDB would give confidently wrong lines. The builder and the parser
  are one class pair in `Flare.Mcp` so the CLI and the API share the format.
- **Read-time, like browsers.** `StackTraceSymbolicator` also rewrites Mono frames, after the browser
  pass, in the CoreCLR form `at Ns.Type.Method(args) in /path/File.cs:line 23` that the stack-trace
  viewer already links to a repo. Stored spans are never changed, and the parsed files share the
  128 MB cache.
- **Overloads** are told apart by parameter names (Mono prints them); with no name match a method is
  resolved only if it is the sole one of that name with that arity. Ambiguity leaves the frame as
  printed. The IL offset selects the last non-hidden sequence point at or before it.

## Native AOT

`PublishAot` (iOS, macOS) prints `at Ns.Type.Method(String) + 0x48`: a method and a **native offset from
the function's entry**, with no image id. The publish produces a `.dSYM`, whose DWARF line table maps
addresses to real source lines (checked against a published sample: `Program.cs` line 3 for an offset inside
`Cart.Add`).

- `flare sourcemaps upload-native <dSYM>` reads the Mach-O file itself (`LC_UUID`, the symbol table for
  function addresses, `__debug_line` DWARF 2-4 for lines; no external tools) and uploads, as
  `{uuid}.native.json`, one entry per application function: its linkage name and `(offset, line, file)` rows.
  Functions whose sources are under `/_/src/runtime/` are dropped, which keeps a sample app's file near 70 KB
  instead of several MB.
- The linkage name is stored raw and mangled on the server: every character outside `[A-Za-z0-9]` becomes `_`
  and the type and method are joined with `__` (`My.Shop.Cart.Add` is `aot_My_Shop_Cart__Add`; the leading
  `{assembly}_` cannot be told from a namespace, so lookups match on suffixes). Generic instantiations
  (`Gen<Int32>`) and every instantiation of a generic method are accepted when they all agree on the line.
- Frames carry no UUID, so every native file uploaded for the release is tried and a frame resolves when
  exactly one file knows it. The offset is a return address, so the line is looked up at `offset - 1`.
- **Overloads are not resolved.** The compiler numbers them `Add`, `Add_0`, `Add_1` in an order that is not
  recoverable from the dSYM alone, so such frames stay as printed rather than risk a wrong line.

## Not decided here

Native AOT on Linux or Windows (ELF DWARF, PDB), Mach-O fat files and DWARF 5 line tables (the reader
stops with a message). Android R8/ProGuard `mapping.txt` for Java frames. Resolving overloaded native frames.
Mono frames whose offset is a native rather than IL offset (some fully AOT'd methods) may resolve to a
neighbouring line; this was built against the documented frame format and the real-PDB round trip in the
tests, not against a device.

## Consequences

Symbols are per build: CI must run `upload-dotnet` for each release with the `--release` value the
app reports as `service.version`. Generic methods and types are matched with their type arguments
stripped. Compiler-generated names (`<Method>d__3.MoveNext`, `<>c.<Main>b__0_0`) match as they appear
in metadata. Files are small (sequence points only) next to a JavaScript source map.
