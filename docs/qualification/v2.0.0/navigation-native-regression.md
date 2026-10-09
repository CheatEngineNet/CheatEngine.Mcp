# Prepared standalone navigation Lua regression

Status: historical unexecuted preparation, superseded by the [MCP-only closeout](mcp-closeout.md). The earlier runner/preflight/Astra preparation checks below passed, but no execution approval was received. Future runs require fresh reviewed inputs; do not reuse a pre-integration manifest against the integrated checkout.

The [reviewed navigation follow-up](navigation-followup.md) passed 4,234 portable tests and was [packaged locally](navigation-package.md#latest-reviewed-follow-up-package). Its changed thread and structure Lua bodies remain unexecuted. This preparation uses that exact reviewed Release test module in the isolated follow-up snapshot; it does not replace the frozen primary Settings test module or either pending Settings preparation.

## Exact proposed scope

Run these five facts from `CheatEngine.Mcp.Tests.NativeLua.NativeLuaToolRuntimeTests` once, in one owned process:

| Case | What it verifies |
| --- | --- |
| `ProcessV2_FixedEffects_ReturnObservedTypedHostState` | Hexadecimal thread IDs copied from the caller-owned StringList, successful destruction, and existing simulated create/open/save/pointer-size result contracts. |
| `ProcessV2_ThreadList_DestroysItsCallerOwnedStringListWhenCollectionFails` | The StringList is destroyed when the stubbed collector throws. |
| `StructureGet_StubbedElements_MapTypesAndReportMissingNames` | Detailed/concise element projection, paging, child metadata and case-sensitive missing-name refusal. |
| `StructureGet_BoundedDefinitionLookupAndPagedElements_AvoidWholeStructureTraversal` | Lookup stops at the matching definition and reads only indices 900/901 despite reported totals of 70,000 definitions and 100,000 elements. |
| `StructureGet_MissingDefinition_ReportsNotFoundWithinBoundAndLimitExceededPastIt` | A known-complete search reports not found; a search beyond the 65,536-definition cap reports limit exceeded. |

The ordinary embedded SDK bridge and the pinned standalone Lua 5.3 DLL execute the scripts against Lua table/function stubs. CE is not launched, a real process is not selected, and no debugger or compiler/injection workflow is used. The process fixture's create/open/save operations are simulated; its managed file-output check uses a unique file under the runner's private temporary directory and deletes its output afterward. The runner sets `TEMP` and `TMP` to that run's retained result directory, clears stack-trace instrumentation and golden-update variables, and leaves the user's ordinary environment unchanged.

The runner fails skips, checks the exact five TRX names and their passed outcomes, and requires five executed/passed with zero failed/not-executed. A 60-second deadline terminates only the test process it created; it waits for that process to exit before finishing console capture. Results and temporary artifacts are retained for review. There is no automatic retry or follow-on CE run, even if all five pass. These simulated-host results cannot prove installed CE ABI equivalence or complete native lifecycle/performance acceptance.

## Frozen inputs and command

The manifest verifies 1,088 files: all 867 reviewed snapshot sources, the complete test output file inventory, the Lua DLL, dotnet host, evidence and preparation scripts.

| Input | SHA-256 |
| --- | --- |
| Input manifest | `0BABAF0BB237A137C1EADA7EC05DA8A5395A8C2D443A774EBC917D5386031267` |
| Reviewed test module | `D7A21B4ADD7204DF3312473A13D22451041FFBE2339EE4FA12BF23D830D60697` |
| Lua library | `C95DCDFA0F60F97B43D970D77FD1BB907AF4DE04B500A3C89A99600B20B35BD2` |

The manifest and runner are under `artifacts/native-navigation-preparation/`. Preparation and preflight read/hash files without loading the test module or Lua DLL. The existing clean Release build and portable evidence are reused because no source/test code changed for this preparation. No new test discovery or native test run was performed.

Only after explicit approval:

```powershell
& artifacts/native-navigation-preparation/Invoke-NavigationLuaRegression.ps1 -Execute
```

The earlier native-work stop still applies. Previous one-session approvals are consumed; this proposal does not inherit them. The separate ten-case Settings bridge run followed conditionally by one ordinary-SDK lifecycle session remains pending and unchanged.

Final Astra review found no actionable preparation issues. It independently verified all 1,088 hashes (867 source and 216 test-output files plus the five preparation/runtime inputs), the exact five-case allowlist, outcome/count checks, owned-process deadline, temporary-file isolation and absence of a follow-on CE run. Both existing 859-file Settings inventories remain unchanged. No discovery or native execution was used in that review.
