# Unity IL2CPP targets

A Unity IL2CPP build converts the game's C# to C++ ahead of time and compiles it into native code in
`GameAssembly.dll`; the class, field and method names live in `global-metadata.dat`. Read this page to recognise such
a build, to see what Cheat Engine 7.7's Mono data collector still offers on it, to use numbers from the user's own
offline dump or find a class without one, and to read objects and static fields. The guided workflow is
[unity_il2cpp_recon](../Workflows/unity-il2cpp-recon.md); Mono builds are in [Mono and .NET](mono-and-dotnet.md).

Scope: single-player or offline games the user owns or may modify ([safety](safety.md)). An encrypted
`global-metadata.dat`, a `GameAssembly.dll` that is packed or has no `il2cpp_` exports, value-protection field types
such as `ObscuredInt`, or an anti-cheat mean the game is deliberately protected: stop and tell the user. Never help get
around that protection.

## Recognise the build

| Check | Meaning |
|---|---|
| `module_list(nameContains="GameAssembly")` finds `GameAssembly.dll` | IL2CPP (`UnityPlayer.dll` is loaded too) |
| `module_list_exports(module="GameAssembly.dll", nameContains="il2cpp_thread_attach")` lists it | collector usable |
| `GameAssembly.dll` has no `il2cpp_` exports | renamed exports: treat the game as protected and stop |
| `mono-2.0-bdwgc.dll` or `mono.dll` instead | a Mono build: see [Mono and .NET](mono-and-dotnet.md) |
| only `UnityPlayer.dll` | the scripting backend is not loaded yet: check again later |

