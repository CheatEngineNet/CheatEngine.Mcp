# Continuation checklist

Work resumed at the user's request on 2026-10-20. This checklist records **incomplete native qualification**;
delete it only when its remaining requirements have actually passed.

## Latest native checkpoint (current-27, 2026-10-21)

- [x] Keep the 17 report sessions pinned to exact bundle 06: `artifacts/dist/all-tool-final-20261020-06`,
  product `2.0.0-beta.4+1f987742b7fcd7760057a19508ddacbf2aad4bd8`, plugin SHA-256
  `E05A449551716AC485E2C5897397D109584B94CBEC3444FD8C7461432CF0462B`, gateway SHA-256
  `1FEEFE4975AE558C78932B63C93BAE7A97EB6DBA571C76402B87B63B0406467A`, and installed CE host SHA-256
  `CF6CCC664A90C23531EC20F5C41458CFDC1D04679D6D05ED94C06BCECF95544F`. No live-session evidence from a
  different product or gateway binary was pooled.
- [x] Generate `artifacts/all-tools-goal/all-tools-current-27/tool-results.json` from those 17 sessions and
  fresh normalized-checkout Release TRXs. It records **200 verified** and **7 not verified** rows across all
  207 advertised tools, with `nativeQualificationComplete=false`. Report SHA-256:
  `E5A8293487BCCF155C28DBB72311FC86832A9F00FD54901F22B0123AD50C2BCE`. The open rows are the six
  DBK/DBVM-dependent kernel operations and `symbol_enable_sources`.
- [x] Validate the current source checkout: Release portable **5,838/5,838**, Debug portable **5,838/5,838**,
  and NativeLua **766/766**, all with zero failures or skips. Release/Debug solution builds completed with zero
  warnings/errors. Both `dotnet format style` and `dotnet format whitespace` verification passed.
  TRX SHA-256 values: Release `FB0CFC401CA94A7AD242CAAD2B98E1E6B93D5B4C1E43C2515D5693001442DE0A`,
  Debug `2BAA55FFE972CAAA1B2382C33511A9F08C22C3EAE9142D4E0D80D25185321653`, NativeLua
  `23BAE4953CC35F2812BB126B83A7E41F97DC0E27C5486C1CAEBDC51105415A8A`.
- [x] Preserve the two historical failures as diagnostic questions, not native tool failures: the exact-bundle
  `structure_list` calls pass in workflows and state-workflows; all mapped debugger success/cleanup cases pass
  on bundle 06. That bundle includes the reviewed breakpoint-arity correction and deferred trace cleanup after
  the callback returns. The old provider `-101889` state and debugger monitor-overlap `SEHException` did not
  recur, but neither historical root cause is established. The separate provider trace remains diagnostic-only.
- [x] Keep plugin reload and the completed symbol/lifecycle cases bounded to their passing evidence. Three
  in-process plugin disable/enable cycles pass when the owned registry row is seeded before host startup.
  Explicit symbol reload and the local DLL/PDB fixture pass. The earlier unseeded zero-row observation does not
  establish a product regression.
- [x] Record no-VM kernel evidence: exact-bundle `kernel_get_status` reports DBK/DBVM inactive and an
  independent driver inventory finds no matching driver. This qualifies status/environment only; it does not
  qualify any of the six DBK/DBVM operations. No driver, service, boot, firmware, VBS or virtualization setting
  was changed.
- [x] Keep `symbol_enable_sources` unverified. The only local `ntdll.pdb` does not match the current image,
  and no safe independent effect oracle was established without risking public symbol downloads. No source
  enable call was made.
- [x] Normalize CRLF in the two test-only files flagged by whitespace verification. The audit at
  `artifacts/test-results/all-tool-qualification-20261021/line-ending-normalization-audit.json` (SHA-256
  `4E7C9F78571C2E6616F169E929C1C907380AC5797A2DECA4F49394FF5B7B140A`) proves their normalized source hashes
  are unchanged. `LiveCompilerCandidateTests.cs` changed from `72A3C085D66021A306CFC48F71C89BC1F252C680253608BF30E3BE0D4C4888BB`
  to `0D28E3480C9DDDA5BCF54739A8A3021F862FFD7B09B0B51685C5AD00C4446CE3`; `LivePluginRegistrySeedTests.cs`
  changed from `85F3064C9EFD34690694620C63EC2337DE1FC154CAE79DC7872B531D3A6E14F6` to
  `30E433B3C6B1B51C5F8FF70E7CF7F54DFD8B70BE53D31311FA3791DF34CBA09E`. In both cases the normalized-text
  hash equals the before hash. The old v5 live-session input manifest remains preserved with its original bytes;
  those native sessions preceded this line-ending-only test-source edit, so this checkpoint does not claim the
  current files match the old frozen hashes. The test assembly hash consequently changed from frozen
  `40AC972ACC4B964AEF3801568472697BB6464DEC5C40BBD81EF93609AF28E338` to current
  `B37721965CDC2B0EC38A448A235A5DF43B2494C539F326CB3193EEAD53FCCECF`; the apphost hash remains
  `116E40A84557F723F078278BE14FC6B4D98155C775E392453C05E992FB1F0031`. The product plugin/gateway hashes
  above are unchanged.
- [ ] Establish the historical provider-state and debugger monitor-overlap causes; current exact-bundle
  success is evidence of non-recurrence, not proof of why the old failures occurred.
- [ ] Qualify `symbol_enable_sources` with an independent effect oracle; do not invoke a path that can download
  symbols from a public server or infer success from the echoed request.
- [ ] Keep the six DBK/DBVM-dependent tools open until a reviewed suitable environment passes their success and
  cleanup oracles. No VM is available for this checkpoint.
- [ ] Explain the original run-root no-reparse refusal (`20261020T170209Z-6007`, `C000050B`); keep its guard.
- [x] Complete the fresh independent re-review of the corrected current-27 validation record at
  `artifacts/test-results/all-tool-qualification-20261021/current27-final-validation-independent.json`.
  Final verdict: **GO**, no actionable findings. Record SHA-256:
  `B41DF8AF659A1765D91D45C6E028955FE8A53CF36755770006640A5925F1B93C`. Its first review caught a
  CRLF-sensitive parser error for successful builds; the corrected record reads the exact log lines and shows
  both configurations passing. The report itself remains incomplete with seven open native tools.

## Previous checkpoint (current-26, 2026-10-21)

- [x] Keep all seventeen report sessions pinned to exact bundle 06: `artifacts/dist/all-tool-final-20261020-06`,
  version `2.0.0-beta.4+1f987742b7fcd7760057a19508ddacbf2aad4bd8`, plugin SHA-256
  `E05A449551716AC485E2C5897397D109584B94CBEC3444FD8C7461432CF0462B`, gateway SHA-256
  `1FEEFE4975AE558C78932B63C93BAE7A97EB6DBA571C76402B87B63B0406467A`, and installed CE host SHA-256
  `CF6CCC664A90C23531EC20F5C41458CFDC1D04679D6D05ED94C06BCECF95544F`.
