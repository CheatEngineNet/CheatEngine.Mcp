# Compiler qualification checkpoint

Date: 2026-10-08.
Status: Approved compiler-only phases 1 through 4 passed locally; stable release qualification remains incomplete.

## Exact tested candidate

The tested checkout was clean on `codex/roadmap-v2` at `808a626e65eddab113737a27a7c2345555b1c241`.
Both published binaries identify as `2.0.0-beta.2+808a626e65eddab113737a27a7c2345555b1c241`.
This checkpoint records those bytes; a later documentation commit is not a newly qualified binary candidate.
No push, tag, release upload, installed-plugin replacement, or managed injection was performed.

| Artifact | SHA-256 |
| --- | --- |
| Release plugin DLL | `C07B5DBE73B8AE509C091850CE0F74501028CB868D4AEE0000F07E821B5419FC` |
| Release Native AOT gateway | `7DB3AF0853D85198FE07280BCBB5A4047115D08A344FC31FB8FF6ABE5F23139D` |
| Debug plugin DLL | `93F1F3B22A177654BC092E3525985049C1AD39F250CF7F7AE905C07C42E5A610` |
| Debug Native AOT gateway | `85B423D61D20D4EED5C8E2929132D78DD2B5BC24200EF79B576653834477B740` |
| Local Release ZIP | `EB803348B3F953D89D735637CCE95F254DCA5EF66B2480CDCF72239453EBFB42` |
| Reviewed compiler policy | `C35BC2F0A21639C52D8F69088349CEAB5B49DB64C88983DED33272304EBAD3B2` |
| Fixed raw-compiler bridge template | `E42693FFD25AD257262CEA04C228D5540F4887FA8F5EC759A1E087EFA404A84B` |

The fixed source and reference hashes are checked before admission and recorded in each run summary.
They match the payloads in the [approved compiler policy](compiler-probe-policy.md).

## Build and package evidence

- Locked restore passed; Debug and Release nonincremental builds passed with zero warnings and errors.
- Portable tests passed 4,037/4,037 in each configuration, with zero failures or skips.
- NativeLua passed 611/611 using the installed Lua 5.3 x64 DLL and stubbed CE APIs.
- Both format checks passed.
- Both publications compared the actual lone packaged plugin DLL against the explicit goldens in two isolated load contexts and passed the Native AOT executable MCP smoke checks.
- The Release ZIP passed file and embedded-dependency inventory, matching binary identity, entry-hash, and checksum checks.

The catalog remains 193 backend tools, 43 resources, 15 templates, and 43 prompts; gateway discovery adds `instance_list`.
Portable commands excluded `Category=LiveQualification` and `Category=NativeLua` and used `--fail-skips on`.
Build and package logs are retained locally as `artifacts/issue-edits/compiler-qualified-*.log`.
The tested distributions are `artifacts/dist/roadmap-debug` and `artifacts/dist/roadmap-release`.
The ZIP is `artifacts/releases/compiler-qualified/CheatEngine.Mcp-2.0.0-beta.2-win-x64.zip`; it does not replace the public beta.2 release.

## Native environment and prerequisites

| Component | Observed identity |
| --- | --- |
| OS | Windows 11 Pro x64, `10.0.26300` |
| Build SDK and test runtime | .NET SDK `10.0.401`; runtime `10.0.12` |
| Plugin host frameworks | .NET, ASP.NET Core, and Windows Desktop `10.0.12`, x64 |
| x86 fixture runtime | .NET `10.0.12`, x86 |
| Cheat Engine host | x64 `7.7.1.10828`; SHA-256 `CF6CCC664A90C23531EC20F5C41458CFDC1D04679D6D05ED94C06BCECF95544F` |
| CE compiler bridge | `CSCompiler.dll` `1.0.0.0`; SHA-256 `13DCE393B59B9EF4A5D4FCDC27267D018B350BDC44A62AACC5DBC7F1DF7F7A1C` |
| Installed .NET Framework | v4 Full release `533509`, version `4.8.09221` |
| Framework C# compilers | x86 and x64 `csc.exe` `4.8.9221.0` |
| Lua 5.3 x64 | SHA-256 `C95DCDFA0F60F97B43D970D77FD1BB907AF4DE04B500A3C89A99600B20B35BD2` |

CE exposed `compileCS` and successfully compiled the fixed source on this installed prerequisite set.
This proves availability in the recorded environment, not a minimum-runtime claim or behavior when a prerequisite is absent.
The existing reviewed autorun hashes passed unchanged.
No prerequisite was installed, no source installation was changed, and no saved registry setting was changed to admit the run.
The x86/x64 labels below describe the selected disposable targets; the compiler and plugin host remained x64, and no generated assembly was loaded into either target.

## Native results

