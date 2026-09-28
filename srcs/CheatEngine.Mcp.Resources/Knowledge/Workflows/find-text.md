# Find and edit text in memory

## Goal

Find where the target holds `{text}` (encoding `{encoding}`; `auto` tries UTF-16, then UTF-8), its container and
owner, and, when `{purpose}` is `edit`, change it in place at the same length or shorter. Searches only read; an edit
needs the user's consent, on single-player or offline software the user may modify.

## Steps

1. `process_get_current()` confirms the target; `module_list(nameContains="mono")` (then `clr`, `GameAssembly`)
   tells which containers to expect.
2. One scan per encoding (`utf16`: the first, `utf8`: the second, `auto`: both). Each scans the whole target and
   blocks Cheat Engine meanwhile; tell the user first:
   `aob_find_value(valueType="wstring", value="{text}", writable="required", limit=50)`,
   `aob_find_value(valueType="string", value="{text}", writable="required", limit=50)`. Matches (`result.matches`)
   are exact and case-sensitive, without a terminator.
3. `memory_get_address_info(addresses=["<h1>", "<h2>"])`: region type `private` is a heap copy, `image` a module
   buffer. `memory_read(address="<h1>", valueType="wstring", length=128)` (or `string`) shows the whole text;
   `memory_read(address="<h1>-20", valueType="bytes", size=64)` the bytes before it (offsets are hexadecimal).
4. Container (64-bit layouts):
   - managed UTF-16: `memory_read(address="<h1>-4", valueType="int32")` equals the character count; the object is
     `<h1>-14` on Mono or IL2CPP, `<h1>-C` on .NET (`dotnet_get_object(address="<h1>-C")` confirms it);
   - MSVC `std::string` (size at object+10): up to 15 bytes (`std::wstring`: 7 characters) inline, so the hit is
     the object; longer text is a heap buffer whose holder (step 5) is the object;
   - Unreal `FString`: the holder is a data pointer, int32 Num at +8 (text length plus the terminator) and Max;
   - `char[N]` inside an object or a bare `char*`: no length field.
5. Owner (a whole-target scan too): `pointer_find_references(target="<object>", limit=20)` (for a heap buffer, the
   hit). A holder with a `symbol` is static. On Unity, when `mono_get_status()` reports `attached`,
   `mono_get_object(address="<holder>")` names the owner's class: the instance field whose `offset` equals
   `offsetInObject` points to the text (no such field: not the owner). Then
   [dissect the object that holds it](dissect-structure.md).
6. Edit, only when `{purpose}` is `edit` and the user agrees, one copy at a time:
   - save: `memory_read(address="<h1>", valueType="bytes", size=<text bytes + 2>)`;
   - same length: `memory_write(address="<h1>", valueType="wstring", value="<new text>")` (or `string`);
   - shorter: the same call with memory_write.nullTerminate true, then the new length, such as
     `memory_write(address="<h1>-4", valueType="int32", value="<characters>")` (managed), the `std::string` size
     or the `FString` Num;
   - check memory_write.verified, then ask whether the game shows it.
7. Undo: `memory_write(address="<h1>", valueType="bytes", value="<saved bytes>")`, then the length from step 4.

## Decisions

- UTF-16: .NET, Unity, Unreal, Windows UI. UTF-8: C and C++ engines, Lua, JSON, save buffers.
- No hit (with `result.exact` false, search again): the screen may change case or add markup, or the game uses a
  code page, UTF-32 or glyph indices; search a shorter part as stored. Text in mapped memory (emulated RAM) needs
  aob_find_value.includeMapped true. Found only with aob_find_value.writable `excluded`: a read-only literal;
  [find code by string](find-code-by-string.md) finds the code that loads it, and editing it is out of scope.
- Many hits (UI, history, save and network copies): have the user rename in game and search the new text; the
  copies that follow are live, and the one a player or profile object owns is the source.
- Longer text needs a new allocation and a pointer swap: out of scope.

## Pitfalls

- Never write past the old length: the next field or object follows; the target crashes or saves bad data.
- Managed strings may be interned: one literal serves every use, and an edit changes them all. A .NET garbage
  collection can move the object; find it again before a later edit.
- Units differ: managed lengths count UTF-16 units; `std::string` size and UTF-8 text count bytes (accents take two
  or more).
- Server-side names (online accounts) are out of scope.

## Report

A table of address, encoding, region (module section or heap), container, length field, owner and, after an
edit, saved bytes, new text and what the game shows. Nothing else remains; an edit lasts until undone or rewritten.
See [value types](../Documents/value-types.md), [Mono and .NET](../Documents/mono-and-dotnet.md) and
[structures](../Documents/structures.md).