- `module_get(module="GameAssembly.dll")` gives `path`, `size`, `is64Bit` and `pe.timeDateStamp`. Beside the game on
  disk there is `<Game>_Data\il2cpp_data\Metadata\global-metadata.dat` and no `<Game>_Data\Managed\` folder.
- Keep `pe.timeDateStamp` with every offset and RVA you record: a new value means an update, after which they may be
  stale.
- CE's collector recognises IL2CPP by the `il2cpp_thread_attach` export (Mono by `mono_thread_attach`).
- `module_list_exports(module="GameAssembly.dll", nameContains="il2cpp_class_get_static_field_data")` tells in advance
  whether the collector can report static field addresses: CE reads them only through that export.

Three approaches: **offline with a dump** (the workflow's default) only reads the game's memory, with numbers from the
user's own dump; **offline without a dump** also only reads memory and finds a class by its name; the **collector**
injects CE's Mono data collector, needs `Mcp:EnableTargetCodeExecution` and the user's consent, and names classes
live.

## The collector in IL2CPP mode

`mono_attach()` runs CE's own launcher. On an IL2CPP game it:

- injects `MonoDataCollector64.dll` (or the 32-bit build), which calls the game's `il2cpp_` exports and stays loaded
  until the game exits;
- installs CE-wide symbol, structure-name and dissect hooks (`cheat_engine_hooks_installed`);
- may set the table option Uses Mono (`uses_mono_table_option`): CE then injects again by itself when a process is
  opened or a table loaded, so while the table has it, process_attach, process_create, process_open_file and
  table_load need `Mcp:EnableTargetCodeExecution` and report `monoAutoAttach` ([Mono and .NET](mono-and-dotnet.md));
- patches no `mono_error_ok`: CE aims that patch at `mono-2.0-bdwgc.dll`, which an IL2CPP game lacks, so `hostEffects`
  omits `mono_error_ok_patched`;
- starts a background thread that registers every method that has native code as a symbol `Namespace.Class.Method`,
  `Assembly-CSharp.dll` first; CE shows a progress bar "IL2CPP symbol enum: N%" in its main window until it reads
  Done;
- is refused while the target is paused or the debugger is stopped (`invalid_state`), and can raise a CE dialog a
  human must dismiss, such as a collector version mismatch or "mono is not usable in the target proces (yet)" (CE's
  spelling).

`mono_get_status()` then reports `il2Cpp` true and one entry in `domains`. MCP tracks the attachment it made as a
`mono` resource: until `mono_detach()` or `runtime_release_resources()` it blocks a target switch (`busy`). A
collector someone else started (CE's Mono menu, Uses Mono, or the Unity speedhack) serves the read tools too, but
`mono_attach` returns `busy` and `mono_detach` returns `not_found` for it.

| Need | Tool | On IL2CPP |
|---|---|---|
| Images | `mono_list_assemblies` | `name` ends in `.dll`, such as `Assembly-CSharp.dll` |
| Classes | `mono_list_classes`, `mono_find_class` | work; see the note on images without classes below |
| Fields | `mono_list_fields` | work: `name`, `typeName`, `offset`, `isStatic`, `isConst`, `staticAddress` |
| Static data | `mono_get_static_field_address` | needs the static-field export; `not_found` for generic classes |
| Methods | `mono_list_methods`, `mono_find_method` | work: `signature`, `returnType`, `parameterNames`, `isStatic` |
| Object at an address | `mono_get_object` | works when the collector lists the image's classes |
| Native code | `mono_compile_method` (gated) | compiles nothing: returns the existing code pointer |
| Instances | `mono_start_instance_search` | a read-only memory scan for the class pointer |
| Calls | `mono_invoke_method` (gated) | runs game code |
| IL, JIT info, loading assemblies | none | not available |

- `mono_list_classes`, `mono_list_fields` and `mono_list_methods` take an optional `nameContains`: a literal
  substring that ignores ASCII case, matched against `Namespace.Name` for classes and applied before paging; `total`
  counts the matches.
- When the collector reports no classes for an image, `mono_list_classes` lists CE's guess from a scan of the whole
  target for the image pointer, which can include wrong entries. `mono_get_object` never uses that guess: it refuses
  such an image as `unsupported`.
- `mono_compile_method` gives `host_refused` for a method without native code.

A method's native address without the gated call:

- `symbol_find(nameContains="<Class>.<Method>")` searches CE's IL2CPP method symbols as the thread registers them.
  While `symbolsLoaded` is false the list is still growing, so a miss proves nothing until it is true (the bar reads
  Done). Each call copies the symbol lists on CE's main thread and can block CE for a second or more.
- `symbol_resolve(expressions=["<Namespace>.<Class>.<Method>"])` (`<Class>.<Method>` without a namespace) resolves one
  name. Overloads share one name, so either tool may give any overload's address.
- The first pointer of the method handle is exactly what the collector returns for `mono_compile_method`:
  `util_calculate(expression="<methodHandle>")` gives its `hex`, then
  `memory_read(address="<hex>", valueType="pointer")`.

Check the address with `code_disassemble(address="<address>", count=20)`. A 0 pointer means the method has no native
code (an abstract method, for example); CE's symbol thread skips such methods.

## Object layout (64-bit, hex offsets)

| Item | Layout |
|---|---|
| Object header | `+0` class pointer (`Il2CppClass*`, the class handle in hex), `+8` monitor |
| Instance fields | from `+10`; `mono_list_fields` offsets include the header |
| `System.String` | int32 length at `+10`, UTF-16 characters from `+14` |
| Array | length at `+18`, elements from `+20` |
| Struct (value type) | its class's offsets include the `10` header: subtract `10` where it is embedded |
| Class (`Il2CppClass`) | image pointer `+0`, name pointer `+10`, namespace pointer `+18`, static data pointer `+B8` |

CE's own IL2CPP class scan reads the image, name and namespace pointers at those offsets.

- 32-bit builds (`module_get` reports `is64Bit` false) use 4-byte pointers and an 8-byte header: fields from `+8`,
  array length at `+C` and elements from `+10`, class name pointer `+8` and namespace pointer `+C`.
- The static data offset depends on the metadata version, per Il2CppDumper's per-version headers: `B8` for v24.1 to
  v29 (about Unity 2018.3 to 2022), `A0` for v24.0, `98` for v22 (32-bit: `5C`, `50`, `4C`). UNVERIFIED per build:
  confirm it as described under Static fields.
- Handles and the `offset` of `mono_list_fields` and `mono_get_object` are decimal; CE expressions, record `offsets`
  and `structure_create` offsets are hex. `util_calculate(expression="<decimal value>")` gives the `hex` form.

Checks on a candidate object `<obj>`, such as a scan hit minus the field offset, with `<offset>` in hex:

- `memory_read(address="<obj>", valueType="pointer")` equals the class handle in hex;
- `memory_read(address="[[<obj>]+10]", valueType="string")` reads the class name;
- a string field: `memory_read(address="[<obj>+<offset>]+14", valueType="wstring", length=64)`.

With the collector attached, `mono_get_object(address="<address inside the object>")` does this in one call. It walks
down at most `maxBacktrack` bytes (4096 by default) reading memory only, accepts the nearest header whose class the
collector lists in its image (on IL2CPP the header is the class pointer), and returns the start `address`,
`offsetInObject`, `className`, `classHandle` and each field's `offset`, `address` and current `value`. It refuses
generic-instance and array objects (`unsupported`), returns `not_found` when no listed header lies in range, and can
block CE for seconds while it lists a large image's classes. It is ungated, and the collector is never asked about the
address itself.

Map the fields for structure_read ([structures](structures.md)):
`structure_create(name="<Class>", elements=[{offset="<offset>", name="<field>", valueType="int32"}])`.

## Static fields

1. Collector: `mono_list_fields.staticAddress`, or `mono_get_static_field_address(classHandle="<classHandle>")` plus
   the field's offset in hex.
2. On `not_found` (no static-field export), follow the class's static data pointer, with `<klass>` the class handle in
   hex or `[<obj>]`: `memory_read(address="[<klass>+B8]+<offset>", valueType="int32")`. Confirm `B8` first on a class
   with a static singleton of its own class (a field such as `Instance`):
   `memory_read(address="[[<klass>+B8]+<offset>]", valueType="pointer")` then reads that object's class pointer, which
   must equal `<klass>`.
3. Offline, the class pointer sits in a global of `GameAssembly.dll` that Il2CppDumper's `script.json` names
   `<Class>_TypeInfo`. Its `Address` is the RVA in decimal: `util_calculate(expression="<Address>")` gives the hex
   `<TypeInfoRVA>`. The global may not hold the class pointer until the game has used the class (UNVERIFIED): first
   check that `memory_read(address="[[GameAssembly.dll+<TypeInfoRVA>]+10]", valueType="string")` reads the class
   name. A record built on it survives restarts of one build:
   `record_create(records=[{description="<field> (static)", address="GameAssembly.dll+<TypeInfoRVA>",
   variableType=2, offsets=["B8", "<offset>"]}])`.
   Record offsets are hex strings in dereference order, as `dump.cs` prints them without `0x`; with no `value` the
   create writes nothing.

Static data exists only once the class is initialised and holds default values until its static constructor has run;
`isConst` fields have no storage. A static singleton (`Instance`, a manager) is the best root for
[pointers](pointers.md).

## Instances

`mono_start_instance_search(classHandle="<classHandle>", maximumResults=100)` returns a `jobId`. Page it with
`mono_poll_instance_search(jobId="<jobId>", afterSequence=0)` until `job.state` is no longer `running`, then end it with
`runtime_stop_job(jobId="<jobId>")`. The job scans memory for the class pointer on 8-byte boundaries and never runs
game code, but it can block CE for seconds.

- Hits are candidates: the class pointer also sits in `GameAssembly.dll` (the TypeInfo globals), in runtime metadata
  such as the class structure itself, and in dead objects. Every hit holds the class pointer, so the header checks
  above always pass: drop hits inside the module, then compare field values with the game, for example with
  `mono_get_object(address="<hit>", maxBacktrack=0)`.
- Unity's garbage collector does not move objects, but objects die and are replaced, for example on a scene change:
  find them again after a load.
- A static root beats a scan; to find the code that writes a field, see [find_writer](../Workflows/find-writer.md).

## Offline: the user's own dump

MCP runs no dumper. The user may run an external tool such as Il2CppDumper or Cpp2IL on their own copy of
`GameAssembly.dll` and `global-metadata.dat`. Il2CppDumper's `dump.cs` marks methods
`// RVA: 0x... Offset: 0x... VA: 0x...` and fields `// 0x...`.