| Evidence ID | Target | Disabled-gate run | Enabled compiler run | Result |
| --- | --- | --- | --- | --- |
| COMPILER-01 | x64 | `20261007T234620Z-a6d5` | `20261007T234637Z-777e` | Pass; combined test 40.443 seconds. |
| COMPILER-02 | x86 | `20261007T234723Z-ba94` | `20261007T234739Z-96c7` | Pass; combined test 41.210 seconds. |

Each disabled-gate run refused before compiler dispatch and produced no output or temporary-tree change.
Exclusive CE scan leases were compared by exact path and four-byte length; their unreadable contents were not claimed as hashed evidence.
Each enabled run compiled the valid fixed source once and immediately exported a 3,072-byte PE file whose length and SHA-256 matched the result.
Instance B stayed responsive and its compiler directories remained unchanged during A's compiler calls.
Invalid source, invalid reference, and invalid core assembly each returned the expected `host_refused` / `hostEffect=unknown` diagnostic with no published output or protected partial file.
Their diagnostic lengths were 16, 241, and 241 UTF-8 bytes respectively in both target runs, without truncation.
Missing reference and missing core assembly were refused as `not_found` / `hostEffect=not_started` without a compiler receipt or temporary-tree change.

| Target | Export SHA-256 | Separate raw artifact SHA-256 |
| --- | --- | --- |
| x64 | `64CAEF72C3096D846301D3E9AE700EEA427DE5267A4B83CB958682FB3769ACF8` | `B5F3AC1BF2D8CC006B02EB2EE7ED85861AC999FFB503F2E06988FF58A681E7B0` |
| x86 | `59ECF4F9EFF2A9AEC4BFE811E37548FD3E99E5B50E268146145EB42E5892859B` | `50630CFAA9D4B2A07D78DEE0D53A043EAA4A5AD54FEB6DEAB18952E61051DA93` |

Both runs classified raw lifetime as `retained_after_a`: the separate raw artifact retained its original hash after B closed and after A closed, at the policy's two-second observation intervals.
The exported file independently retained its own original hash after both shutdowns.
A and B used distinct temporary roots; these observations do not qualify shared-temp cleanup, reload, eventual expiry, or missing-artifact behavior.
The harness subsequently removed the owned compiler files only after all owned processes stopped and restoration succeeded.
All four run summaries report `passed=true`, empty `cleanupFailures`, `userStateRestored=true`, `sourceInstallationUnchanged=true`, and `ownedCompilerFilesRemoved=true`.
Preexisting MCP gateways were preserved, and no owned CE host or fixture remained after the runs.
No generated assembly was injected, invoked, or executed.

## Refused attempts and path diagnosis

Run `20261007T232735Z-e269` stopped before a compiler call because CE's exclusive scan lease could not be hashed.
The harness now records only the exact known lease's path, length, and exclusive status, while every other unreadable file still stops qualification.
The correction has seven portable regression cases and independent review.
Run `20261007T233614Z-351e` passed the disabled gate; enabled run `20261007T233631Z-d1f6` then refused its output path before compiler admission.
Read-only native probes established that the desktop app's packaged-app filesystem redirected the logical LocalApplicationData path into its package cache, despite ordinary directory attributes and no filesystem reparse tag.
The existing secure path policy correctly refused the redirect.
The successful runs used the existing `CHEATENGINE_MCP_LIVE_QUALIFICATION_RUN_ROOT` override with the verified physical directory; the unchanged anchored walk succeeded through every component.
No production path restriction was weakened, no additional compiler call was replayed, and all stopped sessions restored state and removed owned compiler files successfully.
The earlier attempts had zero compiler receipts.

## Review and remaining work

Independent Astra review covered the candidate and compiler harness, including exact opt-in, fixed payloads, candidate identity, request publication, response bounds, path containment, exclusive leases, cleanup, and evidence claims.
Review findings were fixed before the successful native runs, with portable and NativeLua regressions.
The final independent Astra pass found no actionable findings in the completed scope after checking all four summaries and validation logs and independently recomputing the artifact hashes.
This review does not claim the absence of all possible defects or completion of the open qualification and release gates.

The feasibility and installed-prerequisite roadmap item is complete for the recorded CE build.
Unavailable compiler or absent-prerequisite behavior, shared-temp lifetime and expiry, reload, delayed export after closing B, and separate managed injection remain unqualified.
Optional phase 5 has not been approved.
The broader lifecycle/domain matrix, measured performance baselines, two-hour soak, installation/upgrade/rollback, supported MCP clients, CI/analysis, final stable source and package, and public release remain open.
The earlier pointer/native smoke evidence is retained separately in [local-checkpoint.md](local-checkpoint.md) and must not be attributed to these compiler-only runs.
