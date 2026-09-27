# Connect an AI client

CheatEngine.Mcp talks to AI clients through one local **stdio** MCP server: `CheatEngine.Mcp.Gateway.exe`.
The client starts the executable and exchanges MCP messages with it over standard input and output.
There is no URL, port, API key or login to configure.

- Register the gateway **once**. One entry serves every Cheat Engine (CE) instance on the machine; each tool call names its instance with `instanceId` ([multi-instance.md](multi-instance.md)).
- Run the client and CE on the same Windows machine, under the same Windows user.
- Use the **absolute path** of the gateway executable. The examples use `C:\Tools\CheatEngine.Mcp\CheatEngine.Mcp.Gateway.exe`; replace it with yours.
- In JSON files, escape each backslash (`C:\\Tools\\...`). In TOML, use a single-quoted literal string (`'C:\Tools\...'`).

## Use the server key `cheatengine`

Every example registers the server under the short key `cheatengine`.
Keep it short: Claude clients prefix each tool with the key, and Claude Code exposes a tool as `mcp__<key>__<tool>` ([Claude Code MCP docs](https://code.claude.com/docs/en/mcp)).
Full names longer than 64 characters have been rejected by Claude Desktop and by Claude Code tool references ([anthropics/claude-code#34960](https://github.com/anthropics/claude-code/issues/34960), [#21136](https://github.com/anthropics/claude-code/issues/21136)).
CheatEngine.Mcp tool names are at most 40 characters, so `mcp__cheatengine__<tool>` stays at 58 characters or fewer.

## Claude Code

Register the gateway for all your projects (user scope, stored in `~/.claude.json`):

```powershell title="claude-code"
claude mcp add --transport stdio --scope user cheatengine -- "C:\Tools\CheatEngine.Mcp\CheatEngine.Mcp.Gateway.exe"
```

Everything after `--` is the server command and its arguments.
Without `--scope`, the server is added to the current project only (local scope).

To share the entry with a project, use project scope, which writes `.mcp.json` at the project root:

```json
{
  "mcpServers": {
    "cheatengine": {
      "type": "stdio",
      "command": "C:\\Tools\\CheatEngine.Mcp\\CheatEngine.Mcp.Gateway.exe",
      "args": []
    }
  }
}
```

Claude Code asks for approval before it uses a project's `.mcp.json` servers.
A project file contains your local path, so prefer user scope unless everyone uses the same layout.

Check the connection with `claude mcp list` (it shows a health status per server), `claude mcp get cheatengine`, or `/mcp` inside a session.

Notes:

- **Tool search** is on by default: Claude Code loads tool names up front and fetches full definitions on demand, so the large catalog costs little context. `ENABLE_TOOL_SEARCH=false` loads every tool up front.
- **Prompts** appear as commands: type `/` and look for `/cheatengine:attach_and_orient (MCP)`, or type `/mcp__cheatengine__attach_and_orient`.
- **Resources** are available through `@` mentions, for example the `cheatengine://docs/...` guides.
- **Output size**: Claude Code warns above 10,000 tokens of tool output and truncates at 25,000 tokens by default (`MAX_MCP_OUTPUT_TOKENS`). Ask for smaller pages (`limit`) rather than raising the limit.

## Claude Desktop

1. Open **Settings > Developer** and choose **Edit Config**. This opens `%APPDATA%\Claude\claude_desktop_config.json`.
2. Merge the `cheatengine` entry into `mcpServers`:

   ```json title="claude-desktop"
   {
     "mcpServers": {
       "cheatengine": {
         "command": "C:\\Tools\\CheatEngine.Mcp\\CheatEngine.Mcp.Gateway.exe",
         "args": []
       }
     }
   }
   ```

3. Quit Claude Desktop completely and start it again; it reads the file only at startup.

The gateway's stderr goes to `%APPDATA%\Claude\logs\mcp-server-cheatengine.log`, and connection errors to `mcp.log` in the same folder ([MCP docs: connect local servers](https://modelcontextprotocol.io/docs/develop/connect-local-servers)).

Known issue: in the Microsoft Store (MSIX) build, **Edit Config** can open a different file from the one the app reads, which is `%LOCALAPPDATA%\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming\Claude\claude_desktop_config.json` ([anthropics/claude-code#26073](https://github.com/anthropics/claude-code/issues/26073), open at the time of writing).
If the server never appears, put the entry in that file too.

## VS Code (GitHub Copilot)

Run **MCP: Open User Configuration** from the Command Palette to open your user `mcp.json`, then add the server.
VS Code uses a top-level `servers` object and needs `"type": "stdio"`:

```json title="vscode"
{
  "servers": {
    "cheatengine": {
      "type": "stdio",
      "command": "C:\\Tools\\CheatEngine.Mcp\\CheatEngine.Mcp.Gateway.exe"
    }
  }
}
```

The same entry also works in a workspace `.vscode/mcp.json`, or through **MCP: Add Server**.
Confirm that you trust the server when VS Code asks.
Stdio servers also accept `args`, `env`, `envFile` and `cwd` ([VS Code MCP configuration reference](https://code.visualstudio.com/docs/copilot/reference/mcp-configuration)).

Notes:

- **128 tools per request.** VS Code allows at most 128 enabled tools in one chat request, and the CheatEngine.Mcp catalog alone has about 170 ([VS Code: use tools with agents](https://code.visualstudio.com/docs/copilot/agents/agent-tools)). Either deselect tools or whole groups you do not need in the tools picker (**Configure Tools** in the chat input), or enable virtual tools with the `github.copilot.chat.virtualTools.threshold` setting. Tools from other servers and extensions count too. If you see "Cannot have more than 128 tools per request", this is the cause.
- **Prompts**: type `/cheatengine.<prompt>` in the chat input, for example `/cheatengine.attach_and_orient`.
- **Resources**: choose **Add Context > MCP Resources**, or run **MCP: Browse Resources**.
- **Logs**: run **MCP: List Servers**, select `cheatengine` and choose **Show Output**.

## Codex

Register the gateway with the CLI:

```powershell title="codex"
codex mcp add cheatengine -- "C:\Tools\CheatEngine.Mcp\CheatEngine.Mcp.Gateway.exe"
codex mcp list
```

Or add the table to `~/.codex/config.toml` (`%USERPROFILE%\.codex\config.toml`); update an existing `cheatengine` table instead of adding a second one:

```toml
[mcp_servers.cheatengine]
command = 'C:\Tools\CheatEngine.Mcp\CheatEngine.Mcp.Gateway.exe'
```

The Codex CLI, IDE extension and ChatGPT desktop app share this configuration ([Codex MCP docs](https://learn.chatgpt.com/docs/extend/mcp?surface=cli)).
A project-level `.codex/config.toml` applies only to trusted projects.
Restart the Codex session after a change; in the terminal UI, `/mcp` lists the active servers.

Notes:

- `tool_timeout_sec` defaults to 60 seconds, longer than the gateway's own 45-second call timeout, so keep it at 60 or more.
- `startup_timeout_sec` defaults to 10 seconds.
- `enabled_tools` and `disabled_tools` limit which tools Codex offers; use them if the full catalog is more than you need.

## Cursor

Add the server to `~/.cursor/mcp.json` (`%USERPROFILE%\.cursor\mcp.json`) for all projects, or to `.cursor/mcp.json` in one project:

```json title="cursor"
{
  "mcpServers": {
    "cheatengine": {
      "type": "stdio",
      "command": "C:\\Tools\\CheatEngine.Mcp\\CheatEngine.Mcp.Gateway.exe"
    }
  }
}
```

Cursor supports MCP tools, prompts and resources; stdio entries also accept `args`, `env` and `envFile` ([Cursor MCP docs](https://cursor.com/docs/context/mcp)).
To see the gateway's logs, open the Output panel and select **MCP Logs**.
Cursor's documentation states no tool-count limit; if the agent struggles with the catalog, turn off servers you do not need.

## Other clients

Any client that can launch a local stdio MCP server works.
Most accept an `mcpServers` entry with a `command`, as in the Claude Desktop example.
A browser-only or remote client needs a Windows-local component that can start the executable.

## Clients and the large catalog

The gateway advertises one full catalog of about 170 tools, 22 prompts and the `cheatengine://` resources; there are no reduced profiles.

| Client | Behavior with many tools | What to do |
| --- | --- | --- |
| Claude Code | Tool search defers tool definitions by default. | Nothing. |
| VS Code | At most 128 enabled tools per chat request. | Deselect tools in the tools picker, or enable virtual tools. |
| Codex | No tool limit in its MCP documentation. | Optionally trim with `enabled_tools` / `disabled_tools`. |
| Cursor | No tool limit in its MCP documentation. | Turn off servers you do not need. |
| Claude Desktop | No tool limit in the MCP setup documentation. | Keep the server key short (see above). |

Some clients show only tools and not prompts or resources.
Everything a prompt or resource offers is also reachable through tools, and the guides also ship in the skill's `references/` folder.

## Gateway options

The gateway needs no arguments.
If you move the discovery registry, the gateway and every plugin must use the same absolute directory ([multi-instance.md](multi-instance.md#the-discovery-registry)).
The gateway reads it from, in order of precedence:

1. The argument `--instance-directory <absolute path>`.
2. The environment variable `MCP_INSTANCE_DIRECTORY`.
3. The default `%LOCALAPPDATA%\CheatEngine.Mcp\instances`.

A relative path, or any unknown argument, stops the gateway at startup.
[configuration.md](configuration.md) documents every gateway option.

Pass the argument after the executable, for example in Claude Code:

```powershell
claude mcp add --transport stdio --scope user cheatengine -- "C:\Tools\CheatEngine.Mcp\CheatEngine.Mcp.Gateway.exe" --instance-directory "D:\CheatEngine\instances"
```

Or set the variable in the entry's `env`, for example in Claude Desktop, VS Code or Cursor:

```json
{
  "command": "C:\\Tools\\CheatEngine.Mcp\\CheatEngine.Mcp.Gateway.exe",
  "env": { "MCP_INSTANCE_DIRECTORY": "D:\\CheatEngine\\instances" }
}
```

In Codex:

```toml
[mcp_servers.cheatengine]
command = 'C:\Tools\CheatEngine.Mcp\CheatEngine.Mcp.Gateway.exe'
args = ['--instance-directory', 'D:\CheatEngine\instances']
```

## Install the skill

The optional `cheatengine-mcp` skill teaches the agent the workflows, instance routing and cleanup duties.
Copy the whole `skills/cheatengine-mcp/` folder from the release, keeping `SKILL.md`, `references/` and `agents/` together.

| Client | User-wide | Per project | Source |
| --- | --- | --- | --- |
| Claude Code | `~/.claude/skills/cheatengine-mcp/` | `.claude/skills/cheatengine-mcp/` | [Claude Code skills](https://code.claude.com/docs/en/skills) |
| Codex | `~/.agents/skills/cheatengine-mcp/` | `.agents/skills/cheatengine-mcp/` | [Codex skills](https://learn.chatgpt.com/docs/build-skills) |

On Windows, `~` is `%USERPROFILE%`.
Installing the skill does not register the gateway; do both.

## Verify the connection

1. Use the client's list command (`claude mcp list`, `codex mcp list`, **MCP: List Servers**) to confirm that the server starts. This proves registration, not that CE is reachable.
2. Ask the agent to call `instance_list`. With CE open and the plugin enabled, it returns that instance.
3. If the list is empty, ask the agent to read the resource `cheatengine://docs/connection-troubleshooting`. The gateway serves it without any CE instance, so success proves the client reaches the gateway and the problem is on the CE side. See [troubleshooting.md](troubleshooting.md).

## Update the gateway

Stop the MCP server in your client (or close the client) before you replace `CheatEngine.Mcp.Gateway.exe`; a running gateway locks the file.
Replace it together with the plugin folder, from the same build, then restart the client.

## Sources

Checked on 2026-09-27:

- Claude Code: https://code.claude.com/docs/en/mcp, https://code.claude.com/docs/en/skills
- Claude Desktop: https://modelcontextprotocol.io/docs/develop/connect-local-servers, https://github.com/anthropics/claude-code/issues/26073
- Tool-name length: https://github.com/anthropics/claude-code/issues/34960, https://github.com/anthropics/claude-code/issues/21136
- VS Code: https://code.visualstudio.com/docs/copilot/customization/mcp-servers, https://code.visualstudio.com/docs/copilot/reference/mcp-configuration, https://code.visualstudio.com/docs/copilot/agents/agent-tools
- Codex: https://learn.chatgpt.com/docs/extend/mcp?surface=cli, https://learn.chatgpt.com/docs/build-skills
- Cursor: https://cursor.com/docs/context/mcp
