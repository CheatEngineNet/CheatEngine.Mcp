# Later kernel qualification on this PC

Status: **partial; six DBK/DBVM operations remain unqualified**. The latest read-only `kernel_get_status`
case passed on exact final bundle 06 in session `20261020T221714Z-226c` (summary SHA-256
`AD45BDDAE9D2E029FF2A68F3B31F845290A4DE12658B0E51366F93E593F7269C`; receipt SHA-256
`B032FA5B6E8C776547609120561F0F4E6551A7ABA09100F49A30EB7A7B16FBDF`). Product version:
`2.0.0-beta.4+1f987742b7fcd7760057a19508ddacbf2aad4bd8`; plugin SHA-256
`E05A449551716AC485E2C5897397D109584B94CBEC3444FD8C7461432CF0462B`; gateway SHA-256
`1FEEFE4975AE558C78932B63C93BAE7A97EB6DBA571C76402B87B63B0406467A`.

With only `kernelAccess` enabled, the typed status result reported both `dbkInitialized=false` and
`dbvmInitialized=false`; the fields were available and CR0/CR4/DBVM CR4 were zero. No owned resources or
jobs existed. A repeated query after gateway restart matched, and an independent `Win32_SystemDriver`
inventory was unchanged with zero matching drivers. User settings and the installed CE tree were
restored/preserved; there were no cleanup failures or remaining owned processes. This qualifies only
`kernel_get_status`. Earlier passing status evidence from `20261020T180540Z-55c5` is historical and does not
add another exact-bundle qualification.

On 2026-10-20 the user ruled out a VM for kernel work. The read-only host inventory reported
`HypervisorPresent=true` and `VirtualizationBasedSecurityStatus=2`. These observations do not establish
DBK/DBVM suitability. No driver, service, boot, firmware, VBS or virtualization setting was changed.
`kernel_initialize_dbvm`, `kernel_translate_address`, `kernel_read_physical`, `kernel_write_physical`,
`kernel_start_watch` and `kernel_poll_watch` remain unqualified pending the safe prerequisites below.

## Admission before arranging the run

1. The final-bundle debugger event and run-to cases now pass, but the native provider-state root cause remains
   unresolved. Freeze the exact final plugin, gateway, harness, owned fixture and CE identities. Review a
   separate kernel harness before executing it; the present live harness has no successful DBK/DBVM operation
   scenario.
2. Refresh the physical-host/CPU, Windows, hypervisor/VBS, driver-signing and installed CE/DBK/DBVM inventory.
   Compare it with the exact installed CE version's supported configuration. Do not infer compatibility
   from a portable double, an absent status value or another CE source revision.
3. Present any required configuration or driver changes and their restoration procedure for separate
   user approval. Save user work and arrange an attended maintenance window with local recovery access.
   Initialization may freeze or blue-screen the PC; process cleanup cannot undo a host crash.
4. Prepare and review the reboot/recovery procedure and before/after configuration inventory. Private CE
   copies and registry backups do not isolate machine-wide driver or hypervisor state. Record those states
   separately and require an explicit recovery check before claiming restoration.

## Owned fixture requirements

Use one disposable target with a nonce-bound identity and a dedicated resident page containing only known
fixture bytes. Keep the page alive for the entire operation. A reviewed lifetime/residency mechanism must
prevent physical-page reuse; translating twice alone does not prove that a physical write is safe.
Refuse physical writes and watches until that ownership requirement is demonstrated independently.

Publish the virtual address, page bounds, known bytes and a command/acknowledgement channel. The fixture
must independently read and report values after each operation. Keep every access within one 4 KiB page;
never select arbitrary OS, driver or unrelated process memory. Use an independent process-memory reader
and fixture receipts as oracles rather than accepting a tool's requested-state echo.

## Per-tool evidence required

| Tool | Bounded success oracle | Recovery and boundary |
| --- | --- | --- |
| `kernel_get_status` | **Passed** on exact final bundle 06 in `20261020T221714Z-226c`: typed state read, repeated state after gateway restart, empty owned resources/jobs and unchanged independent driver inventory. | Read-only case only; it did not initialize either component and does not qualify any other kernel operation. |
| `kernel_initialize_dbvm` | Distinguish confirmation of already-running DBVM (`offloadOperatingSystem=false`) from actual initialization. An attended offload case requires its own observed before/after state and human CE confirmation. | Do not automate the human crash-warning response or retry an unknown outcome. Query status only after the host responds. A confirmation-only pass does not qualify offload. |
| `kernel_translate_address` | Translate only the fixture's resident page; verify the requested virtual address and page offset, then corroborate its known content through the bounded physical read. | Refresh translation immediately before each physical access; abort if identity, lifetime or mapping changed. |
| `kernel_read_physical` | Read a small owned range and compare every byte with the fixture and independent virtual-memory read. | No unrelated physical ranges. This case requires confirmed DBVM availability. |
| `kernel_write_physical` | Save the exact owned bytes, apply one distinct fixed value, and verify it through the fixture and independent virtual read. | Restore saved bytes once under the same ownership/mapping checks, then verify both reads. An uncertain write requires observation before any further mutation. |
| `kernel_start_watch` | Arm one short, bounded write watch on the owned range; ask the fixture to perform one known write from its published instruction/thread. | Record the exact job identity and watch bounds. Require explicit cancellation/expiry and no remaining native watch or managed job. |
| `kernel_poll_watch` | Drain the native DBVM log once, then verify the non-consuming managed page and match the fixture's address/thread/instruction where supplied. Verify continuation through `afterSequence`/`nextAfterSequence` separately. | A missing/empty event is inconclusive. Do not infer watch removal from an empty poll or managed job absence alone. |

The initial proposed watch case uses write access, zero optional flags and a small entry limit. Read and
execute watches, stack-capture flags and broader behavior partitions require separate cases before any
claim that those partitions are qualified.

## Run and closeout

Enable only the required kernel capability in the owned copies and assert the other optional gates.
Use a single-use launcher with frozen inputs, an explicit kernel acknowledgement and one recorded attempt.
Keep raw responses, independent fixture observations, job/watch cleanup and host configuration receipts.
Stop after any crash, unknown mutation, ownership loss or restoration failure; retain the failed session.

Qualify a row only from its exact success and cleanup receipts in a passing session on the final product
bytes. Disabled-gate refusals, unavailable DBVM and portable Lua doubles remain separate evidence. A
machine reboot or restored registry alone does not prove that kernel/hypervisor state was restored.

This plan follows the current contracts in `srcs/CheatEngine.Mcp.Tools/Kernel/KernelTools.cs` and
`KernelScripts.cs`. Exact host prerequisites and a demonstrated page/watch recovery mechanism remain
open prerequisites for the later attended test.
