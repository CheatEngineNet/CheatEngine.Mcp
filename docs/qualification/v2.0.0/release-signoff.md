# Stable v2.0.0 release sign-off

Status: Planned and not approved for release.

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
The exact candidate gates below refer to the final stable candidate and remain open.

| Evidence ID | Requirements | Level | Candidate | Environment | Outcome | Cleanup | Safe reference | Status |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| LOCAL-01 through LOCAL-08 | Sections 3-6 and bounded portions of 7, 8 and 11 | implementation, V+, VN, AOT, VL | Historical uncommitted `codex/roadmap-v2`, beta.2 version; exact artifact hashes in checkpoint | Windows 11 Pro 26300; CE 7.7.1.10828; .NET 10.0.12; x86/x64 targets | All recorded checks passed | Both native runs restored user state and preserved the source installation with no cleanup failures | [Local checkpoint](local-checkpoint.md) | pass within recorded scope |
| COMPILER-01 and COMPILER-02 | Section 6 feasibility and compiler-only phases 1-4; bounded section 11 checks | V+, VN, AOT, VL | Clean `808a626e65eddab113737a27a7c2345555b1c241`, beta.2, Release; exact artifact hashes in checkpoint | Windows 11 Pro 26300; CE 7.7.1.10828 x64; .NET 10.0.12 and Framework 4.8.09221; x86/x64 targets | Build/package checks and all approved compiler-only phases passed | Four sessions restored user state and preserved installation; compiler files removed; no cleanup failures | [Compiler checkpoint](compiler-checkpoint.md) | pass within recorded scope; no injection or stable release |

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
