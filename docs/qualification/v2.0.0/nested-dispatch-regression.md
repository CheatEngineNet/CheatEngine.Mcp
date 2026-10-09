# Standalone nested-dispatch regression

Status: Release build, hash preflight and independent Astra preparation review complete. The user approved one execution, and both standalone native-Lua cases passed.

## Purpose and limits

The approved instrumented CE session `20261009T111321Z-688b` observed balanced stacks but no synchronized callback inside its three short AOB waits. Repeating that workload would not guarantee the suspected nesting. This preparation creates deterministic nesting in the SDK's existing standalone Lua test host.

It exercises the production `MainThreadDispatcher`, protected Lua calls, callback lifetime and `LuaFrame` restoration. It does not use `DispatchOverrideForTests`. The two cases cover a successful nested callback and a nested managed exception while the outer callback remains inside a protected host-wait call.

This can test the SDK's stack restoration under the simulated contract. It cannot qualify CE's actual scheduler, reproduce the historical native crash, establish its cause, or demonstrate a production fix.

## Isolated inputs

- SDK source archive: commit `325c47b573f8bd39a247f1d0101f110fa36c1696`, ZIP SHA-256 `CAC8BF7218249088597E18011A3333AB30A360872A9A917AFA3B7D7DCA1DF30C`.
- Private source directory: `artifacts/dependencies/sdk-reentry-tests`. It is an archive without its own Git metadata; the parent MCP checkout's HEAD is not its source revision.
- Production SDK source remains the uninstrumented baseline. Changes are limited to two new test/helper files and staging the original published native bridge.
- Published bridge SHA-256: `B008C8D8C136187F241542E6223DC0831999D8300DC2C4C01E1CF49F6FBA7698`.
- Staged Lua library: `artifacts/native-lua-reentry/lua53-64.dll`, SHA-256 `C95DCDFA0F60F97B43D970D77FD1BB907AF4DE04B500A3C89A99600B20B35BD2`.
- Build identity: `2.0.1-mcpreentrytest.1`, explicit SDK source commit and disabled parent Git source queries.

The earlier instrumented source trees, packages and raw run evidence are preserved separately.

## Required assertions

1. Both workers use distinct coroutines of the same Lua global state. Their queued callbacks execute on their captured worker states on the main OS thread, while the SDK's provider returns the main root state.
2. Callback order is exactly `outer-enter`, `wait-enter`, `inner-enter`, `inner-exit`, `wait-exit`, `outer-exit`.
3. Nested SDK work encounters the root host-wait sentinel at a nonzero stack height, changes that stack inside a frame and restores it on both success and exception.
4. Both worker stacks retain their original sentinel values and heights after dispatch returns. Main-thread dispatch flags are reset after the outer callback and on both joined workers.
5. The exact nested exception reaches the inner caller, while the outer caller continues successfully.

## Process and lifetime boundary

Only one OS thread may access the shared Lua global state at a time. Both workers must reach the native synchronize stand-in and block before the main thread pumps either callback. Completion of a callback does not release its worker's native epilogue. After the entire outer callback unwinds, the main thread releases and joins one worker before releasing the other.

Lua states, coroutine references and wait handles remain alive until all workers exit. A failure that leaves a worker active must prevent another theory case or teardown from using those states. The external runner gives the isolated test process a 60-second execution deadline and terminates only that owned process if necessary.

## Prepared runner

`artifacts/issue-edits/Invoke-ReentryLuaTests.ps1` defaults to hash verification without execution. Its explicit `-Execute` mode invokes only `CheatEngine.SDK.Hosting.Tests.Threading.MainThreadReentrancyTests` with the `NativeLua` trait, fails skips, and requires exactly two executed and passed cases in the TRX report. It uses the staged Lua DLL, removes the trace opt-in variable, records console/results and applies bounded cleanup after runner errors.

`Save-ReentryTestInputs.ps1` checks the extracted sources against the pinned archive, rejects unexpected source changes, and records hashes for the test output tree, source tree, runner, Lua library and .NET host. The final manifest is created only after the source and Release build are complete.

No Cheat Engine process, target process, debugger, compiler/injection workflow or capability gate is used. The previous one-session instrumented CE approval was consumed. The user then separately approved one execution of these exact standalone cases; that execution is complete. No additional native execution is scheduled.