- [x] Pass the reviewed assembler scenario `20261021T001320Z-0923`: 9 mapped tool receipts, 2 cleanup
  receipts and MTP 1/1. Summary SHA-256 `85FB4F03A647F1480251989E142310AA2B1C34D2429BFC27775D981299316972`,
  receipts SHA-256 `239B91358E1B17E2123C6125BBC54F686532D2816FCE89D85BA09D54B9B956F6`, TRX SHA-256
  `09A3DDB31E81C786F4D1CDBC3764D01FCBB3894FC63DD9993AC0D81E090B33FB`.
- [x] Pass compiler export and fixed managed injection in `20261021T000621Z-a348`: 2 mapped tool receipts,
  1 cleanup receipt and MTP 1/1. The held export survived host B shutdown and the owned Framework fixture
  returned `7319`. Summary SHA-256 `98D5AB7446DF4EB8400BD469DB7CEAE81B0358493A0E8395A9BB9C15F57AFBD4`,
  receipts SHA-256 `D43098C8457BC7C19DF0F4DFA21B361081B7C020D93C51484B6C989AB44EF36F`, TRX SHA-256
  `F7D1CA2B7E5732BD562A789805B42FCC294F805B6CBED914E910E62D8F6FE900`.
- [x] Pass process creation in `20261021T001819Z-ef61`: 1 mapped tool receipt, 1 cleanup receipt and
  MTP 1/1. Summary SHA-256 `90433A160F5BD1AFAF488CE8B34CF30D9FE20EC73022FA17898DB936D45BD1E4`,
  receipts SHA-256 `ACDE933D78072678C88F1E99E12B17ED4A7344279D76AE4FAD376BA4523BD771`, TRX SHA-256
  `3A4F02645A2C936D426FFE62F7C61E776CE9DA61D0D3BDED76DC9D5FD97EDEFE`.
- [x] Pass Mono in `20261021T001905Z-0c47`: 15 mapped tool receipts, 1 cleanup receipt and MTP 1/1.
  Summary SHA-256 `1FDC0CCD0F9F4DE1A3BFB36D43EE1F4D6E7A61F520E69F15B8F3B36591D5F196`, receipts SHA-256
  `BBA6703428CFD816BF5B1AEC8AE7874C841336A0CDCAAA0F0D81BB651147AEC0`, TRX SHA-256
  `B1491A2E32D5CBB76897FF0D304F736952791D18222B6DAEE4B850255C50A6FC`. The preceding v4 Mono attempt
  `20261021T000658Z-1f16` stopped before child startup because its generated manifest exceeded the legacy
  path limit; the reviewed v5 launcher now preflights the shorter 237-character path.
- [x] Retain the later v5 compiler-injection failure as diagnostic-only: `20261021T001411Z-c26f` has a mapped
  `compiler_call_held_export` receipt recording `IsError=true`, `kind=invalid_argument`, and
  `hostEffect=not_started`. The separate fixed hold-ready file was not published, so neither compilation nor
  injection was attempted. Settings were restored, installed files were unchanged, compiler files were removed,
  cleanup failures were zero and all owned processes stopped. Its full API error text was not saved, so the
  rejection cause is unknown. The separate same-bundle compiler/injection pass above remains valid.
- [x] Qualify debugger tools with current-bundle success evidence: monitor-free basic run `20261020T225741Z-5c14`
  covers attach, break, status, context, stack, continue and detach; event capture `20261020T220121Z-d800`
  and execution `20261020T220529Z-dd07` also pass with cleanup. `structure_list` passes in state-workflows and
  workflows (`20261020T220755Z-2b6f`, `20261020T223158Z-a2f8`). A separate diagnostic-only replay
  (`artifacts/provider-fields-diagnostic/provider-fields-live-20261020T125039Z-8ac7.json`) also passed its
  historical provider prefix on an isolated candidate: A/B recorded 118/22 provider-top reads, calibrated
  top 0 and 1 against the loaded Lua module, and had no negative tops, read errors, or trace overflow. The old
  provider `-101889` and debugger monitor-overlap `SEHException` have not recurred, but their historical causes
  remain unknown; the diagnostic was not qualification evidence and no speculative product change was made.
- [x] Qualify plugin reload on a startup-seeded `Plugins64` row. Three in-process disable/enable cycles passed
  1/1 in `20261020T232333Z-da96`; this does not establish why the earlier unseeded attempt saw zero rows after
  the Settings form had already been created. That earlier harness seeded the registry only after the form
  existed, so it did not establish a product reload regression. Summary SHA-256 `ADB3A4FF821D36FECE6E7B4E8C3D31AB20BCB13A9128571F2D9834C861151BE5`,
  receipts SHA-256 `CE4FB6E1574F8E22FF585F86BF7022F29C5506025B147745A7AFB08A6C5D4469`, TRX SHA-256
  `10D80C82568C5EF16D9FD20080C95CD3604A4BA37A8DB203610279E303B67ABA`.
- [x] Check whether the local Windows symbol cache can safely qualify `symbol_enable_sources`. The only local
  `ntdll.pdb` cache entry is keyed `2CF5F86ACB68735923D72913BBD9B0E31`, while the current system `ntdll.dll`
  (version `10.0.26100.9549`, SHA-256 `651AC9041AF8E6B5EBD331D53163244C0A27D36D1433F0F15CAC1F28C5F2E82C`)
  embeds CodeView GUID `C031D45B-0DA1-8A6E-74D0-6ED6AB2DD9E1`, age 1. The local PDB therefore does not match
  this image. No source-enable call was made: a fail-closed local-only path and independent source readback
  were not established, and the kernel source path still requires DBK/DBVM.
- [x] Obtain no-VM kernel suitability evidence: exact-bundle `kernel_get_status` passed in
  `20261020T221714Z-226c`; DBK/DBVM were inactive and the independent driver inventory contained no matching
  driver. This is environment/status evidence only; it does not qualify the six kernel operations.
- [x] Regenerate `artifacts/all-tools-goal/all-tools-current-26/tool-results.json` with current-25 evidence,
  four passing native sessions, and the two later diagnostic failures. It contains **200 verified** and
  **7 not verified** rows across the 207 advertised tools, with `nativeQualificationComplete=false`; 27 rows
  were added since current-25. Report SHA-256:
  `04168F6F69F5A36143F4F73E9774FB1CE314888F3F9EEB841B77AE2BC2B4166D`. Release portable passed 5,832/5,832
  and NativeLua 757/757.
- [ ] Establish the cause and corrective fix for the historical provider-state failure; the old
  `-101889` stack-top result remains unexplained despite current-bundle `structure_list` passes.
- [ ] Establish the cause of the historical debugger monitor-overlap `SEHException`; current-bundle basic,
  event and execution scenarios pass, but the old exception's cause remains unknown.
- [ ] Qualify `symbol_enable_sources` with an independent effect oracle. The local `ntdll.pdb` does not match
  the current system image; do not infer an effect from echoed request values or invoke a source path that can
  download symbols from a public server.
- [ ] Keep the six DBK/DBVM-dependent tools open: `kernel_initialize_dbvm`, `kernel_poll_watch`,
  `kernel_read_physical`, `kernel_start_watch`, `kernel_translate_address`, and `kernel_write_physical`.
  This host has no VM and DBK/DBVM are inactive; do not change driver, service, boot, firmware, VBS or
  virtualization settings without a separately reviewed, recoverable plan and appropriate authorization.
