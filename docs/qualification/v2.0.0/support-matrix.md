# Stable v2.0.0 support matrix

Status: Reviewed stable-scope proposal; exact release-candidate qualification remains incomplete.

This document defines the minimum intended stable boundary and does not claim that the release candidate has passed qualification.
The final supported and qualified rows require evidence from the exact release candidate package.
The current 121-item status and ownership crosswalk is in [requirement-matrix.md](requirement-matrix.md).

## Product boundary

- The observed qualification baseline is Windows 11 Pro x64 build `10.0.26300`, and no broader Windows version is currently claimed as qualified.
- The Cheat Engine plugin host is x64 only.
- The gateway is the self-contained `win-x64` executable from the matching package.
- The plugin requires the x64 .NET 10, ASP.NET Core 10, and Windows Desktop 10 runtimes.
- The plugin and gateway must come from one build and must report matching version and source identity.
- External debugger and decompiler integration is outside the stable v2.0.0 boundary.
- General desktop automation and target-application UI automation are outside the stable v2.0.0 boundary.

## Minimum compatibility matrix

| Component | Intended stable boundary | Candidate qualification requirement | Current status |
| --- | --- | --- | --- |
| Windows | Windows 11 Pro x64 build `10.0.26300` is the only observed candidate environment | Record the exact edition, version, build, and architecture used for every native, soak, installation, and release result; broader Windows x64 versions remain unqualified until separately evidenced. | Local candidate environment observed; final package pending. |
| Cheat Engine host | Cheat Engine 7.7 x64 | Qualify the exact installed host through the harness fingerprint and reviewed autorun checks. | Baseline only. |
| Installed candidate environment | Cheat Engine 7.7.1.10828 x64 | Run the exact candidate package and record the dynamic installation fingerprint, reviewed autorun result, and cleanup. | Local working package passed both native fixture runs; final clean candidate pending. |
| Plugin runtime | `Microsoft.NETCore.App`, `Microsoft.AspNetCore.App`, and `Microsoft.WindowsDesktop.App` 10.0.x x64 | Record every exact runtime patch used by the candidate qualification. | Pending qualification. |
| Gateway runtime | Self-contained `win-x64` Native AOT executable | Run the executable MCP smoke check from the exact candidate distribution. | Local Debug and Release executables passed; final clean candidate pending. |
| Target architectures | x86 and x64 Windows user-mode targets | Exercise independent disposable fixtures for each architecture and record pointer width, target identity, and cleanup. | Both local fixture architectures passed the maintained scenario and restart checks. |
| MCP transport | Local stdio gateway to authenticated per-activation loopback backends | Verify explicit `instanceId` routing, stale-instance refusal, and two-instance isolation. | Pending candidate qualification. |
| MCP clients | Clients named by the release documentation | Run packaged first-use registration, discovery, selection, resources, prompts, completions, errors, and cleanup in every named client. | No client is qualified by this draft. |

The exact Windows build used during planning and bounded candidate checks is `10.0.26300`, but that fact is not final-package qualification.
The local working package passed the bounded native cases recorded in [local-checkpoint.md](local-checkpoint.md).
That evidence must not be generalized to every advertised domain or to an untested final stable package.
The live harness already accepts and fingerprints an installed Cheat Engine 7.7-or-newer x64 host dynamically, so no new static profile is required for this build.

## Capability classification

Every advertised domain has a current classification below, and the final release evidence must update the evidence without silently broadening the classification.

- `qualified` means the exact candidate package passed its required portable and native cases on the supported environment.
- `optional_environment` means the contract and refusal paths are stable, but successful use depends on an installed runtime, collector, driver, hypervisor, compiler, or target property.
- `experimental` means the typed contract is available but its native coverage or operating boundary is intentionally narrower than the stable promise.
- `unsupported` means the release documents the boundary and does not advertise successful operation.

`experimental` in this table means that the typed contract is present but the full roadmap workflow row has not passed on an exact stable candidate.

