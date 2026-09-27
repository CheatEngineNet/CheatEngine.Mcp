using System.ComponentModel;

namespace CheatEngine.Mcp.Tools.Speedhack;

/// <summary>The observed Cheat Engine speedhack state.</summary>
/// <param name="Speed">Cheat Engine's configured multiplier; it is not a measurement of game time.</param>
/// <param name="HooksInstalled">Whether Cheat Engine reports the speedhack's target symbol.</param>
public sealed record SpeedhackState(
	[property:
		Description(
			"Cheat Engine's configured speed multiplier; it is not a measurement of the target's actual game time.")]
	double Speed,
	[property:
		Description(
			"Whether Cheat Engine reports the speedhack target symbol, which it creates when the speedhack installs its hooks.")]
	bool HooksInstalled);

/// <summary>The observed state after a requested speed change.</summary>
/// <param name="Speed">Cheat Engine's configured multiplier after the request.</param>
/// <param name="HooksInstalled">Whether Cheat Engine reports the speedhack's target symbol.</param>
/// <param name="FirstActivation">Whether no speedhack target symbol existed before this request.</param>
/// <param name="ResourceId">The MCP-owned restore resource while the requested speed differs from one.</param>
public sealed record SpeedhackSetResult(
	[property: Description("Cheat Engine's configured speed multiplier after the request.")]
	double Speed,
	[property: Description("Whether Cheat Engine reports the speedhack target symbol.")]
	bool HooksInstalled,
	[property:
		Description(
			"Whether no speedhack target symbol existed before this request, so Cheat Engine may have installed hooks or shown a failure dialog.")]
	bool FirstActivation,
	[property:
		Description(
			"The MCP-owned resource that restores speed to 1 through runtime_release_resources; omitted at speed 1.")]
	string? ResourceId = null);

/// <summary>The bounded result copied from a fixed speedhack Lua body.</summary>
internal sealed record LuaSpeedhackState(double Speed, bool HooksInstalled, bool FirstActivation = false);
