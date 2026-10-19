# Full tool qualification

The current catalog contains 206 backend tools and the gateway's `instance_list` tool.
Every tool needs a reviewed success case with known inputs, an independent result or side-effect oracle,
and cleanup verification. Catalog discovery, routing, disabled-feature refusals and compilation of a fixed
Lua body do not establish successful native execution.

## Checklist

- [x] Enumerate the complete advertised catalog.
- [x] Check routing, missing/unknown instance refusal and stale-identity refusal for every backend tool.
- [x] Check gateway-local discovery with zero and two instances.
- [x] Run the portable Release suite; final results are recorded below.
- [x] Run the portable Debug suite with the same exclusions.
- [x] Run standalone Lua 5.3 with CE API doubles (742 passed, zero failed or skipped).
- [x] Add and run result assertions for the Lua, table-save and code-view adapters (12 passed).
- [x] Add seven record adapter cases and four code/process adapter cases; run both focused classes.
- [x] Fix the `code_get_comments` array argument expansion found by those tests.
- [x] Fix `process_list_threads` to use CE's documented zero-based StringList indexer; verify empty, truncated and error cleanup paths.
- [x] Prepare eight additional bounded live cases and review their assertions.
- [x] Attempt the private x64 lifecycle session against the published beta.3 bundle.
- [x] Verify both private hosts stopped, user state was restored and the installed CE files stayed unchanged.
- [ ] Qualify same-process plugin reload: the Settings bridge observes zero plugin rows after `loadPlugin` succeeded.
- [x] Add a separate disabled-capability workflow scenario so independent restart checks can complete.
- [x] Run the eight added live checks and six bounded workflows, then target, gateway and host restarts.
- [x] Map a positive production-adapter result/effect assertion for every backend tool.
- [x] Add SDK provider-stack containment and normal-path preservation regressions.
- [x] Exercise the SDK dispatcher with rooted worker/main coroutines, including queued-stop cleanup.
- [x] Prepare, review and run a bounded provider trace replay; retain non-reproduction as diagnostic evidence only.
- [x] Add direct adapter checks for managed collectors, debugger callbacks, execution, kernel calls, patches and table loading using doubles.
- [x] Update to published Client 1.1.0 and SDK 2.0.1; regenerate portable and AOT lockfiles.
- [x] Replace the Client and SDK NuGet dependencies with pinned main-branch Git submodules and verify the source build graph.
- [x] Require reviewed positive adapter mappings for every name in the production tool catalog.
- [x] Add execution-argument, conversion/calculation and pointer-file boundary/failure tests.
- [x] Measure handwritten product C# coverage using fresh portable and NativeLua reports.
- [x] Rebuild and repeat the disabled-capability workflows on the updated dependency bundle.
- [x] Qualify utility calculation/conversion, runtime overview, assembly bytes and reversible symbol preferences on the source-built x64 package.
- [ ] Complete the x86 workflow after the pointer-width fix and physical system-module path correction.
- [ ] Qualify owned resource release, scanner stop and structure guessing with independent readback and cleanup.
- [ ] Qualify file-as-process open/save with exact bytes, file preservation after refused attachment and verified host/target restart recovery.
- [x] Qualify fixed Lua and reversible Auto Assembler operations in their dedicated private scenario on package 12.
- [x] Qualify Windows debugger attachment, stopped state and cleanup against an owned target on package 11.
- [x] Qualify the fixed compiler scenario against source-built package 10, including gate refusal, exported bytes and cleanup.
- [x] Qualify managed injection with the reviewed helper enabled in the private host on package 11.
- [ ] Resolve the intermittent native SDK frame-restoration crash reproduced by the package 11 compiler session.
- [ ] Qualify generated AOB signatures, generated API hooks and owned process creation.
- [ ] Supply the remaining native managed object/instance, Mono, debugger event, compiler/ABI, PDB and patch fixtures.
- [x] Prepare and review separate fixed C/ABI, local DLL/PDB and advancing-thread debugger cases with independent cleanup receipts.
- [x] Qualify the four fixed C/ABI execution tools on package 13, including independent return values, buffer effects and cleanup.
- [x] Qualify local native DLL injection on package 13 using OS module identity, image hashes and independently parsed exports.
- [ ] Qualify successful gated operations and DBK/DBVM operations in their required environments.
- [ ] Qualify all tools on the exact replacement product bundle after fixes, including restart and cleanup.

The machine-readable report contains one row for each of the 207 tools. It keeps routing, tests using
doubles, direct fixed-Lua bodies and live results separate. The reviewed
[`behavior-evidence.json`](behavior-evidence.json) maps positive adapter evidence for all 206 backend names.
Each mapped test lists its exact required TRX case names; every case must occur once and pass.
Passing only one branch of a parameterized test cannot qualify another branch's tool.
These cases use Client or CE API doubles, including real standalone Lua where applicable. They are partial
cases, not complete behavior partitions or native CE qualification. The gateway's local discovery checks
bring the advertised catalog total to 207. A live mapping requires its own concrete oracle and cleanup receipts.

## Source-built native results from 2026-10-19

The file-target guard and recovery fixture, fresh-host PDB case and private debugger transition diagnostic
passed the integrated Release build with zero warnings/errors. All **5,665** portable and **742** standalone Lua
cases passed with zero failures or skips (`debugger-transition-portable-1` and `debugger-transition-native-lua-1`).
The diagnostic bridge records only the exact owned breakpoint event before one context request;
its callback preserves the previous callback's returns and errors. Its results cannot qualify native tool
success: a report-generation check with a passing session marked `diagnosticOnly` retained all 207 routing
rows and verified zero native tools. These changes still require their own native sessions.