- [ ] Explain the original run-root no-reparse refusal (`20261020T170209Z-6007`, `C000050B`); the guard was
  not weakened.
- [x] Complete independent review and final repository/report validation for current-26. Reviewer verdict: GO,
  no actionable findings. The independent validation record is
  `artifacts/test-results/all-tool-qualification-20261020/current26-final-validation-independent.json`,
  SHA-256 `4D2994B22426AD96FD9AE57F820BFECD617C8D0DB73F70CC165A7F19AD155040`; it validates report SHA-256
  `04168F6F69F5A36143F4F73E9774FB1CE314888F3F9EEB841B77AE2BC2B4166D`, has zero session issues, and
  rechecks 3,287 frozen inputs with only the declared postrun TODO/README edits. `git diff --check` passes.

## Previous checkpoint (current-25, 2026-10-21)

The current-25 report recorded 173 verified and 34 unverified tools across 207 advertised tools. Its report
SHA-256 was `19F232D92161EE2976E99887A9AE75CB7E404A7F128080B7A5572F1A5079DA27`; current-26 adds the
four reviewed native sessions and preserves the failed Mono/compiler attempts as diagnostic observations.

- [x] Qualify plugin disable/re-enable on the exact bundle. A private `Plugins64` row was seeded before
  host A initialized; three in-process cycles passed the MTP scenario 1/1 in `20261020T232333Z-da96`.
  Summary SHA-256 `ADB3A4FF821D36FECE6E7B4E8C3D31AB20BCB13A9128571F2D9834C861151BE5`, receipts SHA-256
  `CE4FB6E1574F8E22FF585F86BF7022F29C5506025B147745A7AFB08A6C5D4469`, and TRX SHA-256
  `10D80C82568C5EF16D9FD20080C95CD3604A4BA37A8DB203610279E303B67ABA`. State was restored, installed
  files were unchanged, and cleanup failures were zero. The earlier unseeded session remains a failed
  observation; the later run passed with startup-seeded state.
- [x] Add six direct smoke receipts from `20261020T233322Z-c49d`. Its MTP case passed 1/1 and the independent
  audit at `artifacts/test-results/all-tool-qualification-20261020/final-bundle-06-smoke-native/run-2a296e25f98e4b06a7a891194f810312/postrun-validation-independent.json`
  confirmed exact candidate/host identity, all 3,280 frozen input hashes, restored state, unchanged installed
  files, zero cleanup failures and no remaining owned processes. The wrapper exited 1 after the pass because
  StrictMode read an absent optional `diagnosticOnly` field; the independent audit preserves that boundary.
  All four optional gates were true, so this session supplies no disabled-gate refusal evidence. Its six
  direct report additions are `instance_list`, `pointer_get_access_info`, `scan_list_scanners`, `scan_delete`,
  `speedhack_set_speed`, and `speedhack_get_state`; pointer access uses supplied-fact arithmetic, not a live
  register capture.
- [x] Qualify the four fixed C/ABI execution tools in `20261020T234126Z-7aba`; the exact-bundle MTP test
  passed 1/1. Summary SHA-256 `046241E91BBDF48321D5B772819981D9441879BDE431EAAC84CF7E5A781E75E0`, receipts SHA-256
  `9AABB5EE2C6C567F94036DABD247B20D61D52CF13881B86EC896A37A357E95D2`, and TRX SHA-256
  `3573259E6C65ECC9D2363F13605D5D7C870D804F35E52363E0543E62A203E014`. `exec_compile_c`,
  `exec_call_remote`, `exec_call_method` and `exec_call_local` each have their direct success receipt.
  Both owned allocations have separate `freed=true` cleanup receipts; the passing scenario also requires
  both allocations to return `not_found`. User state was restored, installed files were unchanged, all 3,280
  frozen inputs matched, cleanup failures and remaining owned processes were zero. Only target-code execution
  was enabled; Auto Assembler, unsafe Lua and kernel access stayed disabled.

## Previous native checkpoint (current-22, 2026-10-21)

- [x] Complete the reviewed native success and cleanup cases available on exact final bundle 06.
  Candidate: `artifacts/dist/all-tool-final-20261020-06`, product
  `2.0.0-beta.4+1f987742b7fcd7760057a19508ddacbf2aad4bd8`, plugin SHA-256
  `E05A449551716AC485E2C5897397D109584B94CBEC3444FD8C7461432CF0462B`, gateway SHA-256
  `1FEEFE4975AE558C78932B63C93BAE7A97EB6DBA571C76402B87B63B0406467A`.
  Debugger event capture passed in `20261020T220121Z-d800`; debugger execution/run-to passed in
  `20261020T220529Z-dd07`; state workflows passed in `20261020T220755Z-2b6f`; read-only kernel status
  passed in `20261020T221714Z-226c`; and the reviewed local DLL/PDB symbol fixture passed in
  `20261020T222142Z-7fef`. The workflows scenario then passed its one MTP test in
  `20261020T223158Z-a2f8`. All six passing sessions record the exact bundle identity, restored user state,
  unchanged installed CE files and zero cleanup failures. State-workflows' launcher postrun count assertion
  expected one `host_A_stopped` receipt, but the valid restart plus final cleanup produced two; its separate
  read-only postrun validator confirmed the MTP pass, all 3,278 frozen inputs, host restart identities,
  receipts, cleanup and zero remaining owned processes. The workflows test also passed 1/1 and its summary
  reports restored settings, unchanged installation and zero cleanup failures. Its launcher stopped after the
  native pass because StrictMode rejected an omitted optional `release.failed` field. The independent
  validator at `artifacts/test-results/all-tool-qualification-20261020/final-bundle-06-workflows-native/run-9f2c7d83e0b14d41a784b62f09c13a65/postrun-validation-independent.json`
  originally confirmed all 3,278 frozen input hashes, exact candidate/host identity, release and cleanup
  receipts, preference readback/restoration, module resolution and process/settings cleanup. The refreshed
  proof records that snapshot check and confirms the only later input changes are the README, TODO, report
  generator and regression-test updates; it rechecks the summary, receipts, TRX, runner-log result and current
  candidate/host.
  The runner's OS exit code was not independently captured. No retry was made.
- [x] Regenerate `artifacts/all-tools-goal/all-tools-current-22/tool-results.json` using exact-bundle-06
  evidence and the retained portable/NativeLua reports. It contains **156 verified** and **51 not verified**
  rows across all 207 advertised tools, with `nativeQualificationComplete=false`; 52 rows became newly verified
  since current-21, with no regressions. Fifteen of those newly verified rows are read-only and have no
  tool-specific cleanup receipt: the report records `cleanupRequired=false`, `cleanupReceiptPresent=false`,
  and `cleanupSatisfied=true` instead of claiming a receipt exists. SHA-256:
  `66E3D5F31515DC1ACB896D74BF97CCD7CB210DC9FF390427BB7DCE0EBD7C5408`. Six passing sessions above add
  debugger, state, `kernel_get_status`, symbol fixture, runtime/resource, reversible module-preference and
  owned-module resolution evidence. The seventh included session is the failed plugin lifecycle attempt below
  and does not qualify a tool.
