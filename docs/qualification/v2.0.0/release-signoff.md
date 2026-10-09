# Stable v2.0.0 release sign-off

Status: Not approved for release. The real-CE Lua dispatch crash remains unresolved; passing offline and standalone Lua checks do not qualify the production host.

The maintainer subsequently requested an [MCP-only implementation closeout](mcp-closeout.md), administrative issue closure and a tested local commit to `main`. The original stable-release criteria below remain unmet; issue closure no longer represents stable sign-off.

This document is the durable progress and evidence index for the stable v2.0.0 program.
Unchecked items remain incomplete even when related source code exists.
Issue #28 checkboxes must be updated only after the corresponding evidence is reviewed.

## Critical path

- [ ] Review and approve the support matrix, acceptance criteria, release-stopping defects, and evidence rules.
- [x] Create focused implementation and qualification issues with owners, dependencies, required evidence levels, and roadmap requirement identifiers: #29 through #33 in the v2.0.0 milestone.
- [x] Run the local working candidate through the existing dynamic Cheat Engine 7.7.1.10828 fingerprint and reviewed-autorun checks; the final clean candidate must repeat them.
- [x] Add disposable x86 and x64 fixture coverage required by the target architecture promise.
- [x] Make the packaged-DLL probe compare the exact packaged plugin against the explicit candidate golden directory.
- [x] Complete `pointer_get_access_info` implementation, V+, applicable VN, documentation, and supplied-facts gateway/plugin qualification; live debugger capture remains a separate open workflow.
- [x] Complete `pointer_read_chains` implementation, V+, documentation, and x86 and x64 native fixture qualification.
- [x] Complete the `compileCS` feasibility check under a reviewed policy; see the exact environment and remaining native cases in [compiler-checkpoint.md](compiler-checkpoint.md).
- [ ] Complete `exec_compile_csharp` implementation and qualification or record an explicit reviewed deferral with a linked follow-up.
- [ ] Reconcile and freeze the final tools, schemas, resources, prompts, completions, routing, capability gates, and migration contract.
- [ ] Diagnose the production Lua dispatch failure, demonstrate a fix without timing-altering instrumentation, and repeat the failed native workflow/lifecycle cases on the corrected candidate.
- [ ] Complete lifecycle, recovery, workflow-domain, installation, upgrade, rollback, and supported-client qualification.
- [ ] Record reproducible performance baselines and approve the measured-relative thresholds.
- [ ] Complete the planned two-hour soak against the exact RC package.
- [ ] Build and qualify the exact RC commit and package.
- [ ] Set the final version to `2.0.0` in committed source and rebuild from the clean final commit.
- [ ] Repeat the required final-source and final-package checks after the version change.
- [ ] Create the annotated `v2.0.0` tag at the exact binary source commit.
- [ ] Upload the ZIP and `SHA256SUMS.txt` as a draft and verify remote identity, hashes, sizes, and notes.
- [ ] Publish with `draft=false` and `prerelease=false` only after review.
- [ ] Download and verify the public package and smoke-test the shipped installation and first-use path.

## Evidence ledger

Working-tree evidence is recorded in [local-checkpoint.md](local-checkpoint.md), including package hashes, environment, test counts, two native run identifiers, cleanup, and limitations.
It does not establish a clean final stable candidate.
Compiler-only evidence for a clean local candidate is recorded in [compiler-checkpoint.md](compiler-checkpoint.md).
Subsequent failed native runs, the unresolved production crash, and passing working-tree checks are recorded in [remaining-checkpoint.md](remaining-checkpoint.md). The separately approved standalone and real-CE sessions below have completed; their one-session approvals are consumed. The [Settings observation sequence](settings-observation.md) and [five standalone navigation cases](navigation-native-regression.md) are historical unexecuted preparations, superseded by the MCP-only closeout. Future execution requires fresh reviewed inputs and authorization under the earlier native-work stop. This ledger does not authorize further native, debugger, extended compiler/injection, soak or release execution.
The exact candidate gates below refer to the final stable candidate and remain open.