Package 13 (`dfe5179f83944206eb9fdee9e5895c67ec543be8`) passed smoke session
`20261019T180318Z-59b1`, execution session `20261019T180412Z-c4a2` and native symbol fixture session
`20261019T180445Z-90d7`. Each restored user state, preserved installed files and reported no cleanup failures.
The reports `native-fixtures-{smoke,execution,symbol}-report-13` verify six smoke tools, four execution tools
and native library injection respectively. Each retains all 207 routing rows. Automatic symbol discovery
made the explicit reload/add-module checks inconclusive; their receipts are absent and they remain unqualified.

| Package 13 file | SHA-256 |
| --- | --- |
| `CheatEngine.Mcp.dll` | `5ED25C614204F9C9E65F2A5292734593377CEB5BA0EE9856580FB7244F3EA6CC` |
| `CheatEngine.Mcp.Gateway.exe` | `D3E854D45CB2A07C7615E1A1895F8ED156E72935CE606C24BFDF5740A6183B0A` |

The initial package 13 sessions exposed two harness defects: an unsupported address-list `clear` call,
and an omitted ordinary target stop signal. The supported record API now replaces the invalid clear call;
its regression fails against the old code and all **733** standalone Lua cases pass after the correction.
The ordinary target signal is restored, and the passing smoke session verifies normal shutdown of both
hosts and targets. Ten focused lifecycle cases also pass. The obsolete source-string assertion was removed
in favor of the behavioral Lua case. Initial failed sessions supply no native qualification.

The fixed process-selection trace in `20261019T175101Z-500d` confirms that the file-transition read failure
already occurs inside installed CE: `openProcess` returns Boolean `true` and a new nonzero handle, but CE's
own `readInteger` returns `nil` at the previously readable owned address. This separates that failure from
SDK result conversion. No callback-reset workaround or successful file-to-process transition is claimed.

Debugger session `20261019T180635Z-84d3` observed the exact owned thread and the fixed native return address
in a nonempty stack, then stalled after the first instruction breakpoint in the separate execution group.
Cleanup calls timed out and host A required termination; all owned processes stopped, settings were restored
and installed files were unchanged. Missing disabled-menu evidence remains a cleanup failure. The complete
session is failed and provides no verified debugger success cases.

