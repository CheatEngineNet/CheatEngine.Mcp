# Remaining prepared navigation routes

Status: the isolated implementation and evidence below passed Astra high review. These repairs are now integrated into the primary checkout for the [MCP closeout](mcp-closeout.md), whose verification is recorded separately. The earlier [local package](navigation-package.md#latest-reviewed-follow-up-package) includes these repairs; native CE qualification remains incomplete.

The snapshot is `artifacts/performance-preparation/navigation-followup-7adc921d5236434493cb15e1775855bf`, copied from the 863-file reviewed first navigation repair. The frozen ordinary package and both 859-file Settings preparations remain unchanged.

## Implementation and limits

Thread, registered-symbol and breakpoint resources now require a successful explicit preparation within five seconds. These reads use a copied bounded snapshot; they perform no native list enumeration. Thread and breakpoint snapshots require the same selected process/epoch; registered symbols remain global and available without a target. Symbol ownership is evaluated from current MCP leases. Breakpoint ownership describes the preparation-time observation; direct set/delete invalidates it, while other changes require an explicit refresh or expiry.

The thread collector now passes a caller-owned StringList to `getThreadlist`, parses CE's hexadecimal entries and destroys the list even after collection failure. Pinned CE expects that argument; it does not return a thread table. All three native producers enumerate their full collections before MCP's copy cap. The six explicit module/detail/region/thread/symbol/breakpoint preparations therefore declare `blocking_native`, with descriptions stating that CE can block for seconds.

Resources name their prepared helper through `McpSourceTool.PreparedProjection`, published as `cheatengine/preparedProjection`. Startup requires a unique public instance method on the same container, the source tool's exact result type, explicit `short` metadata, no tool annotation and no feature gate. Existing source-tool read-only, closed-world, ungated and catalog checks remain. Invalid declarations fail even for a short source. This metadata records the reviewed implementation promise; it cannot prove what a method executes.

`structure_get` now searches at most 65,536 indexed global definitions, then reads only the requested element page, at most 1,024 entries. A miss beyond the lookup cap is `limit_exceeded`; exact totals and pagination are preserved. Filtered value/compare operations keep their existing scripts. [Pinned CE's binding](https://github.com/cheat-engine/cheat-engine/blob/ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37/Cheat%20Engine/LuaStructure.pas#L23-L53) is index-only and returns entries in `DissectedStructs`. Its [structure declaration](https://github.com/cheat-engine/cheat-engine/blob/ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37/Cheat%20Engine/StructuresFrm2.pas#L133-L228) has no `Internal` property: the existing optional read defaults to false. The read description no longer promises a meaningful internal classification. Installed CE/source equivalence is unproven.

## Verification ledger

| Requirement | Evidence |
| --- | --- |
| Managed cache/projection behavior | 213 focused cases passed; final portable run passed 4,234/4,234, zero failures/skips. |
| Honest dispatch metadata and narrow projection validation | 38 catalog-capture cases passed; full portable run verifies without capture. Five golden files changed only intended descriptions, six dispatch classes, document sizes and six projection keys. Schemas, URIs and other metadata are unchanged. |
| Actual thread and structure Lua scripts | Regression cases compile with the test project; new native cases have not run. |
| Source and evidence review | Astra high found dispatch metadata, symbol publication ordering, the process test facade and an undefined source-class validation gap; all were repaired. Final source, golden and evidence reviews are scoped clear. |
| Earlier navigation package | Separately reviewed and scoped clear in [navigation-package.md](navigation-package.md); excludes these follow-up changes. |
| Latest follow-up package | All 867 reviewed files copied with matching hashes; maintained DLL probes, AOT smoke, deterministic ZIP and staging passed. Astra high package evidence review is scoped clear; see [the latest package](navigation-package.md#latest-reviewed-follow-up-package). |

The initial focused run passed 200/203 cases. Two test fixtures were corrected (the detached target requires three scalar checks, and the supplied structure page lacked its asserted next offset); the tool-map document was shortened to its existing budget. Symbol reads now publish within their admitted dispatch and use an ordering token so a delayed older copy cannot replace a newer publication. The native process fixture now supplies the stable process facade needed to reach Lua; this correction is not a native execution result.

The first broad run passed 4,233/4,234: a script-inventory assertion still expected 18 scripts after adding the nineteenth. That count was updated without weakening its per-script checks. The final Release build passed with zero warnings/errors; the complete portable rerun passed all 4,234 in 1 minute 46.069 seconds. Both broad results are retained. No native or live cases appear in the final TRX.

Commands executed from the isolated snapshot:

```powershell
dotnet build tests/CheatEngine.Mcp.Tests/CheatEngine.Mcp.Tests.csproj -c Release --no-restore -p:EnableSourceControlManagerQueries=false -p:RepositoryCommit=0f722ef7d6b37eee0985073dfe594c6559b31c14 -p:SourceRevisionId=0f722ef7d6b37eee0985073dfe594c6559b31c14 -v:minimal
dotnet artifacts/bin/CheatEngine.Mcp.Tests/release/CheatEngine.Mcp.Tests.dll --filter-not-trait Category=LiveQualification --filter-not-trait Category=NativeLua --fail-skips on --report-trx --results-directory artifacts/followup-portable-final-results
```

`artifacts/performance-preparation/navigation-followup-evidence.json` records all 867 source hashes (32 modified and four new files), build/module identity, final TRX, test names and log hashes. All 863 prior snapshot hashes still match. The base-commit product version identifies the source baseline, while the inventory identifies its uncommitted changes; it is not a clean stable candidate. Both no-execution Settings preflights again verified all 859 inputs, and their manifests are unchanged.

Native thread/structure regression execution, integration, live performance/admission/lifecycle qualification and final release evidence remain outstanding. The local development package still needs final stable source/version identity. No issue closure, commit, push or publication is claimed.

## Matching Debug portable verification

All 867 reviewed files were copied with matching hashes to `artifacts/candidate-preparation/debug-followup-a80bc6b812c14cbb8bb98a94804d80f5`. Locked restore and the Debug test-project build passed; the build reported zero warnings/errors. The Debug portable suite passed **4,234/4,234**, zero failures/skips, in 1 minute 42.351 seconds. Its exact test-name inventory matches the reviewed Release TRX. NativeLua and LiveQualification were excluded, and no golden-update or native opt-in environment variable was inherited.

The isolated copy and original reviewed source hashes still match. Both 859-file Settings inventories and the 1,088-file navigation native preparation also remain unchanged. `artifacts/candidate-preparation/debug-followup-evidence.json` records source/module/log/TRX hashes, run times and the comparison with Release; `Record-DebugFollowupEvidence.ps1` verifies these recorded conditions. Astra found that directly serializing the TRX timestamp XML element dropped its attributes; the recorder now writes their exact string values. Final independent Astra high evidence review is scoped clear, with no test rerun needed for that recording correction.

Commands executed from the Debug snapshot:

```powershell
dotnet restore tests/CheatEngine.Mcp.Tests/CheatEngine.Mcp.Tests.csproj --locked-mode -p:EnableSourceControlManagerQueries=false -p:RepositoryCommit=0f722ef7d6b37eee0985073dfe594c6559b31c14 -p:SourceRevisionId=0f722ef7d6b37eee0985073dfe594c6559b31c14 -v:minimal
dotnet build tests/CheatEngine.Mcp.Tests/CheatEngine.Mcp.Tests.csproj -c Debug --no-restore -p:EnableSourceControlManagerQueries=false -p:RepositoryCommit=0f722ef7d6b37eee0985073dfe594c6559b31c14 -p:SourceRevisionId=0f722ef7d6b37eee0985073dfe594c6559b31c14 -v:minimal
dotnet artifacts/bin/CheatEngine.Mcp.Tests/debug/CheatEngine.Mcp.Tests.dll --filter-not-trait Category=LiveQualification --filter-not-trait Category=NativeLua --fail-skips on --report-trx --results-directory artifacts/debug-portable-results
```

This adds Debug evidence for the same uncommitted development source. It does not satisfy final clean-candidate checks or execute the pending native regressions.