- [x] Final-source Release portable validation passed 5,832/5,832 tests, and NativeLua passed 757/757,
  with no failures or skips. The report reuses the Release TRX for gateway and portable inputs; current-source
  builds and Debug validation were completed before the final native batch.
- [x] Added and ran `Report_NoCleanupReceiptMapping_MarksCleanupNotApplicable` against the real report script.
  The no-restore Debug build passed with zero warnings/errors; native MTP discovery found the fully qualified
  test and the focused run passed 1/1. TRX SHA-256:
  `7B76CE533BD63B8FC89DA2F114AE72D660415A7EA58365C214AA1F9F5BC69635`. The normal sandbox denied the
  runner's named-pipe IPC, so test execution used the approved elevated invocation. This verifies report
  metadata only and adds no native MCP qualification.
- [ ] Resolve the provider-state root cause. The older `structure_list` session `20261020T092949Z-4423`
  received stack top `-101889`; the final-bundle state and workflows runs passed their `structure_list` cases,
  but did not reproduce or explain the earlier exception. The tool now has passing exact-bundle evidence;
  containment and non-reproduction are not a root-cause fix.
- [x] Qualify same-process plugin reload. The current-22 attempt `20261020T221415Z-d111` observed zero
  owned rows after load; it does not establish why that observation occurred. Exact-bundle current-25 session
  `20261020T232333Z-da96` seeds the owned row before host startup and passes three in-process disable/enable
  cycles with cleanup and restoration verified.
- [ ] Qualify `symbol_enable_sources` with an independent effect oracle. Do not infer an effect from echoed
  request values; avoid the Windows source path that may download symbols and the kernel path that requires
  DBK/DBVM.
- [ ] Obtain suitable native evidence for six environment-dependent kernel operations:
  `kernel_initialize_dbvm`, `kernel_poll_watch`, `kernel_read_physical`, `kernel_start_watch`,
  `kernel_translate_address`, and `kernel_write_physical`. Only read-only `kernel_get_status` passed on
  this host. The host inventory, status receipt and per-operation safety/evidence requirements are recorded
  in `docs/qualification/all-tools/later-kernel-test.md`. No DBK/DBVM initialization, driver, VBS, service
  or boot changes were made.
- [ ] Explain the original run-root no-reparse refusal (`20261020T170209Z-6007`, `C000050B`); the
  path-specific refusal remains unresolved and the guard was not weakened.
- [x] Complete the independent final review of current-22, including the refreshed workflows postrun
  validation and the failed plugin-reload boundary. The reviewer returned GO with no actionable findings;
  diagnostic-only summaries are normalized to failed before report qualification.

## Historical checkpoint (pre final bundle 06)

- [x] Finish the reviewed live success and cleanup cases on the exact bundle 6 candidate.
  Compiler and managed injection passed in `20261020T175440Z-0fa2`; the isolated basic debugger passed in
  `20261020T180500Z-3fca`; read-only kernel status passed in `20261020T180540Z-55c5`; and explicit
  all-scope symbol reload passed in `20261020T181211Z-6bc0`. Every passing session restored user state,
  left the installed CE tree unchanged and reported no cleanup failures. The symbol receipt shows an empty
  pre-reload result and one exact module-filtered export afterward at the independently parsed PE address,
  with `symbolsLoaded=true`.
- [x] Generate `artifacts/all-tools-goal/all-tools-current-19/tool-results.json` from 19 same-candidate
  sessions and the preserved passing gateway, portable and NativeLua reports. The report records **189
  verified tools** (188 backend plus gateway `instance_list`), **6 diagnostic-only**, **12 not verified**,
  and `nativeQualificationComplete=false`. The compiler, managed-injection, kernel-status and symbol-reload
  rows plus six basic debugger rows now have same-session success and cleanup receipts. SHA-256:
  `F3B7FCBA80C2D60159070A0454DF132E751952C807B352BD472C966C35EEB268`. Candidate version:
  `2.0.0-beta.4+1f987742b7fcd7760057a19508ddacbf2aad4bd8`; plugin SHA-256
  `5F86B15826142FD655498AA1E8E5AF8A380A488D27E55AF939679A4B7A72F9FA`; gateway SHA-256
  `059727F11E310988C501C4DB494529660FF40D938B20638805AF05924E9ADBB3`.
- [x] Validate current source: Release and Debug builds completed with zero warnings/errors; each portable run
  passed **5,793** tests with zero failures or skips. NativeLua passed **757** tests with zero failures or skips.
  Fresh TRXs are under `artifacts/test-results/all-tool-qualification-20261020/final/current-release-portable/`,
  `current-debug-portable/` and `current-native-lua/`.
- [ ] Resolve the provider-state root cause. The old `structure_list` session `20261020T092949Z-4423`
  received stack top `-101889`; current-source diagnostics and later workflows did not reproduce it. The
  containment guards and non-reproduction are not a root-cause fix.
- [ ] Resolve the advanced debugger run-to cleanup failure before qualifying event capture. Session
  `20261020T162659Z-23f9` reached its run-to stop but detach threw `SEHException` during monitor overlap.
  The isolated basic debugger pass does not cover run-to or debugger event tools. Six run-to/breakpoint/register/
  step rows remain diagnostic-only and five debugger event rows remain not verified.
- [ ] Resolve same-process plugin reload. Diagnostic `20261020T133119Z-43a6` loaded the plugin but the
  Settings bridge found zero owned plugin rows. No supported independent reload discriminator or safe fix has
  been established; the case remains unverified.
- [ ] Qualify `symbol_enable_sources` with an independent readback/effect oracle. Do not infer an effect from
  its echoed request.
- [ ] Obtain safe native evidence for the six remaining kernel operations. Only read-only
  `kernel_get_status` passed. The other operations require DBK/DBVM; this host has no VM, and no driver,
  DBVM, VBS or service state was changed.
- [x] Run the independent `state-workflows` scenario on owned x64 targets with the same bundle 6.
  Session `20261020T171316Z-d57b` passed all 78 mapped state/record/pointer/file/code cases, target/gateway/host
  restarts and persisted-note recovery. Independent review checked every mapped receipt and cleanup receipt,
  all four disabled gates, owned allowlists, unchanged inputs and installed files, restored settings and no
  remaining owned processes. The Release harness build has zero warnings/errors and 103 focused tests pass.
- [x] Generate `artifacts/all-tools-goal/all-tools-current-16/tool-results.json`: **133 verified** (132 backend
  plus gateway discovery), **12 diagnostic-only**, **62 unverified**, `nativeQualificationComplete=false`.
  This combines nine passing sessions, the debugger diagnostic and the failed original-root state session
  on the same exact product binaries. Failed and diagnostic observations do not qualify success.
- [ ] Explain the original run-root's no-reparse refusal. Failed state session `20261020T170209Z-6007`
  stopped before `pointer_save_map` began; its cleanup and restoration passed. A read-only standalone
  call into the same Core DLL reproduces `C000050B` on children below the old `runs` directory despite
  ordinary directory attributes. The identical guarded walk and native workload pass under the new
  `Local/Temp/CheatEngine.Mcp.LiveQualification` root. No guard, native flag or product code was weakened;
  changing the fixture root is not a fix for the original path-specific refusal.