SDK fixes [#97](https://github.com/CheatEngineNet/CheatEngine.SDK/pull/97) and
[#98](https://github.com/CheatEngineNet/CheatEngine.SDK/pull/98) are merged. Package 13 used
SDK main `900f240975ad077d5b920c70442b96a99bcc2aa4` and Client main
`9b836e6ced8e97b4506dc8523998c7c2081cc2ad`. The process-selection fix requires an actual Boolean `true`
from CE before checking the selected PID and pointer width. Its five rejection regressions fail against
the previous code; all 77 affected tests and 751 Engine NativeLua tests passed after the fix.
This establishes the selection contract, not the cause of the file-target transition failure.

The subsequent SDK [#99](https://github.com/CheatEngineNet/CheatEngine.SDK/pull/99) and Client
[#69](https://github.com/CheatEngineNet/CheatEngine.Client/pull/69) guards are merged after passing CI.
The source dependencies now pin SDK main `7c5b681a4d8055129d7d51b28931e2f6c6d87361` and Client main
`5c0d3057c040920665579ca3b0f464f3cd865317`. A known file target is refused before process selection;
Client reports `unsupported` with `not_started` and invalidates stale target leases. SDK's 83 focused cases
and all 1,223 Release Engine cases passed; Client's 65 focused cases and full 1,657-case Core suite passed.
The old implementations fail the new refusal regressions. This contains the unsafe transition; it does
not restore CE's native memory callbacks. Exact-bundle native verification remains pending.

The next fixture batch adds separate x64 scenarios for four fixed C/ABI calls and a locally built native
DLL/PDB, plus a dedicated advancing debugger thread with published instruction addresses. Review requires
an exact one-byte step boundary and a nonempty stack containing the fixed native call's known return address.
The combined Release build, **5,647** portable tests and **732** standalone Lua tests pass with no failures or skips.
Reports are `native-fixtures-portable-3` and `native-fixtures-native-lua-2` under
`artifacts/test-results/ai-analysis-20261019`. These are prepared
cases; native success requires a complete passing session and its independent cleanup receipts.
There are now **178** prepared backend mappings. The DLL/PDB builder passed a real local MSVC preflight;
its source lives under the existing live-target project so the repository project-layout rule remains intact.

The next correction batch passed its Release build with zero warnings or errors, **5,608** portable cases,
**722** standalone Lua cases and all five contract snapshots, with zero failures or skips. Parsed TRX result
counts agree with each runner summary. Reports are `parallel-followup-portable-1`,
`parallel-followup-native-lua-1` and `parallel-followup-goldens-1` under
`artifacts/test-results/ai-analysis-20261019`.

Debugger status now retains its validated core state when CE cannot supply its advisory breakpoint flag.
`reportedBroken` is optional and is omitted when unavailable; explicit `true` and `false` are preserved.
Clients caching the old required-field schema should refresh the tool catalog. `broken` still requires the
independent context check, including register-global restoration, and execution guards retain their strict checks.
Independent Astra review closed the debugger findings. Native qualification of the replacement bytes is pending.

Package 11 (`2460bfa198cb05d5134846f759b8fdf06a982a55`) passed debugger session
`20261019T154118Z-82ff` and managed-injection session `20261019T154346Z-c862`. Both restored user settings,
preserved installed files and reported no cleanup failures. The debugger report verifies seven native tool cases;
its status, context, stack, break, continue and detach assertions use the owned target. Injection returned the
fixed payload's expected value and verified owned compiler-file cleanup. These results describe this package only.

| Package 11 file | SHA-256 |
| --- | --- |
| `CheatEngine.Mcp.dll` | `432E41DEDC8C5C08BDEA0843009B9342FCB2A6C3F547AD0DE594EA7CA5456F67` |
| `CheatEngine.Mcp.Gateway.exe` | `4F22475239BA9F39C1B17541DE9D5088711D7F53AA1B8AB4129ABFF153AA052E` |

The package 11 workflow `20261019T154209Z-2a27` reached signature generation but its fixture dereferenced an
omitted match count before checking whether generation succeeded. It now retains the raw response and fails
explicitly when the chosen address has no unique signature. The assembler session `20261019T154458Z-2fee`
found CE's additional terminal newline; the two fixed input scripts now include that newline without relaxing
content comparisons. Both failed sessions restored settings and preserved installed files.

The disabled-compiler session `20261019T154553Z-ab0b` passed. The enabled session `20261019T154610Z-703f`
compiled and exported the expected DLL, then crashed in the SDK's outer `LuaFrame.Dispose`/`lua_settop` path.
It returned `instance_unavailable` with unknown host effect. Both owned hosts and targets stopped, user settings
were restored and installed files were unchanged, but the crashed host's disabled-menu evidence is missing, so
the session has a cleanup-evidence failure and supplies no verified native success cases. Native runs were
suspended until the SDK cleanup candidate below was prepared and reviewed; a passing earlier package does not close this regression.

The follow-up SDK candidate `c934aec483c6c5cfe7a24112d9c3218ba4a1629b`
([PR #97](https://github.com/CheatEngineNet/CheatEngine.SDK/pull/97)) releases synchronized callbacks on the
already admitted Lua state. It removes a second provider acquisition during cleanup. The three new regression
cases fail against the previous implementation and pass with the fix; all six focused cases, 136 hosting tests,
363 Lua tests and 746 engine NativeLua tests passed with zero failures or skips. Independent Astra review closed
the bounded patch. These results establish the cleanup invariant, not the cause of the native access violation.

MCP's corresponding Release build passed with zero warnings or errors, all **5,608** portable cases and **722**
standalone Lua cases passed, and the managed-object fixture is integrated with four prepared evidence mappings.
It publishes a pinned object's direct reference and known fields from the owned target, checks module identity,
object values, search results and exact structure layout, then verifies job and structure cleanup. There are
**164** prepared backend mappings; preparation does not establish native success.

Package 12 (`eaa89d411069ab6919b10aa3297105c1c4046262`) passed the compiler pair
`20261019T161750Z-f007` / `20261019T161807Z-9d25`, smoke session `20261019T162310Z-674e`,
managed injection `20261019T162347Z-73ed` and assembler session `20261019T162840Z-f817`.
All restored user state, preserved installed files and reported no sandbox cleanup failures. The assembler
session used corrected fixture assertions with unchanged product binaries. The corresponding reports are
`managed-fixture-{compiler,smoke,injection}-report-12` and `managed-fixture-assembler-report-12b` under
`artifacts/test-results/ai-analysis-20261019`. These bounded passing cases do not establish that the intermittent
native access violation is resolved.

| Package 12 file | SHA-256 |
| --- | --- |
| `CheatEngine.Mcp.dll` | `0A1B36991DC4AAD41E3B3A793E7E9D7AE3A291EA4671768650FAFD3E62E0FE4F` |
| `CheatEngine.Mcp.Gateway.exe` | `0885F6064B8AE1606A0A15718E9B3F65211645177C9F3AFEEC418B0C5AA72497` |

The initial managed-object run `20261019T161918Z-43f2` exposed an incorrect structure-count expectation:
CE adds a `Vtable` pointer at offset zero before the three known instance fields. The corrected oracle requires
exactly that header, the target pointer width and all three exact field offsets/types/sizes; 13 focused tests
cover acceptance and malformed or incomplete layouts. The workflow `20261019T162751Z-e779` passed those
managed-object assertions and verified job/structure cleanup, then failed the file-target restoration readback.
Opening and saving the private 4,096-byte file preserved its exact contents, but reattaching reported the original
PID while reading its known memory value failed. No file-tool or partial-workflow qualification is claimed.

The initial hook run `20261019T162218Z-fc7e` incorrectly required a standalone syntax check to accept generated
source. Generation is now qualified by the complete expected template, independently decoded instructions,
exact owned addresses/rollback bytes, unchanged target memory and verified allocation cleanup. The separate
DISABLE check returned `Access violation` at named `dealloc`, consistent with the cached CE implementation's access
to absent ENABLE-time allocation information; the host remained running. This observation does not qualify hook application. The complete corrected assembler
session above passed its independent apply/release and rollback cases.

Debugger session `20261019T162423Z-8f1b` attached and detached cleanly but did not observe the requested thread
stop within ten seconds. The fixture selected an arbitrary matching thread; CE documents that a sleeping thread
may need to wake before the request takes effect. A dedicated advancing thread fixture is being prepared.
This failed session supplies no native debugger qualification for package 12. All failed sessions above restored
settings and preserved installed files; their sandbox cleanup lists were empty.

Package 10 (`4401986963a7a765b579f3003e06a454672ea707`) passed compiler sessions
`20261019T143232Z-9eaa` and `20261019T143249Z-e5e9`, then smoke session `20261019T143707Z-7128`.
All three restored user settings, preserved installed files and reported no sandbox cleanup failures.
The compiler case verified the fixed exported PE and owned-file cleanup; the smoke case verified isolation
and its existing bounded operations. Their evidence remains separate from package 9 below.

| Package 10 file | SHA-256 |
| --- | --- |
| `CheatEngine.Mcp.dll` | `827D12EF42F72D0375A8A2C7AB2CFEBFE8177A06E73D1B408DA4A0DB2E9EDFDD` |
| `CheatEngine.Mcp.Gateway.exe` | `1BBE58D33385CD96159C3DFD449EECC2CCD7402860588017D7749EF527475B19` |

The new workflows (`20261019T143059Z-8e25` and `20261019T144940Z-f28d`) failed a resource fixture assertion.
The allocation descriptor legitimately omits its optional process ID, and default-false flags may also be omitted.
The fixture must establish target identity independently and validate required allocation fields without assuming
optional fields are present. The assembler run (`20261019T143415Z-29fc`) reached record-script readback,
then failed because CE normalized LF to CRLF. Its correction normalizes only those line endings.
Managed injection (`20261019T143328Z-3b5f`) refused because the private host had disabled CE's required
`DotNetInject.lua`; setup now needs that exact reviewed script for this scenario. The debugger run
(`20261019T143539Z-9adb`) attached successfully but returned invalid status on immediate readback and cleanup
readback. The diagnostic rerun (`20261019T144757Z-c2b2`) showed that the independent context query succeeded,
but CE's advisory `debug_isBroken` query returned a non-Boolean value. A narrow correction is being verified:
preserve strict core status validation and omit an unavailable advisory value. All these sessions stopped their
private hosts and targets and restored settings. Failed sessions do not qualify their partial tool receipts.

The next parallel fixture batch passed its combined Release build with zero warnings or errors, all **5,594**
portable cases and all **714** standalone Lua cases, with no failures or skips. Reports are
`parallel-checklist-portable-1` and `parallel-checklist-native-lua-2` under
`artifacts/test-results/ai-analysis-20261019`. The first Lua run caught an incorrect test expectation:
Lua validation can prevent the native open while the dispatcher correctly reports that Lua execution started.
The corrected case also proves the native open was never called.

This batch fixes file-target handling: CE's unsigned sentinel is preserved, address expressions resolve before
opening the file, and a failed open cannot reuse an earlier file target's sentinel as proof of success.
Separate resource/scanner/structure, file, assembler and debugger fixtures have reviewed cleanup and evidence
mappings. Their corrected live runs are pending; the mappings alone do not qualify these tools.

The latest x64 workflow, `20261019T135210Z-8cfc`, passed on package
`artifacts/dist/roadmap-source-20261019-9`, including the six utility/runtime/assembly/symbol cases and target,
gateway and host restarts. Its report verifies **130** backend native success cases, all **207** routing cases
and all **206** positive adapter mappings. The report is
`artifacts/test-results/ai-analysis-20261019/source-workflow-x64-report-9/tool-results.json`.
The summary also records the owned target executable and managed assembly hashes. User state was restored,
the installed files were unchanged and cleanup reported no failures.

| Package 9 file | SHA-256 |
| --- | --- |
| `CheatEngine.Mcp.dll` | `89082402AADA29AFE3D8BBD400102B3A46F9341724233552D93E8E474B0F6919` |
| `CheatEngine.Mcp.Gateway.exe` | `2B5E37A2E66310EAD87651D927DE70FC4A06439F29D6CC8310BB4D9D40FB2E6E` |

The x86 run `20261019T135014Z-b9af` passed pointer-map export and the six added cases, then failed the module-file
oracle: Windows' cross-width process enumeration reported `System32` for the 32-bit module that CE resolved to
`SysWOW64`. Cleanup and restoration passed. The fixture now resolves only direct System32 module paths to the
physical x86 system directory, retaining strict base-address, path, PE-machine and export comparisons.
Its corrected x86 run is pending. A failed run's partial receipts are not counted as qualification.

The following package 8 results remain evidence for their own binary hashes:

The x64 workflow `20261019T131741Z-b4d5` passed with the source-built Client and SDK, followed by smoke session
`20261019T132216Z-47db`. Both restored user state, preserved the installed CE files and reported no cleanup failures.
The workflow verified target, gateway and host restarts, including the persisted notebook after restart. Its seven
.NET collector receipts match independently parsed metadata from the owned managed assembly. The smoke session
verified debugger status without an attached debugger, speed state and scanner lifecycle; it does not qualify
register preservation while a debugger is stopped.

Fresh reports verify routing for all **207** advertised names and positive adapter evidence for all **206** backend
tools. The workflow supplies **124** native mappings and smoke supplies **5** different mappings on the same package,
for **129** backend tools with reviewed native success cases. The remaining **77** backend tools have no verified
native success case on this package. These are individual cases, not all behavior partitions. All four optional gates stayed disabled
and no same-process plugin reload was requested. `nativeQualificationComplete` remains false.

Reports are in `artifacts/test-results/ai-analysis-20261019/source-workflow-x64-report-8` and
`source-smoke-x64-report-8`, using the 5,560-case portable and 705-case standalone Lua reports below. Package
`artifacts/dist/roadmap-source-20261019-8` passed its isolated plugin-load and Native AOT gateway checks:

| File | SHA-256 |
| --- | --- |
| `CheatEngine.Mcp.dll` | `4C840290E380F1F138F30FDAB88E8B48F647EAD482542C633D0E4C8AC6C20BA7` |
| `CheatEngine.Mcp.Gateway.exe` | `325D761C275FE3F79F59A9D79675CA11A6291BAFEE330D8568A3B56B74F92A28` |

The corresponding x86 workflow `20261019T132136Z-5c93` failed at pointer-map export: CE reported 64-bit WOW64
support modules alongside the 32-bit target modules, and capture retained those out-of-width ranges. The export
validator correctly refused that map. This failed session restored user state, preserved the installation and
reported no cleanup failures; its earlier receipts do not qualify the complete x86 workflow. The capture projection
now retains only complete module ranges representable by the selected pointer width, while preserving validation
of genuinely invalid persisted maps. The new regressions cover actual x86/x64 capture, the inclusive address
boundary and invalid export metadata before any output bytes are written.

The same follow-up adds explicit symbol-preference clearing with `modules=[], replace=true`, so a previously empty
list can be restored. Null lists and empty prepend requests still refuse before dispatch. Six further live cases
are prepared for utilities, overview, assembly and symbol preferences; restoration is admitted before mutation and
checks the exact original list. Assembly cleanup verifies completed release and subsequent `not_found`.

The follow-up Release build and scoped formatting checks passed. All **5,566** portable tests and **706** standalone
Lua tests passed with zero failures or skips, recorded in `pointer-width-portable-1` and `pointer-width-native-lua-1`
under the same artifact directory. The public contract snapshot diff contains only the intended explicit-clear
description and updated help-document sizes. Independent Astra review found no remaining actionable findings.
The six added cases passed in the package 9 x64 session above; complete x86 qualification remains pending.

## Source dependency verification from 2026-10-19

The initial source verification pinned Client `feaf14a0fad0bbbb869bbe4badb547304aa81d8f` and SDK
`06c529f84e47295e3ca7a4e81ca808b9c4686b3a`. Client's source-reference support was merged through its PR #67,
with its portable, package-consumption and CI checks passing. MCP's normal and AOT lockfiles resolve these
dependencies as projects. Tests verify the dependency identity, Release compilation and the direct SDK reference
boundary; CI initializes both submodules recursively.

The portable Release suite passed **5,559** cases and standalone Lua passed **665**, with no failures or skips.
The reports are in `artifacts/test-results/ai-analysis-20261019/roadmap-record-delete-portable` and
`roadmap-record-delete-native-lua`. Commit `ef37e32` also passed the isolated single-DLL load probe and Native AOT
gateway smoke check in `artifacts/dist/roadmap-source-20261019-4`.

The expanded private x64 workflow found that this CE host has no `AddressList.clear()` method. The fix preflights
all records, then destroys them through their current IDs; regression tests cover nested records, callback-driven
deletion, callback-created survivors, empty lists, refusal and bounded error details. The subsequent live run
`20261019T111119Z-887f` verified its cleanup, but stopped at a separate SDK defect: assigning a null parent reports
success without moving a record out of its group. User state was restored, the source installation was unchanged
and cleanup reported no failures. This failed session does not qualify the complete workflow or its prepared
native evidence mappings.

The parent-assignment correction uses Client `9b836e6ced8e97b4506dc8523998c7c2081cc2ad` (merged main)
and SDK `09dad6c15782d9eee38f013d834d432d79e6623e` (PR #96 merged after all CI gates passed).
Both submodules now use their merged `main` commits. The local results below were run on the SDK's equivalent
pre-squash candidate `c4480e6c7fa24b58a6b495a378f11cdca042673c`.
SDK now resolves the child again and verifies its parent ID after the setter returns. Nested root moves are refused
before mutation because CE has no supported detach operation; an already-root record is a verified no-op.
The protected getter tests cover errors and malformed parent IDs before and after assignment, without fabricated
runtime resets. All **746** Engine NativeLua cases passed; independent Astra review found no remaining defects.

MCP's corresponding Release suite passed **5,559** cases and standalone Lua passed **665**, with zero failures or skips.
Reports are under `artifacts/test-results/ai-analysis-20261019/roadmap-parent-portable-3` and
`roadmap-parent-native-lua-2`. Eight additional native evidence mappings bring the prepared total to **122**.
Five are specific to the smoke scenario; the resource and job mappings use the expanded workflows. Each mutable
case requires independent readback and cleanup evidence. These new mappings still require passing native sessions.

Private x64 run `20261019T121114Z-6c36` verified the record-parent correction and completed the file/table operations,
then stopped because `process_set_paused` treated CE's opaque unattached-debugger breakpoint value as a stopped
target. The harness restored user state, left the source installation unchanged and reported no sandbox cleanup
failures; the fixture's pause cleanup also encountered that false refusal. This failed run does not qualify the
complete workflow.

The corrected pause/resume, execution, Mono and .NET structure guards check debugger attachment first and require
a boolean breakpoint result only when attached. They preserve prepared register globals and retain a tracked pause
when resume cannot be verified. Independent Astra review found no remaining findings in this fix. The clean Release
rebuild passed with zero warnings/errors; **5,559** portable cases and **683** standalone Lua cases passed with no
failures/skips. Their parsed reports are in `debugger-state-portable-1` and `debugger-state-native-lua-1` under the
same artifact directory.

Private x64 run `20261019T123934Z-d8c3` used the merged source dependencies and passed pause/resume, code analysis,
module exports and code dissection. It stopped at the function-range fixture: that assertion incorrectly required
an exact function entry and exclusive end, although CE documents an estimate. The API now describes its raw stop
boundary and the fixture checks a bounded estimate against the owned instruction layout. The run restored user
state, left the source installation unchanged and reported no cleanup failures. This failed session does not
qualify the complete workflow.

The corrected function estimate passed in private x64 run `20261019T125424Z-afb6`, including dissection cleanup.
The next failure was in the .NET metadata fixture before any collector call: its expected `Program` type omitted
the owned target's namespace. The fixture now validates the fully qualified type and method metadata before host
startup, with a portable reflection cross-check. User-state restoration and the installation fingerprint passed;
cleanup reported no failures and no owned processes remained. This session remains failed, so its receipts are
observations rather than full-workflow qualification.

Review also found that the speedhack guards and debugger status query used `debug_getContext(false)` without
preserving prepared register globals. A separate bounded context reader now requires a local x86/x64 debugger
interface and restores the known register write set after every result, including malformed returns and exceptions.
Regression cases preserve absent, false and object-valued globals while allowing unrelated callback changes to
survive. Status retains the distinction between context availability and `debug_isBroken`. All **705** standalone
Lua cases and **5,560** portable Release cases passed with no failures/skips; independent Astra review found no
remaining issues in this patch. The clean Release rebuild had zero warnings/errors. Parsed reports are in
`context-preservation-native-lua-2` and `context-preservation-portable-2` under the same artifact directory.
These tests use CE API doubles. An attached-debugger native session is still required to qualify preservation
in the live host.

## Roadmap verification from 2026-10-19

The expanded roadmap build passed with zero warnings or errors. The portable Release suite passed 5,551 tests,
and standalone Lua 5.3 passed 658 tests, with zero failures or skips. The report verifies routing for all 207
advertised names and positive adapter evidence for all 206 backend tools. Eleven seeded fuzz tests exercise
1,792 generated cases, including valid and malformed offline IL2CPP dumps.

Fresh combined coverage reaches **21,597 / 22,890 handwritten product C# lines (94.35%)**. The union excludes
generated files and tests. Line hits do not prove every decision outcome, embedded Lua branch or native host behavior.
The private distribution passed its isolated plugin-load probe and Native AOT gateway smoke check.

These results are retained under `artifacts/test-results/ai-analysis-20261019/roadmap-portable-release-2`,
`roadmap-native-lua-1`, `roadmap-tool-report-portable-1`, and `roadmap-product-coverage-1.json`.
The expanded native fixtures are being reviewed and run separately; a prepared live mapping is not a passing result.
The inspection baseline below remains historical evidence for its own bundle.

## Inspection additions from 2026-10-19

The catalog adds `memory_find_strings` and `memory_read_vtable`. The existing `pointer_read_chains` gains optional
completed-hop traces while preserving its compact default and read budget. The new portable adapter cases assert
Unicode decoding, byte addresses, paging, bounded previews, pointer widths, ordered slots, per-slot errors and
target-change rejection. MCP and gateway cases preserve wide addresses and distinguish null pointers from failed
reads. Updated contract snapshots and positive adapter mappings include both new tools.

The verified inspection baseline passed 5,387 portable tests in both Debug and Release and 651 standalone NativeLua
tests, with no failures or skips. Nine deterministic fuzz tests cover 1,600 generated cases across values, pointer
files, Unicode memory inspection, virtual tables and pointer chains. The .NET SDK and CI use 10.0.402; the matching
bundle passed the isolated plugin-load probe and Native AOT gateway smoke check.

Private x64 run `20261019T073555Z-2c66` and x86 run `20261019T074249Z-5578` passed with two owned targets and two
private CE copies. Both restored user state, preserved the source installation and reported no cleanup failures.
The x86 fixture was corrected to select the main module by an independently observed base address because CE can
report duplicate identical module names under WOW64; the earlier failed run remains diagnostic evidence only.

Each architecture's report verifies 196 routing cases, 195 positive adapter mappings and 62 native success mappings.
The remaining 133 backend tools are unverified for native success. The sessions include target/gateway/host restarts,
but zero plugin reload cycles and no enabled optional capability gates.

The package hashes were:

| File | SHA-256 |
| --- | --- |
| `CheatEngine.Mcp.dll` | `1969546DF46C548D4AFB4408886493F538C5FCF9458D9E3DF36ECBBF41C71047` |
| `CheatEngine.Mcp.Gateway.exe` | `ADA1A445E21D79E354EBBA842B13822C662E83946194B23BD1574B30E533687D` |

Further roadmap implementation is underway. These receipts qualify the inspection baseline only; new changes need
fresh builds and reports. Earlier coverage percentages below retain their original measurement date.

## Automated results from 2026-10-16

The dependency refresh uses the published NuGet packages `CheatEngine.Client` **1.1.0** and
`CheatEngine.SDK` **2.0.1**. Both normal and AOT lockfiles were regenerated. Debug and Release builds
completed with zero warnings or errors. The isolated bundled-DLL reflection/load probe and the published
Native AOT gateway smoke check also passed.

Fresh final TRX reports contain:

| Suite | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| Portable Release | 5,314 | 0 | 0 |
| Portable Debug | 5,314 | 0 | 0 |
| Standalone NativeLua | 651 | 0 | 0 |
| Opt-in live Workflows session | 1 | 0 | 0 |

The generated report verifies routing for all **194** advertised tools, positive adapter evidence for
all **193** backend tools, and the exact required parameterized cases. The added
`AllToolBehaviorEvidenceTests` derives its names from the production catalog and checks that every
adapter mapping references an existing public test, source file, oracle and case list. This structural
guard does not execute the mapped test or prove its outcome; the report checks the fresh TRX results.
The report records input hashes but does not independently attest that a supplied TRX came from current source.

Additional tests pin execution argument types and numeric limits, indexed validation errors, timeout
endpoints and symbol-lookup failure semantics. Conversion/calculator tests cover byte order, fixed scalar
values, operator precedence, invalid expressions and resource limits. Pointer-file tests cover schema
and snapshot validation, incomplete coverage, chunked nonseekable reads, stream ownership and failures
before writes. These checks use deterministic inputs and result, error or side-effect assertions.

Combined portable and standalone Lua coverage reaches **20,193 / 21,369 handwritten product C# lines
(94.50%)**. Hits are unioned by source file and line; generated files and test code are excluded.
This percentage does not measure embedded Lua branch coverage or prove native host behavior.

| Product project | Covered / instrumented lines | Line coverage |
| --- | ---: | ---: |
| Core | 4,357 / 4,581 | 95.11% |
| Gateway entry point | 9 / 10 | 90.00% |
| Hosting | 1,228 / 1,348 | 91.10% |
| Plugin | 794 / 851 | 93.30% |
| Prompts | 704 / 709 | 99.29% |
| Resources | 222 / 222 | 100.00% |
| Tools | 12,879 / 13,648 | 94.37% |

The separate static source-to-test pairing analyzer could not parse this repository's C# 14 extension
blocks with its bundled Roslyn 4.14. Its partial classifications are not used as coverage evidence.

The current private x64 run `20261016T174241Z-e914` passed the eight additional checks, six bounded
workflows and one restart each of target A, gateway and host A. All four optional gates were explicitly
disabled, and zero plugin reload cycles were requested. The session passed in about 37 seconds, restored
user state, left the source installation unchanged and reported no cleanup failures. The runner also
confirmed that no recorded owned processes remained. Its 813 prepared inputs matched their manifest.

The report verifies **37 backend tools** against this run's exact primary and cleanup receipts.
The other **156 backend tools** still lack native success evidence; gateway-local `instance_list` has
its separate discovery tests. Same-process plugin reload and the native fixtures listed above remain
unqualified. `nativeQualificationComplete` remains false.

Current private bundle SHA-256 values:

- DLL: `8C01D51F78642FF036E4616032A541D5DE2BD534006D9FF51C8C97B81FC33AE8`
- Gateway: `664EB1CD2756ED076C443446A6D09A151CEB30CA43BB69BC2392555C3BEFD2E3`

Fresh TRX and Cobertura files are under `artifacts/test-results/all-tools-20261016/` in
`portable-reviewed`, `portable-debug-reviewed`, `native-reviewed` and `live`. The per-tool report is
`tool-report-final/tool-results.json`, and the deduplicated line totals are in
`product-coverage-final.json`. These local artifacts and raw native logs are not committed.
Installed and released binaries have not been replaced by this work.

## Automated results from 2026-10-11

The final portable suites passed in both Debug and Release: 5,046 tests each, zero failures or skips.
The final standalone NativeLua suite passed 651 tests, zero failures or skips. The report generated from
the Release and NativeLua TRX files verifies positive adapter evidence for all 193 backend tools.
It also verifies routing for all backend tools and the gateway's own discovery cases.

[`live-evidence.json`](live-evidence.json) maps 37 positive native tool cases from the reviewed workflow
session below. Each case requires its primary receipt and any mapped cleanup receipt, along with a passed
session, restored user state, an unchanged source installation and no cleanup failures. These 37 cases
do not establish native qualification of the entire catalog.

## Failure observed on 2026-10-11

The ordinary x64 lifecycle session used the published `2.0.0-beta.3+059f249cc3293dff65b19ca257ecfe196d20aac1`
plugin and gateway, with all four optional gates disabled. Reading the second instance's runtime resource
failed with `The Lua stack does not contain the inputs required by the protected operation.`
The stack trace reaches `LuaProtectedApi.PushBytes`, `LuaState.TryGetGlobal` and
`MainThreadDispatcher.Dispatch` before the MCP operation body starts. This identifies the failure
boundary; it does not prove its underlying cause. No automatic native retry was made.

The private run is `20261011T134351Z-8024`. Its final summary reports `passed: false`,
`userStateRestored: true`, `sourceInstallationUnchanged: true` and no cleanup failures.
The eight added live cases were not reached in that run. Raw logs and discovery material stay
in the private run directory; they are not committed.

The `code_get_comments` fix is currently a source change. The installed/released beta.3 bundle has not
been replaced by this work. The regression checks two ordered addresses through the actual managed
adapter and real Lua 5.3 with CE API doubles, avoiding the array-shape bypass in the previous compile-only case.

## Later native results from 2026-10-11

The ordinary SDK candidate first reached the new checks in `20261011T144953Z-1ce8`, exposing an incorrect
test assumption that a target selected before plugin activation must have a positive selection epoch.
Client legitimately starts at zero; the test now checks a nonnegative epoch and verifies that setting the
same pointer size preserves it.

Run `20261011T145414Z-f9a6` then found a production bug: `process_list_threads` read `list.Strings[i]`,
but the installed CE reference documents `list[i]` (or `list.String[i]`). The previous double supplied
the unsupported property. The fix uses `list[i]`, and regression doubles expose only numeric entries.
All 637 standalone Lua tests passed after that fix; further adapter additions bring the final total to 651.

Candidate 2 ran all eight added checks and six workflows successfully in `20261011T150242Z-86a0`,
then failed at the separate Plugin Manager bridge: its checklist remained empty. The earlier experiment
that retained the empty `Plugins64` key failed the same way. Installed CE 7.7 internals are not available;
we have no supported Lua workaround for host-driven disable/re-enable. The exact plugin-row guard stays intact.

The dedicated `workflows` scenario passed in `20261011T151957Z-8cfc` in 33.5 seconds. It explicitly
stages all four optional gates disabled, checks the eight tools and six workflows, restarts target A,
restarts the gateway, then restarts host A while checking host B and rejecting stale identities. It records
zero plugin cycles. Its final summary reports `passed: true`, `userStateRestored: true`,
`sourceInstallationUnchanged: true` and an empty cleanup-failure list. No recorded owned processes remained.

That private candidate uses SDK `2.0.1-mcpqualification.1` with the provider guards below. Its bundled DLL
and AOT gateway passed isolated catalog/load probes and gateway smoke checks. DLL SHA-256:
`F0DA5938E8CABE34C7BED9CCEE16FFE92C5731D9E37A9F2546835576DA594D68`; gateway SHA-256:
`E8547E93BF97BE9D0EFA07E225A27A4A6B41000454834CBF7B03A7FD10664C44`.
The private runner verified 930 input hashes before launch. This candidate is neither installed nor released.

Its ordinary native bridge uses the checked-in SDK binary (`889DC4C2...`), whereas the diagnostic package
used the released CI-built binary (`B008C8D8...`). Their recorded native source/build fingerprints match;
the binaries are not byte-identical. PE and package audits passed. Successful private runs do not establish
that the original released-bundle dispatcher failure is resolved.

## SDK investigation from 2026-10-11

The SDK source now refuses a provider state with a negative stack top before stamp verification or dispatch.
It also verifies that stamp checking preserves the stack top. Both failures release the operation admission
through the existing exception path; they do not reset the stack or retry the provider. These checks contain
an invalid state. They do not fix or explain the original corruption.

Standalone SDK evidence: 278 native-Lua tests passed, 14 main-thread/coroutine tests passed, and the focused
provider/runtime run passed 14 tests. The rooted-coroutine fixture models the published CE 7.5 topology;
it does not establish the internals of the installed CE 7.7 host.

The private diagnostic package `CheatEngine.SDK.2.0.1-mcpprovidertrace.2` was used in
`20261011T144032Z-1764`. The bounded resource/memory/scanner/AOB replay passed, restored user state,
left the installation unchanged and stopped all recorded owned processes. Host A recorded 3,593 trace
events and host B recorded 266. All 397 provider-returned states had nonnegative tops and stamp checking
preserved them. Neither trace froze, overflowed nor contained an incomplete record. The failure did not
reproduce; this is not root-cause resolution or full native qualification.

An earlier candidate replay, `20261011T143815Z-b0c8`, also passed and restored state, but no trace was
created because the private harness had not forwarded the trace opt-in to its sanitized child environment.
That harness omission was corrected and reviewed before the recorded replay. Neither replay reached the
eight additional lifecycle checks. Raw traces, host logs and discovery material remain private.

Client's dispatcher delegates to synchronous SDK invocation. No Client production change is supported by
the observed failure. Installed and released binaries remain unchanged.

## Reproduce the evidence report

Build and run the repository's portable and NativeLua categories as documented in
`tests/CheatEngine.Mcp.Tests/README.md`, with TRX reports enabled. Then run:

```powershell
./eng/Write-AllToolQualificationReport.ps1 `
  -GatewayTrx artifacts/test-results/all-tools-20261016/portable-reviewed/CheatEngine.Mcp.Tests_net10.0_x64.trx `
  -PortableTrx artifacts/test-results/all-tools-20261016/portable-reviewed/CheatEngine.Mcp.Tests_net10.0_x64.trx `
  -NativeLuaTrx artifacts/test-results/all-tools-20261016/native-reviewed/CheatEngine.Mcp.Tests_net10.0_x64.trx `
  -OutputDirectory artifacts/test-results/all-tools-20261016/tool-report-final
```

The output is `tool-results.json` inside that output directory. Supply `-LiveSummary` with the private
session's `summary.json` to include its outcome. A receipt from a failed session is marked
`observed_in_failed_session`, never `verified`. Missing or duplicate routing cases leave their row
incomplete. `nativeQualificationComplete` stays false until the full per-tool native acceptance matrix
is implemented and reviewed; this report does not infer qualification from suite counts.
