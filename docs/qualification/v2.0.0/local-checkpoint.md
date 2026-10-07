# Local roadmap implementation checkpoint

Date: 2026-10-08.
Status: Working-tree evidence, not a stable release or a clean-commit qualification.

The branch is `codex/roadmap-v2`, based on `c51a0ec3af373c852cda062a91888ad94fa0cce9`, with uncommitted implementation changes.
The binaries still identify as `2.0.0-beta.2` and embed that base commit; that identity does not identify the modified source tree.
The package hashes below identify the actual tested bytes.
A future clean candidate must repeat the final-source and final-package gates in [release-signoff.md](release-signoff.md).

## Implementation and verification

| Evidence ID | Roadmap requirements | Level | Result | Scope and limitations |
| --- | --- | --- | --- | --- |
| LOCAL-01 | Sections 4, 5 and 6 implementation | implementation | Pass | Three new tools, typed schemas, bounded behavior, capability and file-policy integration, guidance, and regression cases. Native compiler qualification remains open. |
| LOCAL-02 | Section 11 restore and portable checks | V | Pass | Locked restore; Debug and Release builds with zero warnings or errors; 4,005 portable tests in each configuration, zero failures or skips. |
| LOCAL-03 | Sections 4 and 6 fixed Lua | VN | Pass | 607 NativeLua tests, including existing debugger-capture regressions and 15 cases added after independent review; actual Lua 5.3 x64 with stubbed CE APIs. |
| LOCAL-04 | Sections 3 and 11 public contract | V+ | Pass | Reviewed additive tool snapshots, resource-size updates, both format checks, and five isolated bundle tests including negative tool/resource/prompt cases. |
| LOCAL-05 | Section 11 distributions and packaged parity | V+, AOT | Pass | Debug and Release publication; actual lone packaged DLL compared to explicit goldens in two load contexts; Native AOT executable MCP smoke checks. |
| LOCAL-06 | Section 11 local package | implementation | Pass | Five-file Release ZIP inventory, embedded dependency inventory, matching DLL/gateway version and base-commit identity, entry hashes and checksum verification. No upload or tag operation. |
| LOCAL-07 | Section 5 x64 pointers; bounded parts of sections 7 and 8 | VL | Pass | Run `20261007T225551Z-5e8e`, 23.829 seconds; two private CE instances and owned x64 targets; supplied pointer facts, batch outcomes, isolation, target restart and existing maintained smoke scenario. |
| LOCAL-08 | Section 5 x86 pointers; bounded parts of sections 7 and 8 | VL | Pass | Run `20261007T225641Z-a5c0`, 22.967 seconds; the same Release package and scenario with an independently built x86 fixture whose manifest reports 32-bit pointers. |

The full portable commands exclude `Category=LiveQualification` and `Category=NativeLua` and use `--fail-skips on`.
The existing catalog contains 193 backend tools, 43 resources, 15 resource templates, and 43 prompts; gateway discovery adds `instance_list`.
The stale embedded-documentation mismatch observed during incremental development was resolved by a nonincremental rebuild and then independently by fresh publication.

## Independent Astra review

An independent `gpt-6-astra` subagent reviewed the implementation, arithmetic, file and effect boundaries, batch cancellation and epochs, tests, packaging, native fixture cleanup, and evidence claims.
The first pass found three supplied-facts defects: accepted hexadecimal values were not normalized before Lua; EIP-relative addresses did not infer 32-bit wrapping; and stack-relative POP and bit-string memory instructions could access an address different from their bracket expression.
The fixes normalize accepted facts, narrow EIP arithmetic, and refuse unsupported POP, bit-string, and prefixed-text semantics without extending the existing capture opcode policy.
Fifteen public-tool NativeLua cases cover the fixes, and the full 607-test NativeLua suite passed.
The same independent reviewer re-reviewed the fixes and found no remaining actionable defects in scope.
That is a source and test review result, not a claim that all possible defects or outstanding native/release gates have been exhausted.

## Environment

| Component | Observed identity |
| --- | --- |
| OS | Windows 11 Pro x64, `10.0.26300` |
| Build SDK | .NET `10.0.401` |
| Native host | Cheat Engine x64 `7.7.1.10828` |
| Plugin frameworks | Microsoft.NETCore.App, Microsoft.AspNetCore.App, Microsoft.WindowsDesktop.App `10.0.12`, x64 |
| x86 fixture framework | Microsoft.NETCore.App `10.0.12`, x86 |
| Native host SHA-256 | `CF6CCC664A90C23531EC20F5C41458CFDC1D04679D6D05ED94C06BCECF95544F` |
| Lua 5.3 x64 SHA-256 | `C95DCDFA0F60F97B43D970D77FD1BB907AF4DE04B500A3C89A99600B20B35BD2` |

The Lua DLL came from the reviewed CE installation.
The existing reviewed autorun hashes were unchanged and accepted by the native harness.
No runtime, compiler, driver, dependency, or host installation was changed for these runs.

## Tested artifact SHA-256

| Artifact | SHA-256 |
| --- | --- |
| Release plugin DLL | `E6E2AE56B43CF54F1F5978B566EE762939B76E549D557FDDF8B073BB8262750B` |
| Release Native AOT gateway | `450D0E89EDABC95551689AFE277EB8FBDFF9683A8F0B4395D6293F3C74AE1FA8` |
| Debug plugin DLL | `0D95A43501EC95453E96DC7BF508CEE6A32CCCCEA3125732E5A55D8180CF9C45` |
| Debug Native AOT gateway | `B0D198A18B9955AA6AFBD917396339997CAA54EE089981A5BAEBE0C563479F61` |
| Local Release ZIP | `72B3E25DF77816DD9B5403EEB229D32350EC72DE1B92206C777E95D03321D37C` |

Distributions are under the ignored `artifacts/dist/roadmap-debug` and `artifacts/dist/roadmap-release` directories.
The local ZIP is under `artifacts/releases/roadmap-candidate`; it is a working-build artifact and is not the public beta.2 payload.
Build, test, format, publication, and native console logs are retained locally under `artifacts/issue-edits/roadmap-*.log`.
Raw native reports remain outside the repository in the harness's per-user run storage.
Only redacted results and run identifiers are recorded here.

## Native cleanup and limits

Both native runs reported `passed=true`, empty `cleanupFailures`, `userStateRestored=true`, and `sourceInstallationUnchanged=true`.
The harness stopped only its owned private CE processes and disposable targets; existing MCP client gateways were preserved.
The live scenario covers signed pointer offsets, unreadable hops, zero-address resolution with an unreadable final value, two-instance isolation, and a fresh chain after target restart.
Supplied x86 and x64 instruction facts passed through the actual gateway and plugin; this is not a live debugger-capture qualification.
The maintained scanner, memory read/write, disassembly, record-freeze, speedhack restoration, and one-host withdrawal checks also passed in both runs.
No native compiler, injection, debugger capture, kernel, or hypervisor probe ran.

## Remaining release work

The compiler's actual availability, diagnostics, prerequisites, raw temporary-file lifetime, and separate injection remain pending the [proposed compiler policy](compiler-probe-policy.md).
The broader workflow matrix, repeated activation recovery, measured performance baselines, two-hour soak, installation/upgrade/rollback, named MCP clients, CI and analysis, clean final source identity, version/tag change, and public release verification remain open.
The completed working-tree checks do not close any of those requirements.
See [release notes](release-notes.md) for the draft migration path and [release-signoff.md](release-signoff.md) for final gates.
