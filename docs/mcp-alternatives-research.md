# MCP alternatives and useful gaps

Research date: 2026-10-19. Baseline: this repository at `0866b6e` (beta.4), with 193 backend tools and the gateway's
`instance_list`. The comparison is based on public documentation and our source, not a live test of other servers.
Tool counts are not comparable coverage measurements: several alternatives expose a separate tool per value type.

## Findings

The useful gaps are workflows around the existing memory and inspection primitives: discover text without knowing
its contents first, inspect an object's virtual-function pointers, and explain where batched pointer chains diverge.
The existing catalog already covers memory editing, scans, RTTI, structure reads, code references, captured-operand
analysis and typed errors. Duplicating those tools would add more names without adding useful capability.

Larger opportunities include durable investigation notes, offline metadata searches, structure interchange and
qualified plugin reloads. These need their own persistence, lifecycle or input-format contracts. No dependency was
added for the three bounded improvements below.

## What already exists

| Workflow | Existing tools |
|---|---|
| Read different types at many expressions | `memory_read_batch` |
| Take snapshots and compare typed changes | `memory_create_snapshot`, `memory_compare_snapshot` |
| Follow chains with complete evidence | `pointer_read_chain`; batch validation through `pointer_read_chains` |
| Explain a captured memory operand | `pointer_get_access_info` |
| Identify an object's RTTI class | `memory_get_address_info(includeRtti=true)` |
| Read, compare and export known layouts as C | `structure_read`, `structure_compare`, `structure_generate_c_header` |
| Find strings whose exact text is known | `aob_find_value(valueType="string" or "wstring")` |
| Find code references, referenced strings and control flow | `code_find_references`, `code_find_strings`, `code_get_function_graph` |
| Search modules and symbols without dumping every result | `module_list`, `symbol_find` |
| Keep comments and bookmarks in a cheat table | `code_set_comment`, address-list record tools, `table_save` |

## Implementation checklist

These are workflow improvements inspired by the comparison, not claims that every alternative has these exact APIs.

- [x] `memory_find_strings`: discover readable Unicode runs in a caller-selected range, with explicit UTF-8/UTF-16LE
  decoding, literal filtering, addresses, bounded previews and pagination. This answers “what text is near this
  object?” without requiring the AI to know the text in advance or interpreting a large hex dump itself.
- [x] `memory_read_vtable`: inspect a fixed number of pointer slots from an object/subobject or a direct table
  address, with the target's pointer width, symbol names and per-slot errors. This supplies facts for subsequent
  `code_disassemble`/`code_get_function_graph` calls; it does not infer a class layout or table extent.
- [x] `pointer_read_chains(includeHops=true)`: return each candidate's completed dereferences, including the prefix
  before a failed hop, using the existing read budget. The default response remains compact.
- [x] Register both new tools in backend/gateway composition, source-generated JSON, contract snapshots, the tool
  map and positive adapter-evidence inventory; run focused and full portable tests.

## Remaining roadmap

| Priority | Work | Why it is separate |
|---|---|---|
| High | Extend exact-bundle native success fixtures, including the new inspection tools. | Portable Client-double and routing tests cannot prove CE host behavior. The previous qualification report remains explicit about native gaps. |
| High | Durable investigation notebook: named findings, module identity/RVA, evidence and confidence. | CE comments/records persist, but are not a structured research notebook. File ownership, schema migration and target identity need a clear contract. |
| Medium | Offline IL2CPP metadata/dump import and paginated class/method searches. | Complements the current live collectors, but needs representative versioned fixtures and a supported input format. |
| Medium | Standalone structure-definition interchange. | `structure_generate_c_header` and table persistence exist; importing/exporting selected layouts needs an explicit format and conflict behavior. |
| Medium | Paginated process enumeration. | `process_list` filters/caps results, whereas module and symbol tools also page. Less urgent than inspecting target data. |
| Medium | Qualified same-process plugin reload. | This is native lifecycle work, with an existing qualification gap; a loader/core split should not be introduced as incidental feature work. |
| Later | Remote/WSL connection profiles and companion decompiler/debugger workflows. | These change deployment assumptions and should be designed and tested as separate integrations. |

## Completion criteria for the remaining roadmap

Feature completeness means an AI can discover, inspect, explain, preserve and revisit its findings with explicit
limits and reliable results. A catalog entry alone does not complete a workflow. Each feature below needs public
input/output contracts, deterministic tests, MCP/gateway checks, help content and representative native evidence
where a live host is involved.

- [ ] **Exact-bundle native coverage:** provide an owned fixture and independent expected result for every tool;
  record target identity, mutation restoration, resource cleanup and restart behavior. Keep unsupported environment
  cases explicit. Extend the existing acceptance matrix to include these new tools.
- [x] **Persistent investigation notes:** create, search, update and export named findings; preserve module identity,
  relative address, supporting observations and confidence. Prove restart persistence and reject stale target
  bindings. Define file ownership, version migration and conflict behavior before implementation.
- [x] **Offline IL2CPP metadata:** import an explicitly supported dump format, page type/method/field searches,
  retain the source's identity, and resolve an RVA only against a matching loaded module. Test representative formats,
  malformed/oversized files and mismatched modules; do not assume a metadata address is currently executable.