- Use the RVA, in hex without `0x`: `GameAssembly.dll+<RVA>`. `Offset` is the position in the file on disk, not in
  memory.
- A VA is the image's preferred base plus the RVA. The x64 DLL default base is `0x180000000`, so for a dump made with
  that base `util_calculate(expression="0x<VA> - 0x180000000")` gives the RVA as `hex`. `pe.headerImageBase` does not
  give the preferred base: Windows usually rewrites it to the load address.
- The RVA must be below the module `size`, and `code_disassemble(address="GameAssembly.dll+<RVA>", count=20)` should
  show a function start.
- Dumper errors such as "Metadata file supplied is not valid metadata file" (encrypted metadata) or "This file may be
  protected" mean a protected game: stop.
- RVAs change with every update; names usually do not. Anchor code on a signature ([AOB signatures](aob-signatures.md),
  [make_aob_signature](../Workflows/make-aob-signature.md)): `aob_generate_signature(address="GameAssembly.dll+<RVA>")`
  also works in a `GameAssembly.dll` over 64 MiB, where `aob_generate_signature.generator` reads `managed`.

## Offline without a dump

With neither a dump nor the collector, memory reads can still reach a class by its name through the class layout
above. Each step rests on that layout and is UNVERIFIED per build, so check it as shown.

1. `util_convert_value(value="<Class>", sourceType="string", targetType="bytes")` gives the name's `bytes`. The
   metadata stores each name with a zero byte after it, usually in a mapped view of `global-metadata.dat`, so
   `aob_find(patterns=["00 <bytes> 00"], includeMapped=true, limit=10)` finds it; the name starts one byte after a
   match.