| Advertised domain | Current classification | Evidence and limitation |
| --- | --- | --- |
| AOB | experimental | Portable coverage exists, but the complete module, range, protection, alignment, mapped-memory, uniqueness, and real-fixture signature matrix has not passed. |
| Auto Assembler | optional_environment | Generation and portable gate behavior exist, while apply and cleanup require the capability gate, a target, and a separately reviewed execution policy for native qualification. |
| Code analysis | experimental | The maintained live smoke covered disassembly columns only, so functions, CFG, strings, references, dissection jobs, cancellation, and limits remain unqualified. |
| Debugger | optional_environment | Portable and NativeLua coverage exists, but debugger interfaces, capture, register mutation, stepping, tracing, and cleanup need a reviewed debugger policy and native fixture. |
| .NET | optional_environment | The domain depends on a compatible managed target and collector, and no controlled managed fixture has completed the roadmap matrix. |
| Execution | optional_environment | Calls, C compilation, injection, and invocation require target-code-execution and separate policies; only bounded C# compiler phases 1 through 4 have passed. |
| Instance discovery | experimental | Portable routing and the historical two-instance smoke passed, but the exact final gateway still needs stale-record, same-name, withdrawal, and client qualification. |
| Kernel and DBVM | optional_environment | Typed refusal paths exist, but unavailable hardware or drivers do not qualify successful translation, physical-memory, or watch behavior. |
| Lua | experimental | Fixed Lua has broad NativeLua evidence, while unrestricted `lua_execute`, main-thread behavior, and lifecycle transitions have not completed native qualification. |
| Memory | experimental | The maintained live smoke proved bounded scalar read and write cases, but the full scalar, array, batch, string, byte-order, unreadable-page, and partial-write matrix remains open. |
| Modules | experimental | Portable coverage exists, but the complete PE, import, export, and patch comparison matrix has not passed on the stable candidate. |
| Mono and IL2CPP | optional_environment | Collector availability, target runtime, attachment, object layout, searches, and invocation require controlled fixtures and a separate invocation policy. |
| Pointers | experimental | `pointer_get_access_info` and `pointer_read_chains` have bounded x86 and x64 evidence, but pointer maps, rescans, incomplete captures, and native file fidelity remain open. |
| Processes | experimental | Selection and target restart have bounded live evidence, but create, open-file, pause, pointer-width override, and full recovery behavior remain open. |
| Records | experimental | The maintained live smoke proved create, update, freeze, unfreeze, and delete on an owned record, but the full hierarchy, dropdown, script, rollback, and preservation matrix remains open. |
| Runtime | experimental | Portable lifecycle and resource-ledger coverage exists, but repeated native activation, all ownership classes, failed cleanup, and full recovery remain open. |
| Scans | experimental | The maintained live smoke covered main and independent scanner isolation, but every value mode, range, cancellation, mapped-memory, and lifecycle case remains open. |
| Speedhack | optional_environment | One owned x86 and x64 fixture scenario proved speed restoration and cross-instance isolation, while supported-target boundaries and target switching remain open. |
| Structures | experimental | Portable coverage exists, but native autoguess, nested layout, PDB and .NET fill, read and write, header generation, and CE-owned persistence remain open. |
| Symbols | experimental | Portable coverage exists, but exact resolution, PDB layouts, reload, source state, and registered-symbol ownership need candidate qualification. |
| Tables | experimental | File policy and portable coverage exist, but load, save, merge, malformed content, scripts, rollback, partial effects, and user-owned preservation remain open. |
| Utilities | qualified | The pure managed calculation and value-conversion contract passed the clean `808a626e65eddab113737a27a7c2345555b1c241` portable candidate; the final stable commit must repeat the portable suite. |

No advertised domain other than Utilities is classified as fully qualified by the current evidence.
The broad native smoke is evidence only for the cases named above and is not a domain-wide pass.
Kernel and DBVM unavailability on ordinary Windows configurations is an expected supported outcome and is not evidence that successful kernel operation works.
LBR, shared allocation, DBK map or unmap, caller-selected CR3 access, raw kernel allocation, and broader native-host support remain unsupported unless separately designed and qualified.

## P1 stable inclusion

All three P1 features are intended for stable v2.0.0 when their acceptance criteria in [acceptance.md](acceptance.md) are met.
`pointer_get_access_info` and `pointer_read_chains` are included in the planned stable contract.
`exec_compile_csharp` passed bounded compiler feasibility on the clean local candidate recorded in [compiler-checkpoint.md](compiler-checkpoint.md).
Unavailable-compiler, shared-temp expiry, reload, separate injection, and final stable-package qualification remain open.
If a P1 feature proves infeasible, it requires an explicit linked follow-up, the recorded blocking evidence, and a reviewed roadmap decision before contract freeze.
No P1 feature may be silently deferred to fit a work session or release schedule.

## Compatibility policy

The stable v2.0.0 contract freezes tool names, arguments, result and error shapes, resource URIs, prompts, and gateway routing semantics after the selected P1 changes land.
Additive optional fields and new tools may be added in compatible v2 patch or minor work when old callers keep their behavior.
Removing or renaming a tool, changing an existing field meaning, changing pointer-chain order, or changing host-effect reporting requires a future reviewed breaking contract.
Future deprecations require release-note notice, keep the old behavior through the current major version, and remove it only in a reviewed breaking release.
Configuration keeps the current precedence of built-in defaults, deployment settings, user settings, and dedicated environment overrides.
The four capability switches remain exposure controls and do not become a sandbox.
File and table roots remain empty by default.
Migration from beta.2 must preserve user configuration, tables, unrelated plugins, Cheat Engine settings, and rollback paths.
The beta tags remain unchanged, and stable publication uses a new `v2.0.0` tag at the exact final binary source commit.

## Release-stopping defects

The release stops for a crash, corruption, cross-instance effect, cross-target effect, stale-epoch work, unreported partial or unknown mutation, leaked owned state, broken install or upgrade, incorrect package identity, or wrong success reporting.
The release also stops when cleanup claims success after an orphan, a destructive capability bypasses its gate, an uncertain mutation is replayed automatically, or a supported workflow cannot be recovered using its documented path.
An optional capability that is honestly unavailable is not a release-stopping defect when its refusal and limitation match the reviewed contract.
