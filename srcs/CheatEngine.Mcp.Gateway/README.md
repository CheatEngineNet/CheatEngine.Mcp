# CheatEngine.Mcp.Gateway

The self-contained stdio gateway executable. `GatewayProgram` only composes: `AddCheatEngineMcpGateway(args, version)`
followed by the same `AddTools().AddResources().AddPrompts()` as the plugin, in catalog mode, so no tool is ever
constructed here.

**Depends on:** Core, Hosting, Tools, Resources, Prompts. Never the Plugin; it never creates a Client activation.
