# External connection design

Status: integration contract. The shipped backend remains local to Windows. This document does not introduce a
network listener, a connection-profile tool, or an automatic companion-tool connection.

## Transport and profiles

A profile is an MCP client's named server configuration. The profile launches one gateway on the same Windows
machine as Cheat Engine. The gateway discovers only that machine's local instances; backend ports and discovery
credentials stay on that machine.

| Client location | Gateway location | Transport boundary |
| --- | --- | --- |
| Windows desktop | The same Windows machine | Existing stdio gateway command |
| WSL on that machine | Windows, using an explicitly configured Windows executable | WSL interoperability carries stdio; Windows paths and Windows process identities remain authoritative |
| Another machine | The Windows CE host | An authenticated SSH session starts the gateway and carries stdio; no forwarding of backend HTTP ports |

The remote transport must provide a clean binary-safe stdin/stdout stream and separate stderr. Shell banners,
terminal control sequences, interactive prompts, and profile output cannot be mixed into MCP stdout. A remote
launcher must be explicitly configured by the user; a tool response must never supply the command to launch.

The initial integration uses the client's existing server configurations rather than adding credentials or remote
command execution to the plugin. Existing gateway options remain `--instance-directory` and
`--call-timeout-seconds`. This design adds no command-line flags.

## Identity and authentication

The client identifies an observation by `(profileId, hostIdentity, instanceId, targetProcessId, selectionEpoch)`.
`profileId` is a stable local name; the authenticated transport supplies `hostIdentity`. A display name or PID is
never sufficient to choose a connection. Every routed operation still requires its exact `instanceId`, and a fresh
plugin activation requires rediscovery.

SSH authentication and host-key verification belong to the transport. Profiles store a destination and credential
reference, never a copied private key, password, backend token, or discovery record. Unknown or changed host keys
require the client's normal explicit trust decision. The existing per-activation backend authentication remains
between the remote gateway and the remote plugin on loopback.

WSL is not a separate CE host: the Windows gateway runs with the explicitly chosen Windows account. Windows
process IDs, module paths, write roots, and permissions apply. Linux `/proc` PIDs and `/mnt/...` paths must not be
substituted into tool arguments automatically.

## Reconnection and effects

After transport loss, a call that was sent has an unknown effect until independently checked. Reconnection must
not replay writes, attachment, process creation, allocation, code execution, or resource release. The client starts
a fresh MCP session, lists instances, confirms the selected host and target, and inspects retained jobs/resources.
An old instance ID is never silently replaced by the only available instance.

Read-only calls may be repeated explicitly after reconnection, but their results are new observations. Snapshot
IDs, cursors, imported datasets, jobs and leases belong to their original activation. A restart invalidates them.
Durable notes retain historical build identities; resolving an address again requires matching the current module.

## Companion analysis tools

Debugger and decompiler integrations run as separately configured MCP servers. Their outputs are observations,
not commands to execute in CE. A companion observation carries its profile/host identity, input-file digest, module
build identity, pointer width, RVA or file offset, and source timestamp. RVAs and file offsets are distinct types.

Before using an RVA with a live target, match the module name, width and build metadata and validate its image
range. A PE build-metadata fingerprint supports build matching but is not proof of byte-for-byte integrity. When
integrity matters, retain a separate original-file SHA-256 and explicitly verify the relevant bytes. An absolute
address copied from another host or process is never reused directly.

The first interchange paths are offline dump imports, structure-definition files and exported investigation notes.
No companion may silently change CE's selected target, enable capability gates, or approve a mutation. Automatic
live decompiler/debugger synchronization requires a separately implemented protocol and qualification fixtures.

## Integration acceptance matrix

These are required checks before a transport integration can be advertised as qualified. They are not receipts of
remote or WSL execution in this release.

| Scenario | Required result |
| --- | --- |
| Windows, WSL and SSH startup | MCP initialize/discovery works through clean stdio; stderr stays separate |
| Two profiles with the same display name/PID | Explicit profile, authenticated host and instance select the correct target |
| Unknown/changed host key or failed authentication | No MCP operation is sent; credentials are absent from logs/results |
| Lost connection before, during and after a write | No automatic replay; unknown effects are reported and independently inspected |
| Plugin/gateway/target restart | Rediscovery and target checks occur; stale identities, cursors and jobs are refused |
| Companion module mismatch or out-of-image RVA | No live address is produced and no target mutation occurs |
| WSL path/PID confusion | Invalid foreign paths/IDs are refused rather than translated implicitly |
| Completed fixture | Owned mutations are restored, resources released and both transport ends closed |

Remote/WSL transport execution and live companion synchronization remain unqualified until these environment
checks have receipts. The local gateway's routing and stale-identity tests cover the underlying local contract only.
