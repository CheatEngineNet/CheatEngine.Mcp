# Stable v2.0.0 support matrix

Status: Draft scope for implementation and qualification.

This document defines the minimum intended stable boundary and does not claim that the release candidate has passed qualification.
The final supported and qualified rows require evidence from the exact release candidate package.

## Product boundary

- The stable package is Windows x64 only.
- The Cheat Engine plugin host is x64 only.
- The gateway is the self-contained `win-x64` executable from the matching package.
- The plugin requires the x64 .NET 10, ASP.NET Core 10, and Windows Desktop 10 runtimes.
- The plugin and gateway must come from one build and must report matching version and source identity.
- External debugger and decompiler integration is outside the stable v2.0.0 boundary.
- General desktop automation and target-application UI automation are outside the stable v2.0.0 boundary.

## Minimum compatibility matrix

| Component | Intended stable boundary | Candidate qualification requirement | Current status |
| --- | --- | --- | --- |
| Windows | Windows x64 | Record the exact edition, version, build, and architecture used for every native, soak, installation, and release result. | Pending qualification. |
| Cheat Engine host | Cheat Engine 7.7 x64 | Qualify the exact installed host through the harness fingerprint and reviewed autorun checks. | Baseline only. |
| Installed candidate environment | Cheat Engine 7.7.1.10828 x64 | Run the exact candidate package and record the dynamic installation fingerprint, reviewed autorun result, and cleanup. | Local working package passed both native fixture runs; final clean candidate pending. |
| Plugin runtime | `Microsoft.NETCore.App`, `Microsoft.AspNetCore.App`, and `Microsoft.WindowsDesktop.App` 10.0.x x64 | Record every exact runtime patch used by the candidate qualification. | Pending qualification. |
| Gateway runtime | Self-contained `win-x64` Native AOT executable | Run the executable MCP smoke check from the exact candidate distribution. | Local Debug and Release executables passed; final clean candidate pending. |
| Target architectures | x86 and x64 Windows user-mode targets | Exercise independent disposable fixtures for each architecture and record pointer width, target identity, and cleanup. | Both local fixture architectures passed the maintained scenario and restart checks. |
| MCP transport | Local stdio gateway to authenticated per-activation loopback backends | Verify explicit `instanceId` routing, stale-instance refusal, and two-instance isolation. | Pending candidate qualification. |
| MCP clients | Clients named by the release documentation | Run packaged first-use registration, discovery, selection, resources, prompts, completions, errors, and cleanup in every named client. | No client is qualified by this draft. |

The exact Windows build used during planning is `10.0.26300`, but that fact is not candidate qualification.
The local working package passed the bounded native cases recorded in [local-checkpoint.md](local-checkpoint.md).
That evidence must not be generalized to every advertised domain or to an untested final stable package.
The live harness already accepts and fingerprints an installed Cheat Engine 7.7-or-newer x64 host dynamically, so no new static profile is required for this build.

## Capability classification

Every advertised domain must have one of the following statuses in the final release evidence.

- `qualified` means the exact candidate package passed its required portable and native cases on the supported environment.
- `optional_environment` means the contract and refusal paths are stable, but successful use depends on an installed runtime, collector, driver, hypervisor, compiler, or target property.
- `experimental` means the typed contract is available but its native coverage or operating boundary is intentionally narrower than the stable promise.
- `unsupported` means the release documents the boundary and does not advertise successful operation.

Core routing, process selection, memory, scans, AOB, pointers, modules, symbols, code analysis, structures, records, tables, user-mode debugger, Lua, and speedhack are intended for qualification.
.NET, Mono, IL2CPP, debugger interfaces, compilation, injection, managed invocation, kernel, and DBVM capabilities require separate environment and policy evidence.
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
Configuration keeps the current precedence of built-in defaults, deployment settings, user settings, and dedicated environment overrides.
The four capability switches remain exposure controls and do not become a sandbox.
File and table roots remain empty by default.
Migration from beta.2 must preserve user configuration, tables, unrelated plugins, Cheat Engine settings, and rollback paths.
The beta tags remain unchanged, and stable publication uses a new `v2.0.0` tag at the exact final binary source commit.

## Release-stopping defects

The release stops for a crash, corruption, cross-instance effect, cross-target effect, stale-epoch work, unreported partial or unknown mutation, leaked owned state, broken install or upgrade, incorrect package identity, or wrong success reporting.
The release also stops when cleanup claims success after an orphan, a destructive capability bypasses its gate, an uncertain mutation is replayed automatically, or a supported workflow cannot be recovered using its documented path.
An optional capability that is honestly unavailable is not a release-stopping defect when its refusal and limitation match the reviewed contract.
