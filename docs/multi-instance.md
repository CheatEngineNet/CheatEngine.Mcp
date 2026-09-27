# Several Cheat Engine instances

One gateway can drive many Cheat Engine (CE) processes at once.
Each CE process loads its own copy of the plugin, with its own backend, its own target and its own state; the gateway routes every call to exactly one of them.
[architecture.md](architecture.md) shows the routing in detail.

## How it works

1. Each CE process with the plugin enabled starts a backend on `127.0.0.1`, on a free port chosen at enable (`Mcp:Port` is `0` by default).
2. The backend writes a discovery record into the shared registry directory, then shows **MCP: Enabled**.
3. The gateway, started by your AI client, reads the registry, verifies each backend and lists the live ones through `instance_list`.
4. Every other tool takes a required `instanceId`. The gateway checks that instance's identity again and forwards the call to it alone.

You register the gateway once in your client, whatever the number of CE processes ([clients.md](clients.md)).

## Instance IDs

`instance_list` returns one entry per verified instance: its `instanceId`, display `name`, CE `processId` and plugin version.
It also returns `discoveryIncomplete`, which is `true` when the discovery deadline expired before every record was checked; call it again before you conclude that an instance is missing.

An `instanceId` has the form `ce-<CE process ID>-<activation>`, for example `ce-8412-5b0e3c7a9d214f6e8a1b2c3d4e5f6a7b`.

- It is **immutable** for one plugin activation and unique across instances.
- It **changes** whenever CE restarts or the plugin is disabled and enabled again. Every job, patch, record, scanner or pointer-map ID from the old activation becomes invalid too.
- A call with a stale ID fails with `instance_unavailable`. Call `instance_list` again and re-orient on the new ID.

The `processId` in `instance_list` is Cheat Engine's own process ID; the target's process ID comes from `process_get_current`.

## Explicit routing only

The gateway never guesses where a call should go:

- **No default instance.** Every CE tool needs `instanceId`, even when only one CE is running. There is no "selected instance" setting.
- **No fallback.** A call to an instance that stopped or cannot be verified fails; it is never redirected to another instance, even one with the same name.
- **No retry.** The gateway never repeats a call. After a failure or timeout, a mutation may or may not have happened; the agent must read the state of the same instance before deciding what to do.

The same rule applies to resources: live resources carry the instance in their URI, `cheatengine://instances/{instanceId}/...`, and `cheatengine://instances` lists the instances like `instance_list`.
Prompts and the `cheatengine://docs/...` guides are served by the gateway itself and name no instance.

## Display names

A display name helps you and the agent tell instances apart; it is not an identifier.

- The default name is `Cheat Engine <CE process ID>`.
- Names may repeat. Two instances can both be called `game-a`; always route by `instanceId`.
- A name is at most 128 characters.

To give each CE its own name, set `MCP_INSTANCE_NAME` in the environment that launches it.
Each PowerShell window below starts one CE; adjust the path to your installation:

```powershell
# Window A
$env:MCP_INSTANCE_NAME = 'game-a'
& 'C:\Program Files\Cheat Engine\cheatengine-x86_64.exe'
```

```powershell
# Window B
$env:MCP_INSTANCE_NAME = 'game-b'
& 'C:\Program Files\Cheat Engine\cheatengine-x86_64.exe'
```

The variable applies only to processes started from that window; it does not rename a CE that is already running.
The `Mcp:InstanceName` setting also works, but the user `appsettings.json` is shared by every CE of the same user; for per-instance settings files, give each CE its own absolute `MCP_DATA_DIRECTORY` ([configuration.md](configuration.md)).
A new name takes effect at the next enable.

## Ports

Leave `Mcp:Port` at `0`: each backend then gets its own free port and nothing can collide.
A fixed nonzero port is allowed, but it must be unique among running instances; a second CE with the same port fails to start its backend and shows **MCP: Start failed**.
The host is always `127.0.0.1`.

## The discovery registry

Each enabled plugin writes one record, named after its activation, into the registry directory:

```text
%LOCALAPPDATA%\CheatEngine.Mcp\instances
```

A record holds the instance ID, name, CE process ID and start time, plugin version, loopback endpoint and a per-activation bearer token.
Disabling the plugin withdraws its record.

The gateway treats a record only as a candidate.
It ignores records whose CE process has exited or was replaced (same PID, different start time), then asks the backend for its identity over the authenticated loopback connection and compares it with the record.
It repeats that check before every forwarded call, so a record left behind by a crash or a recycled process ID is never routed to.

### Moving the registry

The plugins and the gateway must use the **same absolute directory**, and both must run as the same Windows user.

| Component | How to set it |
| --- | --- |
| Plugin | `MCP_INSTANCE_DIRECTORY` in CE's launch environment, or the `Mcp:InstanceDirectory` setting |
| Gateway | `--instance-directory <path>` argument, or `MCP_INSTANCE_DIRECTORY` in the client's server entry |

A relative path is rejected by both.
If they disagree, the plugins publish where the gateway does not look, and `instance_list` stays empty.
[clients.md](clients.md#gateway-options) shows how to pass the gateway option in each client.

## Tokens stay private

Each activation generates a new random bearer token, and the backend accepts only authenticated requests.
The gateway reads the token from the registry and uses it only to talk to that backend:

- Tokens never appear in tool results, `instance_list`, resources, completions or logs.
- Do not open, copy or paste registry records into chats, issues or reports.
- The token keeps unauthenticated HTTP clients out. It is not a boundary against other programs running as the same Windows user, which can read the registry directory; see [security.md](security.md).

## Isolation and shared targets

Separate CE processes have separate CE state: target selection, address list, scan tabs, structures, debugger, jobs and leases.
A call to one instance never touches another instance's state.

Two instances attached to the **same target process** still share that target's memory:

- A freeze in one instance fights writes from the other.
- Patches, injected code and breakpoints from both land in the same process and can overlap or conflict.
- Cleanup in one instance does not see the other's changes.

Prefer one CE instance per target.
If two must share a target, coordinate their changes explicitly and clean up each instance separately.

## Stopping one instance

Disabling the plugin or closing one CE removes only that instance.
Its calls fail with `instance_unavailable` while the other instances keep working.
Before you disable, run the session's cleanup on that instance: the plugin releases the leases it owns, but CE-owned changes such as address-list records, freezes, breakpoints and speedhack survive a disable.
