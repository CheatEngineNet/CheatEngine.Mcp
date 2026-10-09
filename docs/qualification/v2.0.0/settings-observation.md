# Settings reload observations

Status: historical unexecuted preparation, superseded by the [MCP-only closeout](mcp-closeout.md). The earlier Release build, two 859-file preflights and Astra review passed. Neither the ten standalone cases nor the proposed live session was authorized or executed; integration supersedes the primary-checkout inputs, so future runs require fresh preparation and review.

## Evidence and purpose

The [retained-key experiment](lifecycle-key-fix.md) failed with a measured zero-row checklist. Its registry change was rolled back after the source premise was disproved. The ordinary product distribution remains unchanged; there is no demonstrated fix for the empty list or historical SDK stack failure.

Pinned CE's reload returns early for a missing form or a runtime class other than `TformSettings`, and its broad exception handler can swallow an error before checklist population. `getSettingsForm()` wraps the existing form rather than creating it. The [object binding](https://github.com/cheat-engine/cheat-engine/blob/ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37/Cheat%20Engine/LuaObject.pas#L91-L96) exposes the runtime class through the read-only [ClassName property](https://github.com/cheat-engine/cheat-engine/blob/ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37/Cheat%20Engine/LuaObject.pas#L123-L132).

The fixed bridge now reads that class and the checklist count before reload. A row-count refusal adds both observations beside the existing post-reload count. Class text is capped at 128 characters, and the complete existing error receipt remains ASCII-sanitized and capped at 1,024 characters. These observations do not change settings, populate rows, reload the plugin, or weaken the exact one-row/name guard. The rest of the existing lifecycle scenario is unchanged.

The new evidence can identify a class mismatch and distinguish a list already empty before reload from a changed list. It cannot by itself prove that the reload completed, identify a swallowed exception, or establish why an unchanged zero-row list is empty. Matching the expected class only rules out that one pinned-source guard; installed binary/source equivalence is still unproven. A passing lifecycle attempt would not demonstrate that these extra reads fixed anything.

The additional source review confirmed that normal Settings opening and its page-change handler provide no independent checklist-refresh operation. The guard preserves non-plugin user settings and both private hosts share that guarded CE settings tree. No specific mismatched registry value has been identified as the cause. No internal flags, native addresses, user settings or checklist objects are modified by this observation patch.

## Prepared sequence

1. Run the ten exact `NativeLuaToolRuntimeTests.LivePluginBridge_*` cases in one owned process with the same staged Lua DLL and 60-second deadline. The two new `LivePluginBridge_ReloadChangesRows_ReportsObservedClassAndBeforeAfterCounts` theory cases assert independently observed pre/post counts and class text, refusal before key messages/Settings apply, and unchanged enabled state. Existing eight cases retain their earlier scope. Require ten passed, zero failed/skipped before continuing.
2. If all ten pass and the complete sequence is approved, run one maintained x64 `lifecycle` session with the unchanged ordinary SDK package. Keep the same two private CE copies, owned targets, original prelude/six workflow groups, three plugin cycles and target/gateway/host restarts described in [lifecycle-resume.md](lifecycle-resume.md#exact-proposed-workload). All four optional gates remain false; no compiler/injection acknowledgement is set. Stop for evidence review on failure; no automatic retry.

This preserves the known failing ordering. Its incremental purpose is the bounded Settings observation, not a new product change. Existing settings backup/restoration, refusal of pre-existing user CE/DebugView processes, owned-process cleanup and post-run checks remain in force. No debugger, private SDK trace or additional native entry point is introduced.

The Release test-project build passed with zero warnings/errors (`artifacts/issue-edits/goal-settings-observation-build.log`). Both no-execution preflights verified 859 files including all 527 unchanged product sources and the original distribution. Native tests have not been run under the earlier stop; this prepared sequence requires a new execution decision because previous approvals are consumed.

| Prepared input | SHA-256 |
| --- | --- |
| Test module | `F767C901F32DA587DFB04723EE0339D9181BB88CCC3C66355BA842B35A7F1411` |
| Standalone manifest | `B2AAA114D396827219BEA835EB1A4C66536DA176F113388545438C7D2C3F628F` |
| Lifecycle manifest | `529ADA4C471080611FE176033EBC5C9EDBEE2A3A0D3C6CF00467C47605CE34BA` |
| Ordinary plugin | `1620DD5978C4996BA80DEB5B81403EB3B695B3D029C1D1D533A3F39254D5A97F` |
| Ordinary gateway | `ADB97797600614CEF21C7F5E2352F95E9102A3D4DFCE48DA4F5B19335E65CA88` |

Prepared commands, only after approval:

```powershell
& artifacts/issue-edits/Invoke-SettingsObservationNativeTests.ps1 -Execute
# Proceed only after verifying all ten exact cases passed.
& artifacts/issue-edits/Invoke-SettingsObservation.ps1 -Execute
```

These runs cannot satisfy the complete requirements of #28, #31 or #33. Final clean-candidate checks, the broader native matrix, compiler/injection qualification, installation/client/environment cases, soak and publication remain separate requirements.

Independent Astra high review found no actionable issues in the read-only observations, exact row/name guards, bounded errors, two new cases, both manifests or conditional runners. It specifically confirmed that an expected class with unchanged zero rows remains ambiguous. Source whitespace and runner syntax checks passed. No new native pass is claimed.