- [x] **Structure interchange:** export/import selected layouts with field types, offsets, nested layouts and
  target pointer width. Define duplicate-name handling, recursive references and validation before changing any
  existing structure; demonstrate a lossless round trip with independent fixture expectations.
- [x] **Process pagination:** keep the current default response compatible, add stable ordering and a continuation
  contract, and explicitly handle processes exiting or appearing between pages.
- [ ] **Same-process reload:** qualify disable/re-enable in the actual host, including plugin-list discovery,
  outstanding callbacks, leases, port cleanup and fresh instance identity. Repeated successful load calls alone
  do not meet this criterion.
- [x] **Optional external connections:** design connection identity, authentication and failure recovery for remote
  clients and companion analysis tools; document which process/module each observation refers to.

## Latest verified baseline

The four feature criteria above passed their portable and standalone Lua checks and the private x64 workflow
`20261019T131741Z-b4d5` on the source-built bundle. That workflow checked the notebook after target, gateway and
host restarts; exercised offline metadata import/search/resolution; round-tripped nested structure definitions;
and replayed process pages. It restored user state, preserved the installed CE files and reported no cleanup failures.
These feature checks do not complete the separate all-tool or same-process reload criteria.

| Completed feature | Representative passing regression |
|---|---|
| Durable notes and stale binding refusal | `InvestigationToolsTests.CreateThenRestartSearch_RecoversTheImmutableRevision`; `Update_RetainedStaleBinding_IsRejectedBeforePublishingSuccessor` |
| Offline metadata identity and bounded imports | `Il2CppDumpStoreTests.Tools_ResolveMethod_RejectsChangedTargetBuildAndUnboundImports`; `Tools_ImportRejectsMalformedUtf8AndOversizeFiles` |
| Structure interchange and width validation | `StructureInterchangeToolsTests.Import_ProductionAdapter_ValidFilePassesEncodedNestedReferencesToOneLuaMutation`; `Import_PointerWidthMismatch_IsRefusedBeforeLuaMutation` |
| Compatible, stable process pages | `ProcessPagingTests.List_LegacyRequest_PreservesTheOriginalRequestAndJsonShape`; `List_PagedSnapshot_SortsOnceAndKeepsTheOriginalProcessesAcrossPages` |

The current catalog contains 206 backend tools plus gateway discovery. The latest Release verification has 5,665
passing portable cases and 742 passing standalone Lua cases. Detailed receipts and the remaining native gaps are recorded in the
[full tool qualification report](qualification/all-tools/README.md). A subsequent x86 workflow found that pointer
map capture retained out-of-width WOW64 module metadata; its correction passed regression tests and is awaiting
fresh native verification.

## Earlier inspection baseline

Before the remaining roadmap implementation began, the catalog contained 195 backend tools plus gateway
discovery. The .NET 10.0.402 Debug and Release builds completed with zero warnings/errors. Each portable suite
passed 5,387 tests; standalone NativeLua passed 651 tests, all with zero failures/skips. Nine deterministic fuzz
tests exercised 1,600 generated cases. The bundled-plugin load probe and Native AOT gateway smoke check passed.
These receipts describe that baseline; further roadmap changes require fresh verification.

| Requirement | Representative passing evidence |
|---|---|
| Exact string bytes/addresses and Unicode handling | `MemoryStringToolsTests.FindStrings_Utf8MultibyteRuns_ReadsTheExactRangeOnceAndReturnsExactAddresses`; `FindStrings_MinimumLength_CountsScalarsInsteadOfBytesOrUtf16CodeUnits` |
| Native pointer-width slots, symbols and independent errors | `MemoryVtableToolsTests.ReadVtable_ObjectAddress_ReadsTheFirstPointerThenNamesEachNonNullSlot`; `ReadVtable_UnreadableSlot_ReportsItAndContinuesToLaterSlots` |
| Useful chain evidence without additional reads | `PointerBatchHopTests.ReadChains_IncludeHops_ReportsOrderedSignedDereferencesWithoutExtraReads`; `ReadChains_UnreadableHop_RetainsOnlyCompletedPrefixAndIndependentCandidates` |
| Exact values through MCP and the gateway | `ExactValuePipelineTests.MemoryFindStrings_UnicodeAndWideAddress_RoundTripAcrossClientAndGateway`; `MemoryReadVtable_WidePointersNullAndUnreadableSlots_RoundTripAcrossClientAndGateway` |

The tests above use Client doubles. Separate private x64 and x86 workflow sessions passed on the exact packaged
baseline, restored user state, left the installed CE files unchanged, and reported no cleanup failures. The reports
verify routing for 196 advertised tools, positive adapter evidence for 195 backend tools and native success evidence
for 62 tools. The other 133 backend tools still require native success fixtures. Both sessions used disabled optional
capability gates and zero plugin reload cycles; they do not establish gated operations or same-process reload.

The completed feature contracts now have the verification described above. The
[external connection design](external-connections.md) defines transport, identity, authentication and recovery;
its environment acceptance matrix is explicitly separate from local test results.
