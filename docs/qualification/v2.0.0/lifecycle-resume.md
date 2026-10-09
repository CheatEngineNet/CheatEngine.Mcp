# Ordinary-SDK lifecycle session preparation

Status: the user approved one session after offline preparation and Astra review. It executed and failed at the first plugin-disable step after all six workflow groups completed. Cleanup passed; no retry is authorized or scheduled.

## Why this is the next boundary

Four ordinary-SDK prefix sessions and one instrumented prefix session passed, and two deterministic standalone nested-dispatch cases also passed. They did not execute plugin disable/re-enable or the later restart matrix. The historical private `.2` session `20261008T015918Z-ee72` completed all six workflow groups before the fixed Settings/Plugins bridge refused its first disable request. The bridge's form-initialization and bounded error-receipt fixes have offline coverage but still need verification in CE.

The maintained `lifecycle` scenario covers that boundary using the current ordinary SDK package. A passing result could qualify this specific current-package workflow/lifecycle sequence. It would not establish the cause or fix of the earlier uninstrumented native stack failure, reproduce historical package bytes/timing, or qualify the stable release.

## Exact proposed workload

One x64 session starts two private CE copies and two owned x64 targets, using the existing guarded harness:

1. Attach both targets; verify runtime identities, scalar sentinels and all four disabled capability gates; run the original read-only resource prelude.
2. Run all six bounded workflow groups: memory, named scanner, AOB, control-flow graph inspection, modules and structures. Temporary allocations, snapshots, named scans and structures are owned and cleaned up by the existing harness. The CFG fixture writes bytes into owned non-executable memory for analysis; it does not execute target code.
3. Disable and re-enable plugin A three times. Require fresh activation identity/endpoint/token and stale-instance rejection, while B remains available and unchanged.
4. Restart A's owned target and reconnect; restart the owned gateway and verify both instances; restart private host A together with its owned target, reject the former instance and verify both current instances.
5. Close the owned processes, restore the snapshotted user settings, compare the source installation fingerprint, and retain the original error together with any cleanup error.

All optional gates remain false: `unsafeLua`, `autoAssembler`, `kernelAccess`, `targetCodeExecution`. No debugger attachment, compiler qualification, managed injection, kernel/DBVM workflow or soak is selected. No user process is closed to satisfy preflight; the harness refuses an existing CE or DebugView process.

Each private CE driver retains its normal five-minute lifetime, and each owned target its normal ten-minute lifetime. These are per-process bounds, not a promise that the whole restart scenario completes in five minutes. The runner lets the maintained harness perform ordered shutdown and settings restoration; it does not abruptly terminate the harness parent. Any failure ends this single attempt for review, without an automatic retry.

## Exact candidate and runner

- Distribution: `artifacts/dist/goal-contract2-release`, already used by the four passing ordinary-SDK prefix sessions.
- Plugin SHA-256: `1620DD5978C4996BA80DEB5B81403EB3B695B3D029C1D1D533A3F39254D5A97F`.
- AOT gateway SHA-256: `ADB97797600614CEF21C7F5E2352F95E9102A3D4DFCE48DA4F5B19335E65CA88`.
- CE 7.7.1.10828 x64 executable SHA-256: `CF6CCC664A90C23531EC20F5C41458CFDC1D04679D6D05ED94C06BCECF95544F`.
- Source: `0f722ef7d6b37eee0985073dfe594c6559b31c14` plus the reviewed uncommitted MCP changes. This is not a clean stable candidate or a published release.
- SDK: ordinary dependency; the private stack trace package is not selected, and the child process receives no stack-trace opt-in.

`artifacts/issue-edits/Prepare-LifecycleResume.ps1` verifies all 527 product-source hashes and the full Release distribution against the retained package manifest. It captures the current live-harness source, built test and target output trees, .NET host, CE executable and runner in `goal-lifecycle-resume-inputs.json`.

The root Release test-project build passed with zero warnings/errors (`goal-lifecycle-resume-build.log`). Script syntax and no-execution preflight passed for 820 exact files. No source or package change was required to select the existing scenario.

`Invoke-LifecycleResume.ps1` defaults to hash verification only. After explicit approval, its `-Execute` mode invokes exactly `CheatEngine.Mcp.Tests.LiveQualification.McpLiveQualificationTests` with the `LiveQualification` trait and fail-skips. It requires one executed/passed case, the complete three-cycle/three-restart receipt, disabled gates, unchanged package hashes, restored user state, unchanged installation, no cleanup failures and absence of recorded original/restarted host/target PIDs.

Proposed command from the workspace, only after approval:

```powershell
& artifacts/issue-edits/Invoke-LifecycleResume.ps1 -Execute
```

Independent Astra high review verified all 820 input hashes, the 527 product-source matches, exact scenario routing, original/restarted PID coverage, completion checks and preservation of the harness's restoration path. No remaining actionable findings were reported in that scope.

- Prepared manifest SHA-256: `7A20146763110D9A459F4C4CF0FAF065A4C7EDD471C32042F26063BBDCD9D06F`.
- Built test module SHA-256: `7F8404D131ABC76F3CE612FE5B181DD83706C44ED2193E5E2C7D7906D20E43F7`.

The user separately authorized this broader lifecycle run after review. That one-session authorization is consumed; further native execution requires a new decision.

## Approved execution result

Run `20261009T120128Z-8f44` completed the original prelude and all six workflow groups with the unchanged ordinary-SDK package. Memory, named scans, AOB, CFG, modules and structures each have their completion receipt; each mutating group's cleanup reported zero failures. CFG fixture bytes were not executed. All four optional gates were false.

The first `CyclePluginAsync("A")` failed when the Settings/Plugins bridge returned `expected exactly one owned plugin row`. The new bounded error-response path worked: the harness reported the precise refusal immediately, instead of waiting for a missing response. The earlier form-before-registry-reload change therefore did not resolve checklist population in real CE.

The test result is **Passed: 0, Failed: 1, Skipped: 0**, 18.441 seconds; the test process exited 2. No plugin reload or target/gateway/host restart pass is claimed. The six preceding workflow groups passing does not make the lifecycle test pass or demonstrate a fix for the historical native stack failure.

User state was restored, the source installation stayed unchanged, cleanup failures were empty, and all four recorded owned host/target PIDs were absent. All 820 prepared input hashes still matched after execution. The failed attempt is retained for source-based diagnosis; it has not been retried.

- Raw summary SHA-256: `FCAEA7CA2B06C37B854EF57B854FA511622B28D95EC9969BE761978609807594`.
- Runner receipt SHA-256: `80E6A139EC4E52D202ACEAE10E8A7ACA2AD5D5ECA2ABBDC62E6E918B0453A941`.
- TRX SHA-256: `0897C68743C4FC2A11922DF7DC872972915A8F7F05E1A04CE3F8184AF33FC87A`.
- Local result directory: `artifacts/lifecycle-resume-results/c244a91c720a41e88e3a71e59979a583`.
- Bounded evidence extract: `artifacts/issue-edits/goal-lifecycle-8f44-evidence.json`.
- Pre-edit bridge/lifecycle source snapshot: `artifacts/issue-edits/lifecycle-8f44-source/`.