- [x] Run the bounded inspection scenario on both owned x64 and x86 targets using bundle 6.
  Sessions `20261020T164951Z-0ffb` and `20261020T165142Z-1eee` passed, including gateway restart,
  allocation release and restoration of original module preferences. Independent review confirmed both
  single-test TRXs, all intended receipts, four disabled optional gates, empty file/table allowlists,
  unchanged frozen inputs, restored user settings and installed files, and no remaining owned processes.
  The Release harness build has zero warnings/errors; all 101 focused admission/options/cleanup tests pass.
- [x] Preserve the earlier `artifacts/all-tools-goal/all-tools-current-15/tool-results.json` from the same bundle's
  eight passing sessions and one failed diagnostic: **55 verified** (54 backend plus gateway discovery),
  **12 diagnostic-only**, **140 unverified**, and `nativeQualificationComplete=false`.
  The two inspection sessions add 16 verified rows: the 14 read/utility/symbol-preference cases plus
  process attachment and disabled-gate runtime information. Diagnostic observations remain unqualified.
- [ ] Identify the cause of the original run-root's no-reparse refusal documented above; the path-specific
  `C000050B` reproducer remains unexplained.
- [ ] Complete the required independent final review and address any actionable findings. Current-source
  builds and test suites passed above, but the remaining native cases are still open.

## Checkpoint validation

- [x] Release solution build: zero warnings and errors (`artifacts/pause-checkpoint-build-2.log`).
- [x] Standalone Lua suite: 743 passed, zero failures/skips (`artifacts/pause-checkpoint-lua-2.log`).
- [x] Changed C# whitespace verification passed (`artifacts/pause-checkpoint-format-1.log`).
- [x] Portable Release suite: 5,728 passed, zero failures/skips (`artifacts/pause-checkpoint-portable-2.log`).
- [x] Run the saved checkpoint through Debug/CI and inspect the result after publication.
  GitHub build run `39314293272` passed at `1f987742b7fcd7760057a19508ddacbf2aad4bd8`.
  Fresh local Debug build: zero warnings/errors; portable suite: 5,728 passed, zero failures/skips.

## Resumed verification on 2026-10-20

- [x] Correct stale Mono fixture identity validation, rebuild the owned fixture from current source, and pass all
  32 focused manifest/build-identity regressions. Preserve the old fixture under an ignored checkpoint folder.
  Fresh standalone `continue-mono-preflight.json` verified matching source/runtime/EXE identities, PID/nonce,
  nonzero pinned object pointer, primitive values, warmup result, stop marker and exit code zero.
  This qualifies no MCP Mono tool.
- [x] Publish the isolated `artifacts/dist/continue-20261020-1` bundle, including its Native AOT gateway smoke check.
- [x] Verify the resumed source with 5,736 portable tests in both Debug and Release and 743 standalone Lua tests,
  all with zero failures/skips. Both full solution builds passed with zero warnings/errors.
  The Release report includes all 11 seeded fuzz tests; both style and whitespace verification passed.
- [x] Correct the Tools README's test filters and verify discovery of 2,005 Tools and 320 Contract cases.
- [x] Fix contradictory remaining-work text for verified report rows; reproduce the old failure and pass all
  14 cumulative-report regressions while retaining failed/diagnostic evidence boundaries.
- [x] Run the isolated process-create scenario: `20261020T092911Z-916a` passed, restored user settings, preserved
  installed CE files and reported no cleanup failures.
- [ ] Resolve the ordinary workflow's reproduced native Lua-state failure before continuing native verification.
  `20261020T092949Z-4423` completed allocation release and scanner-stop cleanup, then `structure_list` received
  a provider state with stack top `-101889`. The complete session failed; settings restoration, installed-file
  preservation and host cleanup passed. Its earlier operation receipts do not qualify native success.
- [x] Prepare and review a current-source provider diagnostic. The old instrumented SDK predates current
  provider-stack guards and same-state callback cleanup and must not be replayed unchanged.
  The reviewed current-source prefix ran once in `20261020T100317Z-52fd`; it did not reproduce the state failure
  and restored settings with no cleanup failures. Host A produced no trace, so the diagnostic postcheck failed.
  Host B retained 150 complete records, including 88 provider records. No native tool is qualified by this run.
- [x] Fix the incomplete diagnostic capture, verify synchronous creation of the owned trace directory and
  obtain complete captures in another independently reviewed bounded run.
  The candidate-only helper passed 18 focused cases, including a junction escape refusal. Independent review
  verified all 282 frozen inputs. Run `20261020T102110Z-3f05` retained 946 complete host-A records and 150
  host-B records, with no overflow/failure record. Settings restoration, installed-file preservation and
  owned-process cleanup passed. The failure did not recur; its root cause remains unresolved.
- [x] Repair private Mono provisioning and its effect oracle: four collector-copy regressions and three legacy
  path-limit boundaries pass. The refreshed source passes 5,743 portable cases in each of Debug and Release,
  plus 743 standalone Lua cases, all with zero failures/skips. Both builds and both formatting checks pass.
  `continue-20261020-3` passed publication, packaged-plugin catalog validation and Native AOT gateway smoke.
  Review confirmed exactly four intended golden-line changes: warning descriptions and document sizes.
- [x] Complete the current-bundle Mono scenario `20261020T113335Z-7c44`: all 15 Mono tool receipts passed,
  including both one-shot method invocations, exact object fields and the complete instance-search page.
  Collector detach, job absence, target exit code zero, settings restoration, installed-file preservation
  and absence of all owned processes were independently checked.
- [x] Repeat smoke, isolated process-create, assembler/API-hook generation, fixed C/ABI and DLL/PDB scenarios
  on `continue-20261020-3`. Runs `20261020T113552Z-757d`, `20261020T113649Z-0577`,
  `20261020T113725Z-a97b`, `20261020T113806Z-2760` and `20261020T113838Z-0c99` passed with cleanup.
  Smoke used the plugin's enabled default gates; it made no kernel calls. The other scenarios assert their
  explicit capability configuration. Automatic symbol discovery still leaves explicit reload inconclusive.
- [x] Correct diagnostic dispatch before a new debugger attempt: diagnostic mode must skip the ordinary
  break/context cycle. Two regressions failed against the old order; all four focused cases pass after
  the correction. Independent bounded review found no remaining dispatch issue.
- [x] Run that isolated diagnostic once: `20261020T114533Z-36ae` observed an installed `CC` entry byte
  and advancing owned hit/progress counters, but zero callbacks. It made no context request; callback
  restoration, breakpoint removal, detach and sandbox cleanup passed. This qualifies no debugger tool.
- [x] Correct breakpoint API overload arity: omit absent optional method/callback slots rather than passing
  explicit `nil`. Three of eight argument-shape regressions failed before the fix; all eight pass after it,
  and all 751 standalone Lua cases pass. Independent review found no remaining arity issue.
  New local distribution `continue-20261020-4` passed its isolated plugin and Native AOT gateway probes.