## Completed offline evidence - 2026-10-09

- Locked restore passed. Release build of `tests/CheatEngine.SDK.Hosting.Tests/CheatEngine.SDK.Hosting.Tests.csproj` passed with zero warnings/errors after correcting the new files' compile and style diagnostics. Initial failures are retained in `goal-reentry-test-build.log` and `goal-reentry-test-build-2.log`; the successful result is `goal-reentry-test-build-3.log`.
- Source comparison checked 1,277 entries. Exactly two new test/helper files and the original published native bridge differ from the archive; no existing SDK production source changed.
- No-execution runner preflight verified all 1,485 input hashes. The source and output copies of the native bridge both match the published bridge hash above.
- Test module SHA-256: `81F3DCABC50555F27FC9799CB94A73AAE231416447F8424BCAE5334E3E56E0A0`.
- Input manifest SHA-256: `8F921108C1B48C8891903C94AD20E9B5F8BE6996451AE63DA7135BB9C2630A29`.
- Installed runtime inventory records .NET x64 10.0.12; the SDK is pinned to 10.0.401. `dotnet --list-runtimes` passed. `dotnet --info` printed the identities but exited nonzero because sandbox access to Service Control Manager was denied; that command is not recorded as a successful check.
- Independent Astra high review found no remaining actionable findings in the prepared source, callback serialization, lifetime/quarantine paths, runner and exact input hashes. It verified the output bridge identity and successful build. It did not execute the cases.

The exact build command, from the private SDK directory:

```powershell
dotnet build tests/CheatEngine.SDK.Hosting.Tests/CheatEngine.SDK.Hosting.Tests.csproj -c Release --no-restore -p:MinVerVersionOverride=2.0.1-mcpreentrytest.1 -p:EnableSourceControlManagerQueries=false -p:RepositoryCommit=325c47b573f8bd39a247f1d0101f110fa36c1696 -p:SourceRevisionId=325c47b573f8bd39a247f1d0101f110fa36c1696 -v:minimal
```

The approved execution command, run once from the MCP workspace:

```powershell
& artifacts/issue-edits/Invoke-ReentryLuaTests.ps1 -Execute
```

| Requirement | Evidence |
| --- | --- |
| Deterministic nested success and managed exception cases compile | `MainThreadReentrancyTests.Nested_synchronized_dispatch_preserves_worker_and_root_stacks(false/true)`; successful Release build and reviewed source |
| Exact inputs and isolated runner are prepared | 1,485-hash preflight; source/output bridge match; Astra scoped clear |
| Both native cases pass | Exact false/true cases passed in the approved run below; no real-CE qualification |

## Approved standalone execution

Run directory: `artifacts/reentry-lua-results/885121c89ef846fead7b76804b21e5f3`. The single approved runner invocation completed with **Passed: 2, Failed: 0, Skipped: 0**, total duration 237 ms. The TRX names identify both `innerThrows: False` and `innerThrows: True` cases; both passed.

The owned test process (PID 46208) exited with code 0, without timeout, runner failure or cleanup failure. A follow-up process check found that PID absent. All 1,485 prepared input hashes still matched after execution.

- Receipt SHA-256: `A99389CBB5DE27B418688FF3252A215CCCAF1D8473073B3F9B76E172ABCA7A11`.
- TRX SHA-256: `3B0C076521EDC02D36636319990259A4FC33ADB5C11A49143E78E940A532488D`.
- Evidence extract: `artifacts/issue-edits/goal-reentry-test-execution-check.json`.

This result demonstrates balanced restoration and correct exception ownership for the two deterministic simulated-host paths. The earlier CE trace contains no synchronized reentry, so these passing standalone cases do not connect the historical CE failure to this mechanism, rule out other schedules, or demonstrate a production fix. The original crash and native qualification gates remain open.

Independent Astra high final review confirmed both exact TRX cases, console/receipt agreement, raw evidence hashes and all 1,485 input hashes, with no remaining actionable findings in that scope. Preparation, approved execution and evidence review were checked off in #28 and #33; exact body readback confirmed both issues remain open. #31 remains open unchanged.
