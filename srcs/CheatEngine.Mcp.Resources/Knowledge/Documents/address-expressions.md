# Address expressions and number formats

Address parameters take Cheat Engine (CE) address expressions, in which every number is hex. Offsets, sizes, values
and results use stricter formats, listed here by parameter. Read this before you build an address, convert a pointer
chain or pass a number whose base matters. Writes follow [safety](safety.md): single-player or offline software, with
the user's consent.

## Address expressions

Address parameters, such as `memory_read(address=...)`, `pointer_read_chain(base=...)`, record addresses and range
bounds, go to CE's own symbol handler; tools reach it through `getAddressSafe`. The exceptions are listed under Number
formats of inputs.

| Form | Example | Meaning |
|---|---|---|
| Hex number | `7FF6A1B2C3D0`, `0x7FF6A1B2C3D0` | An absolute address. |
| Module | `game.exe` | The module's base address. |
| Module and offset | `game.exe+1A2B30`, `game.exe-10` | The base plus or minus a hex offset; it survives ASLR. |
| Quoted module | `"Tutorial-x86_64.exe"+2FD4A0` | Quote a name that contains `-`, `+`, `*`, brackets or parentheses: CE splits on them. |
| Export or PDB symbol | `kernel32.CreateFileW` | `module.name`; `module!name` also works. A bare `CreateFileW` resolves too (see duplicate names below). |
| Registered symbol | `playerBase+10` | A name from symbol_register, an AA `registersymbol` or a loaded table. |
| Dereference | `[[game.exe+1A2B30]+10]+4C8` | `[x]` reads the pointer at x, at the target's pointer size. Brackets nest. |
| Arithmetic | `game.exe+1A2B30+4*8` | Only `+`, `-` and `*`, with `*` first. There is no `/`, and parentheses never group. |
| Typecast | `(DWORD)[game.exe+10]` | Keeps the low 32 bits of the pointer read: a 32-bit pointer in a 64-bit target. `(BYTE)`, `(WORD)` and `(QWORD)` also work; `(CHAR)`, `(SHORT)` and `(LONG)` sign-extend. |
| Record description | `(Health)+4` | The last computed address of the address-list record described `Health`. |
| Structure element | `[playerBase]+PlayerData.health` | The offset of `health` in the CE structure `PlayerData`, ignoring case. |
| Thread stack | `THREADSTACK0-2F8` | A slot near the top of thread 0's stack (Toolhelp order, 0 is usually the main thread). CE's own pointer scanner reports such roots; this server's pointer tools never do. |

Rules:

- **Every number is hex.** `game.exe+100` is 256 bytes past the base. Expressions have no decimal notation; convert
  with util_calculate. Write offsets as bare hex (`+4C8`): `0x` is native only at the start of a whole plain number,
  and inside an expression (`game.exe+0x10`) it resolves only through CE's Lua fallback (see Lua inside expressions).
- **A name made only of hex digits is a number** (`BEEF`, `add`). symbol_register refuses such names.
- **Registers have no thread context here.** `RAX` never resolves as a register. If it resolves at all, it is another
  kind of name, such as a Lua global left by an earlier debugger context read, and may be stale. Read registers with
  debugger_get_context and pass the hex value.
- **An unreadable bracket fails the whole expression** (`not_found`). pointer_read_chain instead fails with
  `memory_read_failed` and names the failing hop.
- **An unknown name can block** while CE waits for its symbol loader. `symbol_resolve(shallow=true)` skips that wait and
  CE's lookup callbacks (Mono names, the Lua fallback); a `$name` token still runs.
- **Mono names change the target.** After mono_attach, `Namespace.Class:Method` resolves by JIT-compiling the method in
  the game, and `Class:field` gives a static field's address or an instance field's offset. See
  [Mono and .NET](mono-and-dotnet.md).

Test an expression with `symbol_resolve(expressions=["game.exe+1A2B30", "[game.exe+1A2B30]+10"])`. Each entry of
symbol_resolve.items gives the `address` and CE's best `name`, or its own `error`. A single-address tool fails with
`not_found` when its address does not resolve; memory_read_batch, memory_read_samples and memory_get_address_info
report it per item.

## Lua inside expressions

CE's symbol handler evaluates Lua in two cases:

- **`$` tokens.** A whole address of `$` and hex digits (`$7FF6A1B2C3D0`) is a hex number. Any other `$name` token runs
  as Lua: CE reads the Lua global `name`, else evaluates `return name`. A number result becomes the address; a string
  result is resolved again as an expression. Inside a longer expression even `game.exe+$10` runs `return 10` and adds
  ten, not 0x10 (CE source).
- **The Lua fallback.** CE 7.7's `autorun/luasymbols.lua` registers a lookup callback that evaluates any token CE
  cannot resolve, except one that starts with a quote, as the Lua expression `return <token>`. A name that happens to
  be a Lua global resolves to its value.

This server has no address-expression screen. Every address parameter reaches the symbol handler as given, in
read-only tools too, whatever Mcp:EnableUnsafeLua says, and CE evaluates a record's address again each time it
re-interprets the address list. Only asm_check screens a script: it refuses `$` before anything but hex digits as
`unsupported` (hostEffect `not_started`), and still sends the script's other addresses and operands to the symbol
handler, the Lua fallback included.

- Write only the forms of the table above: no `$`, no quotes except around a module name, and no Lua syntax such as
  function calls or braces.
- Run Lua only through lua_execute, which needs Mcp:EnableUnsafeLua and the user's consent; see [Lua](lua.md).

## Pointer chains in three notations

One chain: read the pointer at `game.exe+1A2B30`, add 10, read the pointer there, add 4C8.

| Where | Written as | Order |
|---|---|---|
| CE expression | `[[game.exe+1A2B30]+10]+4C8` | The innermost bracket first. |
| Tools (pointer_read_chain, record offsets, pointer-scan paths) | base `game.exe+1A2B30`, offsets `"10"`, `"4C8"` | Dereference order: the first offset follows the base. |
| CE's Add address dialog, .CT files, Lua `Offset[i]` | `4C8` first; `10` sits just above the base | Reversed: the first listed is applied last. |

- **Read it:** `pointer_read_chain(base="game.exe+1A2B30", offsets=["10", "4C8"], valueType="float")`. The offsets are
  signed hex strings; pointer_read_chain.hops shows every read and pointer_read_chain.expression the bracket form.
- **Keep it as a record:**
  `record_create(records=[{description="Health", address="game.exe+1A2B30", variableType=4, offsets=["10", "4C8"]}])`.
  Record offsets are the same signed hex strings in dereference order; the tool stores them reversed for CE, which
  follows them on every read. Without a value the create writes nothing. record_get, record_list and record_find
  return the `offsets` in the same form; a symbolic offset from a loaded table is copied as text, never evaluated.
- **Change it:** CE clears the offsets when the address changes, so give both:
  `record_update(updates=[{id=<id>, address="game.exe+1A2B38", offsets=["10", "4C8"]}])`. `offsets=[]` in an update
  removes the chain.
- **Bracket addresses in records go stale.** A bracket expression works as a record address, but CE re-evaluates
  address text only when it re-interprets the address list: every 15th list update (about 7.5 s at CE's default
  500 ms update interval), when symbols finish loading and after a script that registers symbols is enabled. Use
  offsets instead.
- **From a pointer scan:** each pointer_list_paths entry has an `expression` (module quoted, such as
  `[["game.exe"+1A2B30]+10]+4C8`), usable as any address, and its `offsets` (signed hex, dereference order). For a
  module root, pass `module+moduleOffset` as pointer_read_chain.base or as the record address, with the offsets
  unchanged.
- **Negative offsets:** `"-8"` in pointer_read_chain and record offsets, `[[base]+10]-8` in an expression. Record
  offsets range from -80000000 to 7FFFFFFF, so `"FFFFFFF8"` is refused with a hint to write `"-8"`.

More: [pointers](pointers.md) and [manual pointer chain](../Workflows/manual-pointer-chain.md).

## Number formats of inputs

