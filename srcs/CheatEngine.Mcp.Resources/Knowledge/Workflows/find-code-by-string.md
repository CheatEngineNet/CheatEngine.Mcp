# Find the code that uses a text

## Goal

Find the instructions that use the text `{text}` in the module `{module}` (if not given, the main module:
`processName` of `process_get_current()`), encoding `{encoding}` (`auto` tries UTF-8 and UTF-16). Nothing here
changes the game.

## Steps

1. `module_get(module="{module}")`: keep `pe.managed` and the `executable` sections (code, `.text`); literals sit in
   the others (`.rdata`).
2. Find the literal (exact, case-sensitive, no terminator):
   `aob_find_value(valueType="string", value="{text}", module="{module}", limit=10)` for `utf8`,
   `aob_find_value(valueType="wstring", value="{text}", module="{module}", limit=10)` for `utf16`, both for `auto`.
3. Warn that a pass may freeze Cheat Engine, then dissect the code in windows of up to 1 MiB:
   `code_start_dissect(address="<.text address>", size=1048576)`, keep `jobId`, and
   `code_poll_job(jobId=..., afterSequence=0)` until `job.state` is `completed`.
4. `code_find_strings(textContains="{text}")` lists each referenced literal containing the text with its start
   `address`; `code_find_references(address="<that address>")` lists each `fromAddress`, often a `lea` before a call.
   None yet: dissect the next window.
5. Or sweep one function or small range for the literal's address, which the tools print as absolute hex
   (`lea rcx,[7FF6A1DF1A30]`, no module names):
   `code_start_search(address="<start>", size=65536, textContains="<literal address>")`, then `code_poll_job`.
6. A literal used through a pointer table has no direct reference:
   `pointer_find_references(target="<literal>", module="{module}", writableOnly=false)` gives the slot, then
   `code_find_references(address="<slot or table start>")`.
7. Per reference: `code_get_function(address="<fromAddress>")`, `code_get_function_graph(address="<startAddress>")`
   for the branch to it, `code_disassemble(address="<fromAddress>", before=8, count=16)` and
   `symbol_resolve(expressions=["<fromAddress>"])`.
8. Only if the user wants notes kept: `code_set_comment(address="<fromAddress>", comment="MCP: shows {text}")`.
9. `runtime_stop_job(jobId=...)` for each job. `code_clear_dissect()` only if the user asks: it erases their dissect
   data too.

## Decisions

- 32-bit target (`pointerSize` 4): instructions embed the literal's address, so
  `aob_find_value(valueType="uint32", value="0x<literal>", module="{module}", executable="required", alignment=1)`
  finds it a few bytes into each referencing instruction, without a dissect.
- Several copies: code usually references the read-only one; a writable copy
  (`memory_get_address_info(addresses=["<literal>"])`) may be a buffer.
- Several references: the check around each tells which caller matters.
- Next: [trace the logic](trace-logic.md) of the check, [force or invert the branch](patch-branch.md), or
  [find what writes](find-writer.md) the value it compares.

## Pitfalls

- No literal: try the game's other modules; else the text comes from data files (localization) or is built at run
  time; use [find text](find-text.md).
- `pe.managed` true, or a Unity game: literals live in metadata, reached through runtime string objects; use
  [Mono recon](unity-mono-recon.md), [.NET recon](dotnet-recon.md) or [IL2CPP recon](unity-il2cpp-recon.md).
- Cheat Engine's dissector (shared with its Memory View) keeps data across passes and goes stale after a game
  update. An empty result is not proof: computed addresses and undissected windows are missing.
- One dissect at a time (`busy`); expired jobs discard their results; running ones block target switching.

## Report

A table: literal address, encoding and section; each referencing instruction (address, module+offset, text); its
function start and the check around it. Say which jobs were stopped, that the dissect data stays in Cheat Engine
until cleared, and which comments were written. Background: [code analysis](../Documents/code-analysis.md),
[AOB signatures](../Documents/aob-signatures.md), [value types](../Documents/value-types.md).
