# Security policy

CheatEngine.Mcp gives an AI client Cheat Engine's power over local processes, so its security boundaries matter.
This page says which versions receive fixes, how to report a vulnerability privately, and what counts as one.
The trust boundary is the Windows user account: each backend listens only on `127.0.0.1` and refuses every request without its per-activation bearer token, which isolates it from unauthenticated HTTP clients but not from programs running as the same user.
What each capability gate and file policy covers is in the [safety document](srcs/CheatEngine.Mcp.Resources/Knowledge/Documents/safety.md), which agents read as `cheatengine://docs/safety`.

## Supported versions

| Version | Supported |
|---|---|
| 2.0.0, in development on the latest `main` | Yes |
| Anything older than 2.0.0 | No; update to the latest `main` |

Fixes land on `main`; there are no maintained release branches.

## Reporting a vulnerability

Do not report a vulnerability in a public issue, pull request, discussion or chat.

1. Use GitHub private vulnerability reporting: open the repository's **Security** tab and choose **Report a vulnerability**, or go to `https://github.com/CheatEngineNet/CheatEngine.Mcp/security/advisories/new`.
2. The maintainer must enable private vulnerability reporting for that button to appear. Until it is enabled, open a public issue titled `Request for a private security contact`, with no details about the problem, not even the affected component, and a maintainer will reply with a private channel.

Include in the private report:

- The version or commit, the Cheat Engine version and build, and the MCP client.
- The component: the plugin, the gateway, a tool, the loopback backend, the discovery registry or the build.
- The relevant settings, especially the four capability gates and `Mcp:Files:AllowedRoots`.
- The steps to reproduce, what you expected and what happened, and the impact you see.

Redact every bearer token, discovery record, user name and personal path from logs and screenshots before you send them.
Test only on machines and targets you own, and stop as soon as you have shown the problem.

The maintainers will acknowledge the report, keep you informed while they investigate, and agree on the timing of any public disclosure with you.
Please allow time for a fix before you disclose the details.

## Scope

In scope:

- The plugin (`CheatEngine.Mcp.Plugin.dll` and its folder), the stdio gateway (`CheatEngine.Mcp.Gateway.exe`) and the code in this repository.
- The per-activation loopback backend: a listener reachable beyond `127.0.0.1`, a request accepted without the exact bearer token, or ambient settings that change the endpoint or the environment.
- Bearer tokens: a token that reaches a tool result, an error, a completion, a log, a crash report or any file other than its own discovery record.
- The discovery registry: records written with weaker permissions than their folder, or a gateway that routes to a backend whose identity it did not verify.
- The capability gates `Mcp:EnableUnsafeLua`, `Mcp:EnableAutoAssembler`, `Mcp:EnableTargetCodeExecution` and `Mcp:EnableKernelAccess`: a way to reach a gated capability through MCP while its gate is `false`.
- The file policies: a write outside `Mcp:Files:AllowedRoots`, a table outside `CheatEngineClient:AllowedTableRoots`, or a path trick that escapes either.
- Caller input that becomes Lua source or Auto Assembler code outside the tools whose documented purpose is to run it.
- The build and distribution: a shipped file that the packaging checks should have caught, such as a token or a personal path.

The gates are exposure switches, not a sandbox.
With a gate on, the tools it covers can do anything the Cheat Engine process can do, and that is the intended behavior, not a vulnerability.
The [tool map](srcs/CheatEngine.Mcp.Resources/Knowledge/Documents/tool-map.md) names the gate that each tool needs, and the [safety document](srcs/CheatEngine.Mcp.Resources/Knowledge/Documents/safety.md#gates-are-switches-not-a-sandbox) lists what each gate and file policy covers.

Out of scope:

- Cheat Engine itself, its kernel driver, DBVM and its Lua API: report those to the Cheat Engine project. Issues in CheatEngine.Client or CheatEngine.SDK belong to those projects.
- Anything that requires control of the Windows account or administrator rights already, including other programs running as the same user: they can read the discovery records and call a backend, and authentication is not a boundary against them.
- Harm caused by a cheat table, Auto Assembler script or Lua script that the user chose to run.
- Requests to bypass anti-cheat, DRM or license checks, or to hide Cheat Engine from them.
- Misuse of the software against other people's software, games, accounts or data.

## Responsible use

Use CheatEngine.Mcp only on software you own or are authorized to modify: your own programs, single-player or offline games, the Cheat Engine tutorials and disposable test targets.
Respect each program's license and terms of service.
Do not use it to cheat in online or multiplayer games, to bypass anti-cheat, DRM or license checks, or to read other people's data.
The project does not support such use, and the knowledge it serves to agents (`cheatengine://docs/safety`) tells them to refuse it.
