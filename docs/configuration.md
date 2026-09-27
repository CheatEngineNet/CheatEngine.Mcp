# Configuration

CheatEngine.Mcp reads one immutable configuration for each plugin activation.
Disable and enable the plugin after changing a setting.
The gateway reads its command line and environment when it starts, so restart the MCP client after changing gateway settings.

The default installation needs no configuration file.
The shipped defaults keep the backend on loopback with an automatic port, allow the four capability groups, and deny host-file and table-file access until a root is configured.

## Plugin settings

The plugin reads settings in this order, with later values taking precedence:

1. `appsettings.json` beside `CheatEngine.Mcp.Plugin.dll`.
2. `%APPDATA%\CheatEngine.Mcp\appsettings.json`, or `appsettings.json` under `MCP_DATA_DIRECTORY`.
3. The documented `MCP_*` environment variables below.

The deployment file is replaced by an update.
Put local settings in the user data directory so they survive a replacement of the plugin folder.
The plugin does not reload any source while it is enabled.

This is a minimal user settings file that gives the instance a label and permits writes only to one dedicated folder:

```json
{
  "Mcp": {
    "InstanceName": "CE tutorial",
    "Files": {
      "AllowedRoots": ["C:\\Users\\me\\Documents\\CheatEngine.Mcp-output"]
    }
  },
  "CheatEngineClient": {
    "AllowedTableRoots": ["C:\\Users\\me\\Documents\\CheatTables"]
  }
}
```

### Backend and discovery

| Key | Default | Rules and effect |
|---|---:|---|
| `Mcp:Host` | `127.0.0.1` | Must be exactly `127.0.0.1`. The backend never accepts a LAN address. |
| `Mcp:Port` | `0` | Integer from 0 through 65535. Zero selects a free local port. |
| `Mcp:ServerName` | `CheatEngine.Mcp` | The MCP `serverInfo.name`. It cannot be empty. |
| `Mcp:InstanceName` | `Cheat Engine <CE PID>` | Display label returned by `instance_list`. It may repeat and is at most 128 characters. |
| `Mcp:InstanceDirectory` | `%LOCALAPPDATA%\CheatEngine.Mcp\instances` | Absolute directory containing private discovery records. The gateway must use the same directory. |

The plugin maps only these backend and discovery environment variables onto `Mcp` settings:

| Environment variable | Equivalent key |
|---|---|
| `MCP_HOST` | `Mcp:Host` |
| `MCP_PORT` | `Mcp:Port` |
| `MCP_INSTANCE_NAME` | `Mcp:InstanceName` |
| `MCP_INSTANCE_DIRECTORY` | `Mcp:InstanceDirectory` |

`MCP_PORT` must be an integer.
Invalid values stop the plugin before its backend is published.
The backend ignores ambient ASP.NET Core hosting variables such as `ASPNETCORE_URLS` and `Kestrel__Endpoints__*`.

### User data and logs

`MCP_DATA_DIRECTORY` selects an absolute directory for the user settings file and plugin logs.
Without it, the data directory is `%APPDATA%\CheatEngine.Mcp`.
The instance registry remains a separate directory unless `Mcp:InstanceDirectory` or `MCP_INSTANCE_DIRECTORY` changes it.

Plugin logs are written as `CheatEngine.Mcp.<CE PID>.log` in the data directory.
`Mcp:Logging:MinimumLevel` defaults to `Information` and accepts `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`, or `None`.
`Trace` can include tool arguments and results, so do not enable it for sensitive targets unless the log location is protected.

### Capability gates

All gates default to `true`.
They limit the MCP surface but are not a sandbox for remaining Cheat Engine capabilities.
Calls refused by a gate report `capability_disabled` before their host operation starts.

| Key | Covers |
|---|---|
| `Mcp:EnableUnsafeLua` | Caller-authored Lua and table content that carries Lua. |
| `Mcp:EnableAutoAssembler` | Caller-authored Auto Assembler checks and patches. |
| `Mcp:EnableTargetCodeExecution` | Target or CE code execution, injection, Mono attachment and invocation, compilation, and speedhack operations. |
| `Mcp:EnableKernelAccess` | DBK, DBVM, physical-memory and kernel access operations. |

The frozen v2 contract identifies the tools that require each gate.
Use the running `tools/list` schema to confirm the installed build's exact tool metadata; see [Tool reference](reference/tools.md).

### File and execution limits

| Key | Default | Rules and effect |
|---|---:|---|
| `Mcp:Files:AllowedRoots` | `[]` | Absolute local directories under which MCP tools can write host files. An empty list refuses every such write. The MCP data and instance directories remain refused. |
| `CheatEngineClient:AllowedTableRoots` | `[]` | Absolute table directories accepted by CheatEngine.Client. An empty list refuses table load and save. |
| `Mcp:Execution:DispatchBudgetMilliseconds` | `100` | Integer from 1 through 10000. A dispatch that exceeds it is recorded; it is not interrupted. |
| `Mcp:Execution:MaxConcurrentDispatches` | `4` | Integer from 1 through 64. Further calls fail as busy before starting. |
| `Mcp:Execution:MaxJobs` | `16` | Integer from 1 through 64. |
| `Mcp:Execution:JobDefaultTtlSeconds` | `120` | Integer from 1 through 300. |
| `Mcp:Execution:JobMaxTtlSeconds` | `300` | Integer from 1 through 300 and no lower than the default TTL. |
| `Mcp:Execution:JobBufferLimit` | `4096` | Integer from 1 through 65536. Oldest buffered job items are evicted when this limit is reached. |

Use narrow, dedicated directories for table files and outputs.
Do not grant a whole drive, a system directory, a profile root, or a shared and synced directory.

## Gateway settings

The gateway must discover the same records as the plugins.
It supports only the following options:

| Command line | Environment variable | Default | Rules |
|---|---|---:|---|
| `--instance-directory <absolute path>` | `MCP_INSTANCE_DIRECTORY` | `%LOCALAPPDATA%\CheatEngine.Mcp\instances` | The command line wins and the path must be absolute. |
| `--call-timeout-seconds <seconds>` | `MCP_GATEWAY_CALL_TIMEOUT_SECONDS` | `45` | The command line wins. The value is a whole number from 5 through 3600. |

For example, an MCP client can start the gateway with a private registry directory:

```json
{
  "command": "C:\\Tools\\CheatEngine.Mcp\\CheatEngine.Mcp.Gateway.exe",
  "args": ["--instance-directory", "C:\\Users\\me\\AppData\\Local\\CheatEngine.Mcp\\instances"]
}
```

Keep the registry private.
It contains per-activation bearer tokens, even though `instance_list` never returns those tokens or backend endpoints.

## Multiple instances

Use a distinct `MCP_INSTANCE_NAME` or `Mcp:InstanceName` for each Cheat Engine process when labels help operators select the correct instance.
Use separate `MCP_DATA_DIRECTORY` values when each process needs different user settings or logs.
The generated `instanceId`, not the label, identifies every routed call and changes when a plugin activation restarts.

See [Multiple instances](multi-instance.md), [Security](security.md), and [Troubleshooting](troubleshooting.md) for the operational consequences.