- [x] Verify that correction natively: diagnostic `20261020T115504Z-1dfb` observed one exact owned callback,
  matching context, restored callback/breakpoint/debugger state and advancing post-detach counters. It remains
  diagnostic-only. Ordinary debugger session `20261020T115551Z-681a` separately passed the existing basic and
  first execution group with cleanup. Six independent scenarios repeated successfully on the same bundle.
  Report `all-tools-current-6` verifies 50 bounded native cases on `continue-20261020-4`, with 157 unverified.
- [x] Require explicit host acknowledgement for thread-ignore changes; four false-success cases reproduced
  the old behavior. All six acknowledgement cases and 757 standalone Lua cases pass after the correction.
- [x] Prepare and independently review the five remaining debugger event cases, with exact owned thread/
  instruction oracles, 23 passing oracle regressions, empty job admission and bounded restoration proof.
- [ ] Complete their native run. Bundle `continue-20261020-5` passed isolated-plugin and Native AOT probes,
  but session `20261020T121721Z-6535` crashed host A during existing run-to cleanup, before the new cases.
  Windows Application event 1000 independently records PID 72728 / `0x11C18`, access violation `0xc0000005`,
  unknown faulting module at `0x7ffb09b29820`. Settings and installed files were preserved and all owned
  processes exited. Missing host-A disable evidence remains a cleanup failure; no native tool is qualified.
  Original evidence is retained in `artifacts/test-results/continue-20261020/debugger-crash-6535`.
- [x] Run a fresh independent Astra checkpoint review. It found no implementation issue; its stale suspension
  wording finding is corrected. This does not replace final verification after the remaining native work.
- [x] Verify the current debugger/event source with 5,774 portable tests in each of Release and Debug and
  757 standalone Lua tests, all with zero failures/skips. Both solution builds passed without warnings/errors.
  One earlier loaded Debug attempt hit the notebook directory-lease contention timeout; its focused case
  and the complete sequential rerun passed without changing the notebook implementation or its test.
  Report `all-tools-current-7` retains 207 routing passes, 13 failed-session observations and 194 unverified
  rows for bundle 5. None of the crashed session's receipts establishes native success.
- [x] Repeat all six independent scenarios on bundle 5 after the final checks and a fresh input freeze:
  smoke `20261020T122756Z-115c`, process-create `20261020T122829Z-828c`, assembler `20261020T122901Z-eac8`,
  fixed C/ABI `20261020T122940Z-16c2`, symbols `20261020T123008Z-bc8d` and Mono `20261020T123037Z-35e9`.
  All passed with settings/install preservation and no cleanup failures or remaining owned processes.
  Post-run hash verification matched all frozen source/dependency/binary inputs. `all-tools-current-8`
  now records 38 verified native cases, 12 failed-session observations and 157 unverified rows for bundle 5.
  Ordinary debugger and x64/x86 workflow retries remain suspended pending new diagnostic evidence.
- [x] Extend the private provider diagnostic to version-2 atomic field capture, with bounded self-memory reads,
  exact loaded-Lua module identity, per-state/lifetime calibration and contained diagnostic failures.
  Independent review findings were fixed; 20 focused SDK cases and 92 harness cases pass. The packaged plugin
  probe and embedded-SDK equality checks passed; independent preflight verified all 3,281 frozen inputs.
  Single-use run `20261020T125039Z-8ac7` retained 946 complete A records and 150 B records, with matching
  loaded-module hashes and same-state healthy top-0/top-1 calibration. No incomplete, overflow or first-failure
  record occurred. Settings/install preservation, all owned process exits and post-run input hashes passed.
  Report: `artifacts/provider-fields-diagnostic/provider-fields-live-20261020T125039Z-8ac7.json`.
  The failure did not recur. This validates capture only; it establishes no root cause or native tool success.
- [x] Map the existing passing native gateway discovery receipt with a gateway-only scope. All 17 report
  regressions pass, including rejection of interchanged gateway/backend scopes. Report
  `artifacts/all-tools-goal/all-tools-current-9/tool-results.json` reuses the same exact bundle-5 sessions:
  39 verified cases (38 backend and one gateway-local), 12 failed-session observations and 156 unverified.
  This bookkeeping correction adds no backend native qualification.
- [x] Run one independently reviewed private plugin-registry diagnostic, `20261020T133119Z-43a6`.
  Exactly the owned registry pair was seeded and verified before the single requested cycle. The Settings
  checklist still reported zero rows before and after reload (`loading=nil`); no transition was reached.
  The failed hypothesis is diagnostic-only. User settings, installed files, all 3,266 frozen inputs and
  owned-process cleanup passed; it establishes no cause or native success. Do not repeat it unchanged.
  Report: `artifacts/plugin-registry-diagnostic/plugin-registry-live-20261020T133119Z-43a6.json`.
- [x] Refresh source validation after the gateway-report changes: 5,777 portable cases pass in each of Debug
  and Release, and all 757 standalone Lua cases pass, with zero failures/skips. Both solution builds report
  zero warnings/errors; style and whitespace verification report no changes. Format workspace loading
  reports duplicate analyzer-release documents and a source-generator metadata reference warning.
  Report `artifacts/all-tools-goal/all-tools-current-10/tool-results.json` uses these fresh Release/Lua TRXs
  and the same seven bundle-5 sessions: 39 verified (38 backend + gateway discovery), 12 failed-session
  observations and 156 unverified. The private plugin diagnostic remains excluded.
- [x] Build and independently review a private external crash-capture helper, then verify it only against
  its own x64 fixture. Run `d36ed920e2cd4260a65e28cb667f8875` passes 20 admission-refusal and six runtime
  cases: clean stop, duration detach, handled breakpoint/AV, one second-chance AV dump and survival after
  confirmed exit-kill disablement followed by abrupt helper exit. All owned processes exited.
  Four exclusions remain explicit: x86, oversized executable, recovery fault injection and redundant parser
  permutations. The first fixture attempt failed before helper startup because of reader sharing mode;
  the corrected bounded readers pass five local probes and independent review. This validates fixture
  capture only; it qualifies no CE tool or recovery-injection branch.
  Rebuilt v3 run `8d13ede4ac5c469cbef5b42b3ed35167` repeats all 26 passing cases and four explicit exclusions,
  with absolute compiler/linker identities and cleared `CL`, `_CL_` and `LINK` environment flags.