| Input | Format |
|---|---|
| Address parameters | A CE expression, hex by default. |
| kernel_read_physical.physicalAddress, kernel_write_physical.physicalAddress, kernel_start_watch.physicalAddress | A plain hex literal, `0x` optional; no expression. |
| pointer_find_paths.target, pointer_rescan_paths.target, pointer_find_references.target | A plain hex literal (`0x` optional) is taken as is. Anything else is resolved in the selected process, even for a map of an earlier one: pass hex for offline work. |
| process_open_file.startAddress | A plain number that CE reads as a Lua integer (CE source): write hex as `"0x10000000"`. Bare digits are decimal, and a symbol gives 0. |
| pointer_read_chain.offsets, record_create and record_update offsets, structure element offsets and child starts, structure_read.fromOffset and toOffset | Signed hex strings, `+` and `0x` optional: `"4C8"`, `"-8"`. `"1224"` means 0x1224. |
| structure_autoguess.offset | A hex string, zero or greater (`"400"`); a negative one is refused. |
| debugger_start_trace.stopCondition | `REG=HEX`, such as `RAX=1A`: compared as a hex number, leading zeros and `0x` ignored. |
| scan_first.lastDigits | 1 to 16 hex digits that the address must end with. |
| asm_generate_injection.offset, the maxOffset of pointer_find_paths and pointer_find_references, structure_read.offset (an element index), every size, count, limit, alignment and paging offset | Decimal JSON integers: `1224`, not `0x4C8`. |
| Values of memory_write, memory_write_batch, structure_write_element, aob_find_value, util_convert_value | Integers in decimal or `0x` hex (`0xFF` is -1 as int8); floats as invariant text (`1.5`, `NaN`, `Infinity`); valueType pointer takes a hex address, never an expression. |
| Values of record_create and record_update | Types 0-5 and 13: decimal (`100`, `-5`, `1.5`, `1.5e3`) or `0x` hex. Other text, an expression included, is refused: CE would evaluate it as Lua. A byte array record (8) takes hex pairs, `??` keeping a byte. |
| Values of scan_first and scan_next | Decimal only, `0x` refused. byte takes 0 to 255; int16, int32 and int64 are signed, so pass a larger value as its signed equivalent (`-1` for 4294967295 in int32). Floats must be finite. |
| Byte strings: bytes values, asm_apply_code_patch, asm_generate_injection.expectedBytes, code_disassemble_bytes, kernel_write_physical | Hex pairs, spaced or not: `48 8B 05`, `488B05`. |
| aob_find.patterns | Hex pairs with `??` wildcards; see [AOB signatures](aob-signatures.md). |
| exec_call_remote and exec_call_method argument values | Always JSON strings. An integer is decimal, `0x` hex, or bare hex when it contains A-F: `"10"` is ten, `"1A"` and `"0x10"` are hex. Prefix addresses with `0x`. |
| exec_call_local.parameter | A decimal JSON integer; a negative one passes its two's complement. |
| mono_invoke_method argument values | Types 0-3: decimal or `0x` (`true` or `false` for 0). Type 12: an address expression, `0` for null. |
| debugger_set_register.value | A signed decimal JSON integer; `-1` sets all bits. |

Two traps: a pointer value `1000` means 0x1000, but an exec integer argument `10000000` means ten million.

## Output formats

- **Addresses:** uppercase hex without `0x` or leading zeros (`7FF6A1B2C3D0`, `4A2B10`), scan_list_results included.
  Any address parameter accepts them.
- **Names** (symbol_resolve, memory_get_address_info) are CE's display names, such as `game.exe+1C0`.
- **Typed values** are strings: decimal integers (64-bit ones included), round-trip floats with `NaN` and `Infinity`,
  hex pointers and spaced bytes (`48 8B 05`). scan_list_results.value is CE's own display text.
- **Instruction bytes** are unspaced (`488B05`) in asm_assemble.bytes and code_disassemble results.
- **Offsets** are hex strings in pointer, structure, record and memory_compare results, signed where they can be
  negative.
- **Decimal integers:** the field offsets of mono_list_fields, mono_get_object, dotnet_get_type and dotnet_get_object,
  mono_get_object.offsetInObject, aob_generate_signature.offset and all sizes. Convert one before an expression:
  offset 200 is `+C8`.
- **Handles:** Mono and .NET handles and tokens are decimal strings; pass them back unchanged.
- **Decimal addresses:** exec_call_remote.returnValue (also exec_call_local, exec_call_method) and
  mono_invoke_method.returnValue print pointers in decimal. Convert with util_calculate before using one as an
  address.

## Converting numbers

Both run locally, without the target.

