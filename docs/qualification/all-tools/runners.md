# Local qualification runners

The maintained live test selects a fixed workload using `CHEATENGINE_MCP_LIVE_QUALIFICATION_SCENARIO`.
Every scenario requires the ordinary explicit live acknowledgement, private CE copies, owned fixtures, and successful user-state restoration.
The scenario selection is checked before starting a host.
Implementation of a runner does not constitute a passing qualification result.

| Scenario | Fixed workload | Evidence boundary |
| --- | --- | --- |
| `smoke` | Existing two-instance pointer, scanner, record, and shutdown checks, plus packaged resources and prompts. | Existing smoke admission and capability settings apply. |
| `lifecycle` | Six bounded memory-analysis workflows, three in-process plugin cycles, target restart, gateway restart, and one private CE restart. | All four optional capability gates are disabled. Named scans require the existing MEM_PRIVATE preference. |
| `performance` | Three hash-pinned published beta.2 baselines and three candidate runs, each with 100 single reads and 30 batches of 16 reads after warmup. | Only these two workload profiles are compared; this does not qualify job latency or the whole catalog. |
| `soak` | Two hours, 1,000 reads, 20 bounded non-executing decode jobs, 30 plugin cycles, ten owned target restarts, and one gateway restart. | Uses 135-minute owned lifetimes. Both CE processes must remain within the documented handle/private-byte budgets after equal 15-second idle intervals. |
| `compiler` | Reviewed compiler-only phases 1 through 4. | Requires the separate compiler acknowledgement and [approved policy](compiler-probe-policy.md). |
| `compiler-extended` | Private missing compiler component, shared temporary directory, bounded delayed export after B shutdown, and actual plugin reload lifetime observation. | Prepared only; the [extended policy](compiler-extended-policy.md) still requires its own reviewed admission. |
| `compiler-injection` | The fixed compiled entry point in an owned .NET Framework target after B shutdown and verified export. | Prepared only; requires the additional managed-injection acknowledgement and explicit approval. |
| `process-create` | One hash-pinned owned .NET Framework fixture launched through `process_create`, which publishes an owned manifest and waits for its stop marker. | Requires the standard live acknowledgement, all four optional gates disabled, and the installed .NET Framework compiler prerequisite. Its operation and cleanup receipts belong to this isolated session. |
| `mono` | The 15 Mono tools against one pinned primitive object in a private x64 Mono runtime, with fixed method calls and collector/job/target cleanup. | Requires the separate fixed-Mono acknowledgement and `eng/Build-LiveMonoTarget.ps1`. Only target-code execution is enabled. The runtime is privately extracted; no global installation is required. |

The lifecycle workflow uses data allocations that it frees, private named scanner state that it deletes, and a temporary global structure that it removes.
Synthetic instruction bytes remain ordinary writable data and are never invoked.
File-policy refusal checks require no output file to be created.
An error or failed cleanup cannot be counted as a pass.

The performance baseline must first be downloaded from the existing `v2.0.0-beta.2` release and verified with `eng/Stage-QualificationPackage.ps1` into `artifacts/qualification-baseline/beta.2/staged`.
The runner checks the baseline version and both executable hashes before launch.
The staging script accepts only a reviewed ZIP hash, a new destination, and the five expected flat distribution files.
It never executes the extracted binaries.

The separately prepared Framework fixture lives under `eng/LiveFrameworkTarget` and builds with `eng/Build-LiveFrameworkTarget.ps1`.
It uses the installed Framework compiler and adds no package dependency or solution project.
Fixture identity records the source, compiler, executable, and architecture; injection preparation checks the source and executable before starting it.
The fixture must remain alive until its explicit stop marker, exit with code zero, and be disposed before the scenario can pass.

The Mono scenario additionally requires `CHEATENGINE_MCP_LIVE_MONO_QUALIFICATION` to contain
`I_AUTHORIZE_FIXED_MONO_COLLECTOR_AND_METHODS_ON_A_PRIVATE_OWNED_TARGET`.
It invokes `StaticAdd42(0)` and `InstanceBumpCounter(7)` once each, verifies their results and the instance
effect, and never retries an uncertain invocation. The private runtime executable, native library,
configuration and core assembly must match the reviewed hashes before launch.

`eng/Write-AllToolQualificationReport.ps1` accepts a PowerShell array through `-LiveSummaries` for
cumulative evidence from separate scenarios. All supplied sessions must select the same product version
and identical plugin/gateway hashes. A tool's operation and cleanup receipts must each occur exactly once
in the same passing session. Failed and diagnostic-only sessions supply observations, never verified results.

Use an unused distribution directory to preserve any gateway currently used by an MCP client.
Raw reports stay in the private run directory; committed evidence must redact personal paths and exclude discovery tokens.
Use the [current checklist](../../../TODO.md) and the per-tool evidence maps to record actual results.
