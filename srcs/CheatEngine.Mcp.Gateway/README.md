# CheatEngine.Mcp.Gateway

The self-contained Windows x64 Native AOT stdio gateway executable. `GatewayProgram` only composes:
`AddCheatEngineMcpGateway(args, version)`
followed by the same `AddTools().AddResources().AddPrompts()` as the plugin, in catalog mode, so no tool is ever
constructed here.

Arguments, each overriding its environment variable, which overrides the default:

- `--instance-directory <absolute path>` or `MCP_INSTANCE_DIRECTORY`: the registry the plugins publish to, by default
  `%LOCALAPPDATA%\CheatEngine.Mcp\instances`.
- `--call-timeout-seconds <5..3600>` or `MCP_GATEWAY_CALL_TIMEOUT_SECONDS`: how long one routed call may take, 45 by
  default. An expired call is reported as `timeout` with `hostEffect: unknown` and is never sent again.

Any other argument, a relative directory or an out-of-range timeout stops the gateway at startup.

**Depends on:** Core, Hosting, Tools, Resources, Prompts. Never the Plugin; it never creates a Client activation.