`util_calculate(expression=...)` evaluates `+ - * / % << >> & ^ | ~` and parentheses with unsigned 64-bit wraparound,
and returns `hex`, `unsignedValue` and `signedValue`. Its literals are **decimal unless prefixed `0x`**, the opposite of
CE. It knows no symbols or brackets: resolve those with symbol_resolve first.

- `util_calculate(expression="1224")` gives the hex `4C8`; `util_calculate(expression="0x4C8")` gives `1224`, the
  decimal form that sizes and counts take.
- `util_calculate(expression="0x7FF6A1B2C3D0 - 0x7FF6A1B20000")` gives `C3D0`, a module offset.
- `util_calculate(expression="-8")` gives the signed `-8` and the hex `FFFFFFFFFFFFFFF8`.

`util_convert_value(value=..., sourceType=..., targetType=...)` encodes the value (memory_write syntax) as
little-endian bytes and decodes them as the target type; a fixed-size target needs the same byte count.
util_convert_value.pointerSize is 4 or 8.

- `util_convert_value(value="100", sourceType="float", targetType="bytes")` gives `00 00 C8 42`.
- `util_convert_value(value="50000", sourceType="uint16", targetType="int16")` gives `-15536`, to scan as int16.
- Setting util_convert_value.byteOrder to big_endian only reverses the bytes shown; the value is still encoded and
  decoded as little-endian.
- Big-endian memory (an [emulated console](emulators.md)): read and write multi-byte numbers (int16 to uint64, float,
  double) directly, as in `memory_read(address="...", valueType="float", byteOrder="big_endian")`; memory_write,
  memory_read_samples and the items of memory_read_batch and memory_write_batch take the same byteOrder. Reverse the
  pairs yourself only for util_convert_value, pointers and scans: `42 C8 00 00` decodes with
  `util_convert_value(value="00 00 C8 42", sourceType="bytes", targetType="float")` as `100`.

More on encodings: [value types](value-types.md).

## Finding and naming addresses

- **Search by name:** `symbol_find(nameContains="Health", module="game.exe")` searches registered symbols, registered
  symbol lists ({$C} functions, IL2CPP methods as CE enumerates them) and the main list (exports, PDB); not .NET,
  Mono JIT or module names. While symbol_find.symbolsLoaded is `false`, symbols or IL2CPP methods are still loading:
  search again later. With large symbol sets one call can block CE for a second or more.
- **Address to name:** `memory_get_address_info(addresses=["7FF6A1B2C3D0"])` gives the symbol, module, section and
  region. Store module+offset so a table survives ASLR. See [identify an address](../Workflows/identify-address.md).
- **Name an address:** `symbol_register(name="playerBase", address="[game.exe+1A2B30]+10")` resolves the expression
  once, now. After the object moves, symbol_unregister the name and register it again.
- **Duplicate names:** a bare name that several modules export, such as `CreateFileW` (kernel32 and kernelbase),
  resolves in the first module of CE's module preference, which symbol_get_module_preference lists. Write
  `kernel32.CreateFileW` to choose. `symbol_set_module_preference(modules=["game"])` moves the named modules
  (extensionless: `game`, not `game.exe`) to the front, but it changes every later lookup in CE, tables included, and
  nothing restores it: ask first, keep the list you read, and set it back with
  `symbol_set_module_preference(modules=[...], replace=true)`.

See also [errors and recovery](errors-and-recovery.md) and the [memory model](memory-model.md).

## Sources

- CE source, https://github.com/cheat-engine/cheat-engine: `symbolhandler.pas` (`tokenize`, `getAddressFromName`
  with its `$` and lookup-callback paths, `LookupStructureOffset`), `LuaHandler.pas` (`getAddressSafe`,
  `openFileAsProcess`), `CEFuncProc.pas` (`GetStackStart`), `MemoryRecordUnit.pas` (`GetRealAddress`,
  `ReinterpretAddress`), `MainUnit.pas` (`UpdateTimerTimer`), `formAddressChangeUnit.pas` (offset boxes).
- CE 7.7: `autorun/luasymbols.lua` (Lua fallback), `autorun/monoscript.lua` (Mono symbol lookup), `celua.txt`
  (`getAddressSafe`, `getModulePreference`, `setModulePreference`, `debug_getContext`).
- CE wiki: [pointers](https://wiki.cheatengine.org/index.php?title=Tutorials:Pointers).