2. `pointer_find_references(target="<match>+1")`: a class holds its name pointer at `+10`, so a holder `<h>` gives the
   candidate `<klass>` from `util_calculate(expression="0x<h> - 0x10")`. It is the class when
   `memory_read(address="[<klass>+18]", valueType="string")` reads its namespace (empty for none) and
   `memory_read(address="[[<klass>]]", valueType="string")` reads its image name, such as `Assembly-CSharp.dll`. No
   holder can mean the game has not used the class yet: try again later.
3. `pointer_find_references(target="<klass>", module="GameAssembly.dll")`: a holder's `symbol`, such as
   `GameAssembly.dll+<TypeInfoRVA>`, is the `<Class>_TypeInfo` global of step 3 under Static fields.
4. Instances: `aob_find_value(valueType="pointer", value="<klass>", writable="required", alignment=8, limit=100)` finds
   what `mono_start_instance_search` finds, with the same filtering.
5. Field offsets have no names here: find the value with a scan ([value scans](value-scans.md)). The hit minus the
   object start is an instance field's offset; a hit inside the static block, minus `[<klass>+B8]`, is a static
   field's.

## Methods and hooks

- Generic sharing: one native body serves every reference-type instantiation, so a hook there fires for all of them;
  filter on the instance's class pointer (`[rcx]` for an instance method on x64 Windows).
- The C++ compiler can inline small getters and setters, so a hook on them may never fire: hook the code that writes
  the field instead.
- Hooks and patches are Auto Assembler work ([x64 injection](x64-injection.md)): explain the change and get consent.
- `mono_invoke_method` runs game code on the collector's thread, not the game's main thread, and many UnityEngine APIs
  expect the main thread (UNVERIFIED per API): one call per explicit user request, never in a loop.
- On a Unity game the first `speedhack_set_speed`, speed 1 included, starts the collector and hooks
  `UnityEngine.Time.set_timeScale`, and every speed change then also calls `set_timeScale` in the game, unless a
  hidden CE setting skips this Unity path ([speedhack](speedhack.md)).

## Pitfalls

- `mono_find_class` wants plain names: a nested name with `+`, or a generic name with a backtick and brackets, makes
  CE run `System.Type.GetType` inside the game. Use `mono_list_classes(imageHandle="<imageHandle>",
  nameContains="<Class>")` instead.
- CE remembers a failed class lookup until the collector is attached again: retrying the same name keeps returning
  `not_found`.
- Paused target or stopped debugger: Mono calls return `invalid_state`; resume with `process_set_paused(paused=false)`
  or `debugger_continue()`.
- After a collector pipe timeout or error, the next Mono call closes the failed pipe and reconnects by itself.
  `mono_get_object` reports `invalid_state` for the call that hit it, so retry it once; a class, field or method list
  that the failure interrupted can come back short or as `not_found`, so list again. If the reconnect fails
  (`invalid_state`), follow the hint: `mono_detach()`; when it returns `not_found` or `alreadyEnded`, CE owns the
  attachment, so ask the user to deactivate and re-activate Mono features in CE's Mono menu; otherwise, with consent,
  `mono_attach()` once. While CE shows its Mono timeout dialog, Mono calls return `invalid_state` until the user
  answers it (Wait, Cancel or Abort).
- `mono_detach()` closes only the attachment this activation made; if it already ended in CE, the result's
  `alreadyEnded` is true and CE's current attachment is left alone. The collector DLL stays loaded and Uses Mono stays
  in the table; `mono_detach.remainingEffects` also names the `mono_error_ok` patch, which CE never makes on IL2CPP.
  Tell the user: only a game restart unloads the DLL, and the user can untick Uses Mono in CE's table options.

## Sources

- CE 7.7 install: `autorun/monoscript.lua` (launcher, symbol thread, class lookup and cache, IL2CPP class scan, pipe
  timeout and detach), `autorun/dlls/src/Mono/MonoDataCollector/PipeServer.cpp` (`CompileMethod`, static field data,
  `FindClass2`), `autorun/SpeedhackV3.lua`
- https://github.com/cheat-engine/cheat-engine
- https://wiki.cheatengine.org/index.php?title=Mono:Lua
- https://docs.unity3d.com/Manual/il2cpp-introduction.html
- https://docs.unity3d.com/Manual/performance-incremental-garbage-collection.html (non-compacting collector)
- https://unity.com/blog/engine-platform/il2cpp-internals-generic-sharing-implementation
- https://github.com/Perfare/Il2CppDumper (README; per-version headers for `Il2CppClass` and `Il2CppImage`;
  `Outputs/StructGenerator.cs` for `_TypeInfo` in `script.json`)
- https://learn.microsoft.com/windows/win32/debug/pe-format#general-concepts
- https://learn.microsoft.com/cpp/build/reference/base-base-address
