# Settings plugin checklist key experiment

Status: the user authorized both stages conditionally. All eight standalone bridge cases passed; the one lifecycle attempt still failed at the first disable request with measured checklist count zero. Cleanup and independent Astra evidence review passed. Subsequent source review disproved the key-needed premise, and the unsupported registry change was rolled back. No further native run is authorized by this completed two-stage approval.

## Corrected source interpretation

The failed ordinary-SDK run `20261009T120128Z-8f44` reached the first disable request after all six workflow groups completed. Its refusal only established that the plugin checklist count was not one; the old message did not report whether it was zero or greater than one.

The original preparation incorrectly interpreted `FillCheckListBox` as being inside the `OpenKey(...\Plugins64, false)` block. Inspection of the exact pinned file shows that the `end` at line 1248 closes that block; the checklist call at 1250 is outside it. A missing plugin registry key does not itself skip that call. This corrects and supersedes the earlier causal claim. Both the source researcher and Astra confirmed the correction. See [the exact block boundary and outer exception handler](https://github.com/cheat-engine/cheat-engine/blob/ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37/Cheat%20Engine/MainUnit2.pas#L1214-L1261).

The reload has early exits for a missing form or a different runtime class, plus a broad empty exception handler that can swallow failures before checklist population. The [Lua plugin loader](https://github.com/cheat-engine/cheat-engine/blob/ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37/Cheat%20Engine/LuaHandler.pas#L9114-L9145) uses the same plugin handler as the [checklist implementation](https://github.com/cheat-engine/cheat-engine/blob/ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37/Cheat%20Engine/plugin.pas#L1694-L1714). A zero-row observation cannot distinguish those paths, a genuinely empty list, or installed-build differences. The actual cause remains unproven. Both the test process and CE are x64; a default registry-view mismatch has no supporting evidence here.

## Tested experiment, subsequently rolled back

- Open or create the empty `Plugins64` key only after the existing guarded user-state scope has been established.
- Retain all validation of subkeys, exact value names, paired types, enabled values and known private plugin paths before removing any existing values.
- Delete only the validated owned values, leaving the empty key available for CE's reload. The normal end-of-session settings restoration remains responsible for restoring the original registry state.
- Keep the exact one-row and exact plugin-name checks. A refusal now reports the observed count in the existing bounded ASCII receipt.
- Add the zero-row regression and assert count details for both zero and multiple rows. Refused requests still send no key messages and never apply Settings.

The experiment changed `LiveSandboxSession.Lifecycle.cs`, `LivePluginBridge.cs`, and `NativeLuaLivePluginBridgeTests.cs`. After the failed run and corrected source review, only the unsupported key creation/retention change was reverted to the prior validated registry handling. The useful observed-count error and zero-row regression remain. Product source, published plugin/gateway bytes, capability gates and the lifecycle scenario are unchanged. The earlier failed run's source, input manifest and runners are retained under `artifacts/issue-edits/lifecycle-8f44-source/`.

## Prepared verification sequence

1. Run only the eight `NativeLuaToolRuntimeTests.LivePluginBridge_*` cases using the staged Lua DLL in one isolated process. The runner has a 60-second deadline, fails skips and requires exactly eight passed cases. This checks the fixed bridge and error contract using the existing simulated host; it does not touch registry settings or launch CE.
2. Only if all eight pass, run one ordinary-SDK x64 `lifecycle` session with the exact workload in [lifecycle-resume.md](lifecycle-resume.md#exact-proposed-workload). This is the first real verification of the preserved-key correction and all downstream plugin cycles/restarts. The attempt ends for review on any failure; there is no automatic retry.

Both steps required a new explicit execution decision because the earlier one-session lifecycle approval had been consumed. The user subsequently approved this exact conditional sequence; the completed results follow below.

The Release test-project build passed with zero warnings/errors (`artifacts/issue-edits/goal-lifecycle-key-build.log`). Source whitespace and runner syntax passed. The two no-execution preflights each verified 859 exact files, including all 527 unchanged product sources and the retained ordinary Release distribution.

- Test module SHA-256: `6F43AD2E67629A39F1CACFC49B3F76ABA46D9BDBE05E33CA3476DF57A54C2630`.
- Standalone input manifest SHA-256: `9D74F0C5F926935C1265DBBE66784A8607FB9F2B3AEDDB1BF7EFF46E298B4A6E`.
- Lifecycle input manifest SHA-256: `72A30BC04C6544176346142AF9819A8209E518A6893C3CCB0B13470AF658711C`.

Historical commands executed once, in order after approval (their original manifests are retained and intentionally no longer match the reverted current source):

```powershell
& artifacts/issue-edits/Invoke-LifecycleKeyNativeTests.ps1 -Execute
# Proceed only after its eight-case result is verified as passing.
& artifacts/issue-edits/Invoke-LifecycleKeyFix.ps1 -Execute
```

Independent Astra high preparation review found no remaining actionable issues in ownership validation, retained-key semantics, exact row/name guards, the eight-case scope, both 859-hash manifests or runner cleanup. That source review did not establish that the correction would resolve the live failure.

That review missed the incorrect source-block interpretation above. Its clean result must not be used as support for the superseded key-needed explanation.

## Approved conditional execution

The standalone stage ran first in `artifacts/lifecycle-key-native-results/c23fb28679324287a65c9936e1e59ffe`: **8 passed, 0 failed, 0 skipped**, 1.027 seconds. The TRX contains the exact five refusal theory cases (including zero and multiple rows), disable, enable and bounded multibyte-error cases. The owned test process exited 0 with no timeout or runner/cleanup failure and was absent afterward. The simulated-host result does not validate registry behavior.

After verifying those eight outcomes, the single real-CE stage ran as `20261009T121623Z-71c7`: **0 passed, 1 failed, 0 skipped**, 17.852 seconds. The prelude and all six bounded workflow groups completed. The registry receipt reports `count=0, keyRetained=true`, but nonce 1's first disable request returned `expected exactly one owned plugin row (count=0)`. No plugin cycle or restart completed. Preserving the empty key was insufficient in this installed build; this evidence does not establish which registry path/view CE actually read or why its checklist remained empty.

All four optional gates remained false. CFG bytes were inspected without execution. Mutating-workflow cleanup failures were zero, user settings were restored, the source installation was unchanged, and all four recorded host/target PIDs plus both test-process PIDs were absent. Both 859-file input manifests still matched after execution, including the unchanged ordinary product package. Independent Astra high review confirmed the test order, exact outcomes, identities and cleanup with no actionable evidence findings.

Evidence: `artifacts/issue-edits/goal-lifecycle-key-execution-evidence.json`; live output is under `artifacts/lifecycle-key-results/380c1fe8ccf44d388c5a71294102af78`. The tested source, runners and manifests are retained under `artifacts/issue-edits/lifecycle-71c7-source/` before further edits.

| Artifact | SHA-256 |
| --- | --- |
| Standalone result | `336B110762908D68AB06BD3B93E63713E92D6451FF43838A4E4C2B40F638CC78` |
| Standalone TRX | `45B8406473F9FB3C278F228B43058482052D21D511A765FF8E1D896B5B215618` |
| Lifecycle result | `214CAFD53A1955C5954BB0E31D6455E250B6C48F69F7C9BD1882C47ABAE63CB8` |
| Lifecycle TRX | `A4CA3FDAF66FDAAD3EED73FC9AF3F12DC6E5DE95B4194073A1440EE1CBAACDF9` |
| Native summary | `FA635351E2C6AE070681CA4FCE007BF59D947D41F0DA86F3925CB0F84032FBB8` |
| Native receipts | `AB01C154F5C590126F4718DF7B973B241BD9C6C7BA5175B184BCBAC7AD29F6CD` |

This two-stage approval is consumed. Plugin lifecycle, the historical SDK stack failure and stable qualification remain unresolved. The registry rollback is a source correction, not a newly executed native pass. The next work is source-based diagnosis of observable reload boundaries; no automatic native retry follows this failure.

The post-rollback Release test-project build passed with zero warnings/errors (`artifacts/issue-edits/goal-lifecycle-key-rollback-build.log`), and `git diff --check` passed. All 527 product-source hashes and the ordinary distribution's plugin/gateway hashes still match the original package manifest. The original execution manifests remain unchanged as historical evidence; they no longer describe the current harness/test binary. Final review caught an empty consolidated `LiveCounters` field; it was corrected to the explicit numeric counters from the unchanged live TRX.

Final Astra review confirmed that correction with no remaining actionable findings. The checkpoint in #28 and #33 was replaced with the passed subcases, failed lifecycle result and retracted source rationale; exact normalized body readback succeeded. Both issues remain open. #31 was separately reread and remains open with its extended compiler/injection and final contract/CI gates unchanged.

## Next diagnostic limits from source

`getSettingsForm()` [wraps the existing global form](https://github.com/cheat-engine/cheat-engine/blob/ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37/Cheat%20Engine/LuaHandler.pas#L4657-L4662); it does not create or initialize it. The old form-before-reload ordering was not proof of form initialization. Lua's [object binding exposes `ClassName`](https://github.com/cheat-engine/cheat-engine/blob/ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37/Cheat%20Engine/LuaObject.pas#L126-L132), which could check the reload's exact-class guard. Existing checklist counts before/after reload are also observable.

However, `LoadingSettingsFromRegistry` is a public field, not a published RTTI property in [the Settings declaration](https://github.com/cheat-engine/cheat-engine/blob/ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37/Cheat%20Engine/formsettingsunit.pas#L360-L387). It must not be assumed readable through the ordinary Lua property binding. No plugin-array enumeration binding was established by the source review. A correct class plus zero rows both before and after reload would still leave a swallowed exception and an empty plugin list indistinguishable. These observations are an offline design boundary, not a prepared or authorized new session; a further run needs a probe that can discriminate the remaining paths.
