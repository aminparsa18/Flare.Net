# ADR-0171: Resolving overloaded Native AOT frames from the managed metadata

Status: Accepted

Date: 2026-10-09

## Context

[ADR-0168](0168-dotnet-symbolication.md) resolves Native AOT frames (`at Ns.Type.Method(String) + 0x48`)
from the `.dSYM`, but left overloads unresolved: the compiler names them `Add`, `Add_0`, `Add_1`, and the
`.dSYM` does not say which managed method each is. Supersedes the "Overloads are not resolved" paragraph and the
"Resolving overloaded native frames" item of that ADR's "Not decided here".

## What the numbering is

Measured on a `PublishAot` osx-x64 sample with overloads of every shape (primitives, `List<string>`, `int[]`,
`int[,]`, `ref int`, a generic method, generic and nested types, constructors, a trimmed-away overload):

- The compiler names **all methods of a type in metadata (`MethodDef`) order**, not in the order they are compiled.
  Each name is sanitized (every character outside `[A-Za-z0-9]` becomes `_`; `.ctor` is `_ctor`), and a name
  already taken by an earlier method gets the first free `_0`, `_1`, ... suffix. A method really called `Add_0`
  takes part, so `Add, Add, Add_0` is `Add, Add_0, Add_0_0`.
- It is the **original** metadata that counts. ILLink removes an unused `Run(int)` but the survivors are still
  `Run` and `Run_1`, so the dll to read is the one the compiler produced (`obj/`), not a linked copy.
- A generic method instance and a generic type instance carry their arguments in `<...>` in the middle or at the
  end of the symbol (`Gen_1<Int32>__Foo_0`, `Foo_2<Int64>`); the number belongs to the definition, so every
  instantiation of one method has the same number.
- A frame prints parameter types from the definition: the simple type name (`Item`, no namespace), a nested type as
  `Outer.Inner`, a generic type with its arity only (``List`1``, ``Nullable`1``), then `[]`, `[,]`, `*` or `&`
  (`Int32[][]`, `String&`); a generic parameter by name (`T`, `U`).

## Decision

- **`flare sourcemaps upload-native <DSYM> --managed <DLL|DIR>`** (repeatable; existing usage is unchanged and
  `--managed` is optional). The CLI reads each dll and its portable PDB, reproduces the compiler's naming per type
  (`ManagedOverloads.AssignNames`), and prints each method's parameters as a frame does. Assemblies without a usable
  PDB are skipped with one summary line.
- **The tie is recorded in the native file**, not in a new bundle: a function matched to its managed method gets
  `b` (the frame-form mangled name, `My_Shop_Cart__Add`) and `p` (the printed parameter list, `String, Int32`) next
  to its existing fields. `version` stays 1: older readers ignore the two fields.
- **A match needs the lines to agree.** A native function is tied to a managed method only if the symbol name
  equals the derived one *and* at least one of its source rows falls inside that method's sequence-point lines in a
  document of the same file name. A dll from a different build therefore leaves the function untied (with a count in
  the CLI output) instead of naming the wrong overload. A symbol two assemblies disagree on is dropped.
- **Lookup by signature.** A function with `b` is indexed under its exact base name instead of the `_N` guess. The
  server passes the frame's printed arguments; among the candidates of the base name, those whose `p` equals them
  (ignoring spaces) are used. If none does and every candidate has a `p`, the frame stays unresolved. If some
  candidates have no `p` (a file uploaded without `--managed`), the previous rule applies: the frame resolves only
  when all candidates agree on the line. Two overloads that print alike (same simple type names in different
  namespaces) disagree on the line and so also stay unresolved.
- **Generic-type instantiations in a symbol are stripped** (`Gen_1<Int32>__Foo_0` is `Gen_1__Foo_0`). ADR-0168
  said instantiations were accepted, but only a trailing `<...>` was removed, so methods of generic types never
  matched; this fixes it.

## Consequences

Overloaded methods, constructors, generic types and generic methods resolve to the exact line when the build
uploads `--managed`. Verified on the sample: 35 frames from distinct overloads (all of the shapes above) resolved to
the throwing line through the CLI upload and `StackTraceSymbolicator`, and none resolved without `--managed`. The
CI step must pass the same build's dll and PDB; the iOS `obj/` layout (`obj/Release/net10.0-ios/ios-arm64`) is
assumed from the macOS sample and is not verified on a device. Names with non-ASCII characters, explicit interface
implementations (whose frame prints a dotted name) and function-pointer parameters are not matched and stay
unresolved. Symbol-naming is the compiler's internal behavior, not a contract: a future ILC that changes it makes
the line check fail and the functions fall back to unresolved, never to a wrong line.

## Not decided here

ELF and PDB symbols for Linux and Windows AOT, as in ADR-0168.