| Evidence ID | Requirements | Level | Candidate | Environment | Outcome | Cleanup | Safe reference | Status |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| LOCAL-01 through LOCAL-08 | Sections 3-6 and bounded portions of 7, 8 and 11 | implementation, V+, VN, AOT, VL | Historical uncommitted `codex/roadmap-v2`, beta.2 version; exact artifact hashes in checkpoint | Windows 11 Pro 26300; CE 7.7.1.10828; .NET 10.0.12; x86/x64 targets | All recorded checks passed | Both native runs restored user state and preserved the source installation with no cleanup failures | [Local checkpoint](local-checkpoint.md) | pass within recorded scope |
| COMPILER-01 and COMPILER-02 | Section 6 feasibility and compiler-only phases 1-4; bounded section 11 checks | V+, VN, AOT, VL | Clean `808a626e65eddab113737a27a7c2345555b1c241`, beta.2, Release; exact artifact hashes in checkpoint | Windows 11 Pro 26300; CE 7.7.1.10828 x64; .NET 10.0.12 and Framework 4.8.09221; x86/x64 targets | Build/package checks and all approved compiler-only phases passed | Four sessions restored user state and preserved installation; compiler files removed; no cleanup failures | [Compiler checkpoint](compiler-checkpoint.md) | pass within recorded scope; no injection or stable release |
| LIFECYCLE-X64-01 | Sections 7-9 | VL | Clean `d3984574e0d0757beebd3ea36deb6f9bef7358d1`, beta.2, Release; hashes in checkpoint | Windows x64; exact Windows build/runtime patch not emitted; CE 7.7.1.10828 x64; owned x64 targets | Memory, named scan and AOB subcases passed, then allocation refused with a Lua stack-input failure and unknown host effect | Owned hosts/targets stopped; user state restored and source preserved; no reported cleanup failures | [Native lifecycle result](remaining-checkpoint.md#native-lifecycle-and-workflow-result) | failed; not lifecycle-wide qualification |
| DIAGNOSTIC-X64-01 | Production crash investigation | diagnostic VL | `207d6fdb62c4874185ba843f00add85da1438d1f`, beta.2, Release; hashes in checkpoint | Windows x64; exact Windows build/runtime patch not emitted; CE 7.7.1.10828 x64; owned x64 targets | Host A crashed with `0xC0000005` in the Lua stack/dispatch path during AOB work; no root cause or fix established | User state/source preservation recorded and owned processes stopped; host A lifecycle-indicator cleanup evidence failed | [Diagnostic candidate](remaining-checkpoint.md#diagnostic-candidate-207d6fd) | failed; release blocker remains |
| OFFLINE-CONTRACT2 | Bounded sections 3 and 11 | portable, standalone VN, packaged reflection, AOT and local archive checks | Uncommitted working tree on `0f722ef7d6b37eee0985073dfe594c6559b31c14`, beta.2; Debug/Release binary and ZIP hashes plus source manifest in checkpoint | Windows x64; standalone Lua 5.3 DLL identity recorded; no CE host launched | 4,135 portable cases per configuration; 626 standalone Release NativeLua cases; actual packaged catalogs/AOT snapshots and same-input ZIP reproduction passed; independent Astra scoped review clear | Exact test-output executable restoration and staged-file hashes verified; no native host cleanup claimed | [Offline package refresh](remaining-checkpoint.md#offline-package-refresh---2026-10-09), [standalone NativeLua](remaining-checkpoint.md#authorized-standalone-nativelua-regressions---2026-10-09) | pass only within stated working-tree scope; clean candidate/native/CI/install/soak/release pending |
| DIAGNOSTIC-X64-02 | Bounded production crash investigation | diagnostic VL | Same uncommitted Release distribution as OFFLINE-CONTRACT2; ordinary SDK; exact package and harness hashes in checkpoint evidence | Windows 11 Pro x64 10.0.26300; CE 7.7.1.10828 with executable hash recorded; loaded CLR patch and target-binary hash not measured | Two memory/named-scan/AOB sessions passed; a third added the exact post-AOB 64-byte non-executable allocation/free probe; a fourth restored the original read-only prelude too; all passed with four optional gates false | Each run restored user state, preserved source installation and reported zero cleanup failures; all recorded host/target PIDs absent afterward | [Authorized bounded investigation](remaining-checkpoint.md#authorized-bounded-ce-investigation---2026-10-09) | bounded passes only; original crash unresolved and full lifecycle not qualified |
| DIAGNOSTIC-X64-03 | Stack observations during bounded crash investigation | instrumented diagnostic VL | Private SDK `2.0.1-mcpstacktrace.1`, MCP `2.0.0-mcpstacktrace.1`, modified source manifests and exact hashes in diagnostic ledger; original published native bridge retained | CE 7.7.1.10828 x64, owned x64 targets; prepared inputs hashed | One approved session passed; 1,874 records, 76 paired dispatches and 79 balanced frame restorations; no recorded refusal, underflow, overflow or incomplete trace; three short AOB waits contained no traced callback reentry | User state restored, source installation unchanged, zero cleanup failures and recorded owned PIDs absent | [Instrumented session](stack-trace-diagnostic.md#approved-instrumented-session) | balanced instrumented baseline only; historical crash and reentry hypothesis unresolved |
| DIAGNOSTIC-LUA-04 | Deterministic nested dispatcher stack restoration | standalone diagnostic VN | SDK archive `325c47b573f8bd39a247f1d0101f110fa36c1696` plus two new tests; private Release identity `2.0.1-mcpreentrytest.1`; production source unchanged; exact hashes in ledger | Windows x64, .NET 10.0.12, staged Lua 5.3 DLL and published native bridge identified by SHA-256 | Both selected nested success/managed-exception cases passed; 2 passed, 0 failed, 0 skipped | Owned test process exited 0 and absent afterward; no timeout or cleanup failure; all 1,485 prepared input hashes unchanged | [Standalone nested-dispatch execution](nested-dispatch-regression.md#approved-standalone-execution) | simulated SDK behavior verified only; no real-CE crash reproduction or production fix |
| DIAGNOSTIC-X64-05 | Ordinary-SDK full lifecycle attempt | incomplete VL | Same uncommitted ordinary Release distribution as OFFLINE-CONTRACT2; 820 prepared hashes matched before/after | CE 7.7.1.10828 x64, two private hosts and owned x64 targets | All six workflow groups completed; first plugin-disable bridge refused because its checklist did not contain exactly one owned row; 0 passed, 1 failed, 0 skipped | User state restored, source unchanged, no cleanup failures, all four recorded owned host/target PIDs absent | [Lifecycle failure](lifecycle-resume.md#approved-execution-result) | bridge failure reproduced; plugin cycles/restarts unqualified; historical stack failure unresolved |
| DIAGNOSTIC-X64-06 | Conditional bridge regression and retained-key lifecycle experiment | standalone VN and incomplete VL | Same ordinary Release distribution as OFFLINE-CONTRACT2; both 859-file manifests matched before/after execution | Windows x64 test module, staged Lua 5.3 DLL, CE 7.7.1.10828 x64 and owned x64 targets | Eight standalone cases passed, then one lifecycle attempt completed all six workflows but refused the first disable with measured count zero; the key-needed source premise was subsequently disproved and that change rolled back | User state restored, source unchanged, no cleanup failures, all recorded owned processes absent | [Conditional execution](lifecycle-key-fix.md#approved-conditional-execution) | eight bridge cases verified; lifecycle still failed; no post-rollback native pass or historical crash resolution |

The following offline evidence belongs to isolated snapshots, not the primary product or a clean RC. The linked ledgers identify each snapshot, retained logs, test results, source inventory and independent Astra high review. Later snapshots include the earlier repairs, but the earlier focused results remain attributed to the source on which they ran.

| Evidence ID | Requirements | Level | Candidate | Environment | Outcome | Cleanup | Safe reference | Status |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| OFFLINE-PERFORMANCE-01 | Preparation for 9.01, 9.02 and bounded 9.04 | implementation and focused portable checks | Isolated 856-file snapshot on `0f722ef7d6b37eee0985073dfe594c6559b31c14`, uncommitted beta.2, Release; inventory and module hashes in ledger | Windows x64, managed test doubles; no CE host | Twelve-workload measurement/comparison preparation and pointer range-search retention repair reviewed; 20 final helper cases and 130 earlier unchanged pointer regressions passed | No target or CE state created | [Performance preparation](performance-preparation.md) | preparation verified; native baselines and performance acceptance not run |
| OFFLINE-ADMISSION-01 | Preparation for 9.03 and 9.05 | implementation and focused portable checks | Isolated 861-file snapshot on `0f722ef7d6b37eee0985073dfe594c6559b31c14`, uncommitted beta.2, Release; inventory and module hashes in ledger | Windows x64, managed test doubles; no CE host | Two-gateway observation/comparison preparation reviewed; all 52 selected cases passed | No target or CE state created | [Admission preparation](admission-preparation.md) | preparation verified; native concurrency/UI acceptance not run |
| OFFLINE-NAVIGATION-02 | Bounded sections 3 and 8; preparation for 9.05 and 9.07 | implementation, Release portable and catalog checks | Isolated 867-file follow-up on `0f722ef7d6b37eee0985073dfe594c6559b31c14`, uncommitted beta.2, Release; source/module/TRX hashes in ledger | Windows x64, managed test doubles; NativeLua and LiveQualification excluded | Prepared module/region/thread/symbol/breakpoint navigation, dispatch metadata, thread collector and bounded structure script reviewed; 213 focused, 38 catalog-capture and final 4,234 portable cases passed | No target or CE state created | [Navigation follow-up](navigation-followup.md) | portable scope passed; changed Lua scripts compiled only; primary integration and native acceptance pending |
| OFFLINE-NAVIGATION-PACKAGE-02 | Bounded sections 3 and 11.05 | packaged reflection, AOT smoke and local archive checks | Hash-matched copy of all 867 reviewed files on `0f722ef7d6b37eee0985073dfe594c6559b31c14`, uncommitted beta.2, Release; exact DLL/gateway/ZIP hashes in ledger | Windows x64; two isolated DLL load contexts and gateway with empty registry | Maintained locked publish, exact-DLL golden comparison, AOT smoke, two identical ZIPs and five-file staging passed | No CE host, target or installed deployment changed; local output retained | [Latest navigation package](navigation-package.md#latest-reviewed-follow-up-package) | local package scope passed; no Lua execution, final stable identity, installation or publication evidence |

The same reviewed navigation source also passed a separate Debug run:

| Evidence ID | Requirements | Level | Candidate | Environment | Outcome | Cleanup | Safe reference | Status |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| OFFLINE-NAVIGATION-DEBUG-02 | Debug counterpart to OFFLINE-NAVIGATION-02; bounded section 11 | locked restore, Debug build and portable checks | Hash-matched isolated copy of all 867 files on `0f722ef7d6b37eee0985073dfe594c6559b31c14`, uncommitted beta.2, Debug; module/log/TRX hashes in ledger | Windows x64; NativeLua and LiveQualification excluded | Build had zero warnings/errors; 4,234 passed, zero failures/skips; exact test names match the reviewed Release run | No CE host or target attachment; all three pending native preparation inventories unchanged | [Debug verification](navigation-followup.md#matching-debug-portable-verification) | portable scope passed; Astra high evidence review scoped clear; final stable and native gates remain open |

The candidate column must include version, full commit, clean-tree state, configuration, and relevant SHA-256 values.
The environment column must include exact native versions for VL, soak, installation, and release results.
The cleanup column must state whether owned state was released, user state was restored, and the source installation stayed unchanged.
The safe reference may be a CI URL, release URL, or redacted evidence file and must not expose secrets or personal paths.

## Exact candidate gates

- [ ] V passed with locked restore, Debug build, and portable tests excluding LiveQualification and NativeLua.
- [ ] V+ passed with both format checks, Release build, `eng/Publish.ps1`, and reviewed intended golden changes.
- [ ] The portable suite passed against both Debug and Release outputs with zero failures and zero skips.
- [ ] VN passed when applicable and recorded the selected real Lua 5.3 x64 DLL identity.
- [ ] AOT passed for the exact candidate gateway executable.
- [ ] The packaged lone plugin DLL matched the reviewed candidate tool, resource, template, and prompt goldens in isolated load contexts.
- [ ] VL passed on the exact candidate package with explicit maintainer authorization and all existing live restrictions.
- [ ] CI build, format, test, and analysis results were reviewed for release-stopping findings.
- [ ] Locked dependencies, high and critical advisory handling, embedded inventory, licenses, and notices were verified.
- [ ] The flat distribution and ZIP inventories, deterministic packaging promises, checksums, versions, and source identity were verified.

## Contract freeze

- [ ] Final public names and catalog order are reviewed.
- [ ] Input, result, error, and annotation shapes are reviewed.
- [ ] Reflection-disabled JSON and Native AOT metadata are complete.
- [ ] Gateway `instanceId` routing and stale-instance refusal are verified.
- [ ] Capability requirements reject before covered effects.
- [ ] File and table root defaults and configuration precedence are documented accurately.
- [ ] Tool map, workflows, prompts, resources, READMEs, and golden files agree with the candidate.
- [ ] Beta.2 migration and future compatibility policy are reviewed.

## Native and recovery sign-off

- [ ] Repeated enable, disable, and re-enable creates fresh activation identity, token, endpoint, discovery record, and Client state.
- [ ] CE shutdown, target exit and restart, client disconnect, gateway restart, and one-backend withdrawal preserve the other instance.
- [ ] Target transition guards and stale selection epochs prevent work against a replacement process.
- [ ] Owned allocations, patches, symbols, scanners, breakpoints, pauses, speed changes, jobs, and Mono attachment follow their documented cleanup paths.
- [ ] User-owned records, structures, breakpoints, symbols, tables, settings, and unrelated plugins remain intact.
- [ ] Failed cleanup retains honest orphan and recovery information.
- [ ] Cancellation, timeout, lost response, cursor replay, pagination, TTL, bounded buffers, and stop or dispose races have reviewed outcomes.
- [ ] Every advertised domain has a reviewed qualification classification, limitations, and recovery path.

## Performance and soak sign-off

- [ ] Three reproducible baseline runs record workload sizes, environment, and measurement method.
- [ ] Candidate p95 results meet the measured-relative thresholds in `acceptance.md`.
- [ ] Busy responses and dispatch concurrency do not starve Cheat Engine's main thread.
- [ ] Large jobs, completions, and live resources remain within configured bounds.
- [ ] The planned two-hour two-instance soak completes its required workload.
- [ ] Final handle and private-byte measurements meet the approved recovery thresholds.
- [ ] No crash, hang, cross-instance effect, stale-target effect, false success, cleanup failure, or leaked owned state remains.

## Installation, upgrade, and release sign-off

- [ ] A clean install from the exact candidate ZIP passes on the supported environment.
- [ ] Beta.2 and older folder deployment upgrade paths pass without mixing versions.
- [ ] Disable, uninstall, rollback, and locked-file recovery are reproducible.
- [ ] Paths with spaces, a standard user account, allowed roots, configuration failures, and endpoint conflicts have reviewed outcomes.
- [ ] Every named supported MCP client passes the documented first-use workflow.
- [ ] Stable release notes include support, migration, recovery, limitations, and changes since beta.2.
- [ ] The final stable commit repeats V+, Debug and Release portable tests, and applicable VN.
- [ ] The final Release package repeats AOT, packaged-DLL golden comparison, ZIP, checksum, and source-identity checks.
- [ ] The final Release package repeats clean install, upgrade, rollback, and maintainer-authorized two-instance VL.
- [ ] Retained RC evidence has a reviewed intervening diff and explicit applicability decision.
- [ ] The public stable assets download successfully and match the reviewed hashes and source identity.
- [ ] The shipped plugin discovery, gateway, and documented first-use workflow pass after publication.

## Final decision

- [ ] Every P0 requirement has complete reviewed evidence.
- [ ] Every P1 requirement is implemented or has an explicit reviewed deferral and linked follow-up.
- [ ] No release-stopping defect remains unresolved.
- [ ] The final candidate contract, package, support matrix, migration path, soak, and release evidence are approved.
- [ ] Issue #28 may be closed only after the public stable package is verified.