- [ ] Independently review the separate private CE integration and capture one bounded debugger failure
  group with that helper. The reviewed 3,763-file snapshot's one-use attempt
  `c035416b6a624bcc9986b425a77c9cbc` exited before sandbox construction: its manifest validator inserted
  already-parented JSON nodes into another array. A read-only preflight reproduced the exact exception.
  No sandbox/helper evidence was produced; installed files and frozen inputs were unchanged and no owned
  processes remained. The launcher conservatively records restoration unconfirmed because no sandbox
  summary exists. Preserve this failed attempt; fix and test admission plus durable runner output in a new
  reviewed snapshot before another attempt. Outer-debugger non-reproduction cannot establish product stability.
  Reviewed v2 attempt `0eb66dff7959434c801cc37fd9b4dead`, session `20261020T151945Z-3c25`, reached both private
  hosts and helper readiness, but `debugger_detach` returned an unknown-outcome `SEHException` during the basic
  group, before the first execution group. The helper retained 224 events: one first-chance invalid-handle
  exception, one thread-name exception and ten CLR exceptions, all passed through; no second-chance AV or
  dump occurred. Read-only cleanup confirmed detached state. Helper stop/detach and exit, user settings,
  installed files, frozen inputs and all owned process exits passed independent evidence review.
  This failed diagnostic qualifies no tool and does not cover the original run-to crash. Preserve the
  consumed attempt; investigate monitor timing and the native detach path before any new reviewed probe.
  Reviewed V3 attempt `57a45025da89451f81f6be24af22f7ba`, session `20261020T162659Z-23f9`, ran basic checks
  without the monitor, then attached it before the first execution group. Basic detach succeeded and the
  execution assertions reached run-to; cleanup `debugger_detach` then failed with `SEHException`
  (`errorId=c9261948eb11f175`). The helper retained 221 events, including ten first-chance exceptions:
  one thread-name, one invalid-handle and eight CLR exceptions, all passed through. There was no AV or dump.
  Independent review confirmed helper stop/detach/exit, restored settings, unchanged installed files and
  all 3,777 frozen inputs, and no owned processes. This narrows the monitor-overlap correlation; it does
  not prove observer causality, identify the native callee, resolve the original crash or qualify a tool.
  Preserve the consumed diagnostic and do not repeat the unchanged monitor scenario.
  Report `artifacts/all-tools-goal/all-tools-current-14/tool-results.json` retains the six passing bundle-6
  sessions plus this diagnostic: 39 verified, 12 diagnostic-only and 156 unverified rows. The set of
  verified tools is unchanged; no diagnostic receipt is promoted to native success.
- [x] Correct `symbol_reload`'s polling instruction: `runtime_get_overview` has no `symbolsLoaded` field;
  `symbol_find` supplies it. Update both tool-description goldens and pass all five contract snapshots
  after a zero-warning/error Release build. This later description edit is not in bundle 5; its native
  receipts and report remain evidence for that old exact bundle. Final package/check repetitions remain due.
- [x] Refresh verification after that description edit: portable Release and Debug each pass 5,777 cases;
  standalone Lua passes 757, all without failures/skips. The Debug rebuild has zero warnings/errors;
  style, whitespace and Git whitespace checks pass. Report
  `artifacts/all-tools-goal/all-tools-current-11/tool-results.json` uses the fresh Release/Lua TRXs and the
  same seven old bundle-5 sessions: 39 verified, 12 failed-session observations and 156 unverified rows.
  No private crash/symbol diagnostic or newer product bundle is qualified by this refresh.
- [x] Retain unexpected operation-body exception types and managed frames in local dispatch logs, using
  the existing dispatcher diagnostic format without exception messages, data or source-file paths.
  Two regressions fail on the old implementation and pass with the change. The 92 focused dispatch,
  error-correlation and logging cases pass in both Release and Debug, with zero failures/skips;
  both builds have zero warnings/errors and independent source review found no blockers.
  This offline diagnostic improvement does not resolve a native failure or qualify a tool. The full
  5,777-case runs and report 11 above predate this edit; final source/package verification remains due.
- [x] Refresh full offline validation after the logging edit: Release and Debug solution builds have zero
  warnings/errors; portable suites each pass 5,779 cases and standalone Lua passes 757, with zero
  failures/skips. All 11 seeded fuzz cases pass in each portable run. Full style, whitespace and Git
  whitespace checks pass. Report `artifacts/all-tools-goal/all-tools-current-12/tool-results.json` uses
  the fresh Release/Lua TRXs and the same seven bundle-5 sessions. All 14 historical summary/receipt
  hashes are unchanged. Classifications remain 39 verified (38 backend plus gateway discovery), 12
  failed-session observations and 156 unverified rows; no replacement bundle or native fix is qualified.
- [x] Resume live testing after renewed user authorization and repeat six reviewed scenarios on fresh
  `continue-20261020-6`: smoke `20261020T160645Z-e46d`, process-create `20261020T160750Z-1b18`, assembler
  `20261020T160814Z-f8ee`, C/ABI execution `20261020T160848Z-7b1b`, DLL/PDB `20261020T160912Z-1be0`, and
  Mono `20261020T160938Z-bcb2`. All six live TRXs pass their single intended case. Independent review
  confirms exact success/cleanup receipts, restored settings, unchanged installed CE files, all 9,153
  frozen input hashes and absence of every recorded host, target, child and Mono PID.
  Report `artifacts/all-tools-goal/all-tools-current-13/tool-results.json` contains only bundle-6 evidence:
  39 verified rows (38 backend plus gateway discovery), 168 unverified and zero failed-session observations.
  The previous bundle's debugger failures remain unresolved historical evidence, not successful new-bundle
  cases. No debugger attachment/mutation or kernel call was made; smoke queried read-only debugger status.
  This checkpoint does not satisfy final all-tool qualification or the required final independent review.

The temporary live-testing hold after repeated cybersecurity prompts was lifted by the user's explicit
request to do the live tests. Ordinary debugger/workflow retries still require new diagnostic evidence
and review; kernel tests remain planned for a later attended session on this PC. Preserve the remaining
unchecked qualification items and existing safety checks.

The initial checkpoint tests exposed a Mono numeric-type mismatch and an outdated debugger receipt
assertion. Those were corrected before the final checkpoint validation. No new CE session was started
after the pause request.

## First steps next time

1. Read this file, `docs/mcp-alternatives-research.md`, and `docs/qualification/all-tools/README.md`.
   Check actual Git state and CI before making changes. Preserve checked and unchecked evidence boundaries.
2. Rebuild after any source edit. Do not rebuild a running test apphost or change tracked sources/binaries
   during a live native session. Recreate the prepared input hash manifest after every source/binary change.
3. Verify all owned CE/Mono/fixture processes from the previous run have exited and user settings restoration
   succeeded. Use private CE copies and owned targets; preserve the installed CE directory and user state.
4. Continue the independent scenarios below. Check off an item only after its assertions **and cleanup** pass.

## Prepared work awaiting native verification

- [ ] Repeat x64 `workflows` on the corrected exact bundle with the bounded manifest reader and isolated process-create case.
  The earlier run completed file-target recovery, managed object work and signature generation, but its
  later process-create failure invalidated the whole session. Finish target/gateway/host restarts and note persistence.
- [ ] Run the same updated `workflows` scenario on x86. Keep pointer-width and physical module-path checks.
- [x] Run the isolated `process-create` scenario. The unchanged Framework executable worked from a short path
  and exited before `Main` at a 253-character executable path. The fixture now copies to `target.exe`, rejects
  paths >=240 before launch, and records the raw create response before waiting for its manifest.
