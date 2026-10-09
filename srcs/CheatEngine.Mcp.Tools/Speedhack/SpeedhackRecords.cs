using System.ComponentModel;

namespace CheatEngine.Mcp.Tools.Speedhack;

/// <summary>The observed Cheat Engine speedhack state.</summary>
/// <param name="Speed">Cheat Engine's configured multiplier; it is not a measurement of game time.</param>
/// <param name="HooksInstalled">Whether the speedhack_wantedspeed symbol exists, not whether a hook works.</param>
public sealed record SpeedhackState(
	[property:
		Description(
			"Cheat Engine's configured speed multiplier; it is not a measurement of the target's actual game time.")]
	double Speed,
	[property:
		Description(
			"Whether the speedhack_wantedspeed symbol exists in the target. Cheat Engine creates it during the " +
			"first activation once its helper library is injected, also when a time-function hook then fails and " +
			"on the Unity timeScale path, and once it exists never retries hooking in that process, so true does " +
			"not prove that the target's clocks are hooked.")]
	bool HooksInstalled);

/// <summary>The observed state after a requested speed change.</summary>
/// <param name="Speed">Cheat Engine's configured multiplier after the request.</param>
/// <param name="HooksInstalled">Whether Cheat Engine reported that the speedhack_wantedspeed symbol exists after the request.</param>
/// <param name="FirstActivation">
///     Whether this request activated the speedhack in a process without the speedhack_wantedspeed symbol. Speed 1
///     never activates it and always reports false. Cheat Engine attempts its hooks again while the symbol is absent,
///     and never once it exists.
/// </param>
/// <param name="ResourceId">The MCP-owned restore resource while the requested speed differs from one.</param>
public sealed record SpeedhackSetResult(
	[property: Description("Cheat Engine's configured speed multiplier after the request.")]
	double Speed,
	[property:
		Description(
			"Whether Cheat Engine reported that the speedhack_wantedspeed symbol exists in the target; omitted when it " +
			"could not be observed after the change. The symbol also exists after a failed hook and on the Unity timeScale " +
			"path, so it does not prove that the target's clocks are hooked.")]
	bool? HooksInstalled,
	[property:
		Description(
			"Whether no speedhack_wantedspeed symbol existed before this request, so Cheat Engine attempted its " +
			"hooks, which may have failed or shown a dialog. Speed 1 never activates the speedhack and always " +
			"reports false. If the attempt failed after creating the symbol, Cheat Engine never retries in this " +
			"process; if hooksInstalled is still false, a later call retries.")]
	bool FirstActivation,
	[property:
		Description(
			"The MCP-owned resource that restores speed to 1 through runtime_release_resources; omitted at speed 1.")]
	string? ResourceId = null);

/// <summary>The bounded result copied from a fixed speedhack Lua body.</summary>
internal sealed record LuaSpeedhackState(double Speed, bool? HooksInstalled, bool FirstActivation = false);

/// <summary>The bounded cleanup context when MCP could not restore a prior speedhack resource.</summary>
internal sealed record SpeedhackRestoreFailure(string ResourceId, ResourceReleaseOutcome Release);