- [x] Run the private Mono scenario and qualify all 15 tools, including the fixed static and instance calls,
  exact object fields, complete instance-search page, collector detach, job absence and owned target exit.
  Only target-code execution is enabled; the other three gates remain disabled. Each method is invoked once;
  never retry an uncertain invocation.
  Attempts `20261020T102640Z-8602` and `20261020T102945Z-4038` failed before method invocation: first the
  legacy runtime's 290-character temporary manifest path, then the missing private collector DLL.
  Both restored settings, preserved installed files and cleaned up owned processes. The shorter root passed
  a standalone fixture preflight. The harness now checks the path limit and restores only the reviewed
  collector DLL for Mono; its OS module path/hash oracle leaves optional patch/table effects unqualified.
  Run `20261020T105656Z-fce5` then attached and verified the loaded collector but failed assembly selection
  before method invocation. The fixture compared the image name to the EXE filename rather than its CLI
  assembly metadata name. The correction passes all 43 Mono fixture cases in Debug and Release; the raw
  assembly list is retained before assertions. Passing session `20261020T113335Z-7c44` now qualifies these
  15 bounded cases on `continue-20261020-3`; optional patch/table effects remain unqualified.
- [x] Run the revised debugger transition diagnostic. It captures unconditional callback count/raw primitive
  values plus independent owned entry-byte/hit/progress snapshots. Request context only after the exact owned
  thread/RIP event. Diagnostic sessions must never count as verified tool success.
- [x] Repeat the isolated transition diagnostic on `continue-20261020-4` after the breakpoint arity fix,
  then finish the ordinary debugger success cases only if the exact owned event and cleanup pass.
  Both the diagnostic and existing ordinary group passed on that bundle. The later bundle-5 crash above
  is unresolved, so further ordinary debugger attempts remain suspended pending new diagnostic evidence.
- [ ] Use that evidence to finish debugger capture/trace/thread-ignore success cases and clean up every job,
  breakpoint, callback and debugger attachment. Do not infer breakpoint installation from list presence alone.
- [ ] Resolve same-process plugin reload. The reviewed Settings bridge still observes zero plugin rows after
  `loadPlugin` succeeds. No supported Lua-only discriminator or fix has been established; do not bypass
  ownership guards or guess unmanaged callback addresses.
- [ ] Resolve the intermittent SDK frame-restoration/compiler failure with new evidence; non-reproduction is
  diagnostic only. Re-run the compiler, managed-injection, C/ABI and patch cases on the final exact bundle.
- [ ] Finish remaining symbol source/reload cases without mistaking automatic discovery for an explicit effect.
- [ ] Qualify DBK/DBVM operations only in a suitable reviewed environment. Refusals and portable doubles do not
  establish successful kernel operation.
  The user requested no VM. Read-only inventory on 2026-10-20 reported `HypervisorPresent=true` and
  `VirtualizationBasedSecurityStatus=2`; no drivers, virtualization settings or services were changed.
  On 2026-10-20 the user selected a later kernel test on this PC. Prepare the prerequisites and keep these
  cases unchecked until that separately arranged test; do not disable VBS or change drivers/settings now.
  The proposed admission, owned-page oracles and recovery requirements are in
  `docs/qualification/all-tools/later-kernel-test.md`; they are a plan, not execution evidence.

## Mono prerequisite and local assets

- [x] Verified the official Mono 6.12.0.206 x64 MSI with a valid Microsoft signature; extracted privately,
  without a global installation.
- [x] Inspected shipped `mscorlib.dll` IL and `object.h`: legacy 32-bit GC handle bits map to the native
  `mono_gchandle_get_target(uint32_t)` C ABI and return the object header pointer.
- [x] Built and ran the standalone owned fixture: matching PID/hash/nonce, nonzero pinned object pointer,
  expected primitive values, stop marker and exit code zero. This qualifies no MCP Mono tool.

Local ignored runtime: `artifacts/fixtures/mono-6.12.0.206-x64/extracted/runtime`.
MSI provenance and extraction inventories are in the parent folder. MSI SHA-256:
`4125F57D97CFA88257915EDC969E913DE198CD8E22396A29849037479A0AC368`.
Build the fixed helper with `eng/Build-LiveMonoTarget.ps1`; it refuses existing output files, checks reparse
ancestors, pins runtime inputs, and writes the identity sidecar under `artifacts/live-mono-target`.
The legacy compiler's output is not deterministic: pin actual EXE bytes in the prepared run manifest;
do not replace that with a transient hardcoded EXE hash in tracked test source.

Local runner helpers remain ignored in `artifacts/issue-edits`:
`Save-RoadmapInputs.ps1` (use `-IncludeMono` for the complete private runtime input set) and
`Invoke-RoadmapSourceWorkflows.ps1` (includes `process-create`, `mono` and the debugger diagnostic switch).
The reviewed `Invoke-CurrentMono.ps1` uses the direct apphost, a shorter independent run root and an outer
deadline that preserves the cleanup-owning harness; it independently checks the separate Mono PID as well.
Raw live artifacts remain in the workstation's `CheatEngine.Mcp.LiveQualification/runs` folder.

## Evidence and completion

- [x] Generate cumulative all-tool evidence with `eng/Write-AllToolQualificationReport.ps1 -LiveSummaries`.
  Pass a PowerShell array. Sessions must have identical product version/plugin/gateway hashes; operation
  success receipt and each required cleanup receipt must appear exactly once in the same passing session.
  Where the map says cleanup is `none`, record cleanup as not required, not as a present receipt. Keep
  failed/diagnostic observations separate. Every one of the 207 catalog entries needs its own honest evidence classification.
  Current report: `artifacts/test-results/continue-20261020/all-tools-2/tool-results.json` (207 routing passes,
  one verified native case, 12 failed-session observations, 194 `not_verified` rows).
  New exact-bundle report: `artifacts/test-results/continue-20261020/all-tools-current-5/tool-results.json`
  for `continue-20261020-3`: 207 routing passes, 38 verified native cases and 169 `not_verified` rows.
  No results from different plugin/gateway bytes were pooled.
  These 38 cases must be repeated on the arity-corrected `continue-20261020-4` before they qualify that bundle.
  Those repetitions passed: `all-tools-current-6` contains 50 verified / 157 unverified rows for bundle 4,
  including 12 independently verified debugger cases. Bundle 5's independent repetitions passed in
  `all-tools-current-8`: 38 verified cases, 12 failed-session observations and 157 unverified rows.
  Its debugger crash remains unresolved; bundle 4's debugger results do not transfer to bundle 5.
- [ ] Re-run every required success case on the final exact replacement bundle, with restart and cleanup proof.
- [ ] Update the roadmap and all-tools checklist from those passing receipts. Leave environment blockers unchecked.
- [ ] Run a **fresh independent Astra subagent** for final verification; fix findings and repeat until no
  actionable finding remains. Earlier bounded reviews do not satisfy this final requirement.
- [ ] Complete required Release/Debug/formatting/fuzzing checks, publish the verified final main state and verify
  its remote commit. Do not create another beta release until the user requests it.

Pinned dependency mains at this checkpoint: Client `5c0d3057c040920665579ca3b0f464f3cd865317` (merged PR 69),
SDK `7c5b681a4d8055129d7d51b28931e2f6c6d87361` (merged PR 99). Preserve Git submodules rather than reverting to NuGet.
Package 14's frozen product version is `2.0.0-beta.4+cfb2421426f32edcc8986caa1be1896872f7a4e7`;
its successful symbol session was `20261019T184727Z-76aa`. See the all-tools README for exact hashes and failures.
