using System.ComponentModel;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools.Speedhack;

/// <summary>The <c>speedhack_*</c> tools, with one tracked restore action for every non-default speed.</summary>
[McpServerToolType]
public sealed class SpeedhackTools
{
	internal const double MinimumSpeed = 0.01;
	internal const double MaximumSpeed = 1000;

	private readonly ToolDispatch _dispatch;
	private readonly Lock _resourceLock = new();
	private readonly TargetResources _resources;
	private ITargetResource? _restoreResource;

	/// <summary>Creates the tools; no Cheat Engine work runs during construction.</summary>
	public SpeedhackTools(ToolDispatch dispatch, TargetResources resources)
	{
		ArgumentNullException.ThrowIfNull(dispatch);
		ArgumentNullException.ThrowIfNull(resources);
		_dispatch = dispatch;
		_resources = resources;
	}

	/// <summary>Reads the last configured speed and whether the speedhack_wantedspeed symbol exists.</summary>
	[McpServerTool(Name = CheatEngineToolNames.SpeedhackGetState, Title = "Get speedhack state", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Read Cheat Engine's last configured speedhack multiplier and whether its speedhack_wantedspeed symbol " +
		"exists in the target. The speed is configuration, not proof that the target's game clock changed. " +
		"hooksInstalled reports only that symbol: Cheat Engine creates it during the first activation, also when a " +
		"time-function hook then fails and when it takes the Unity timeScale path, and once that symbol exists it " +
		"never retries hooking in the same process, so only a restart of the target retries. While it is absent, " +
		"the next speedhack_set_speed with a speed other than 1 attempts the hooks again.")]
	public SpeedhackState GetState(CancellationToken cancellationToken = default)
	{
		LuaSpeedhackState state = _dispatch.RunLua(CheatEngineToolNames.SpeedhackGetState, SpeedhackScripts.GetState,
			SpeedhackLuaJsonContext.Default.LuaSpeedhackState, cancellationToken);
		if (state.HooksInstalled is not { } hooksInstalled)
		{
			throw new CheatEngineToolException(new ToolError(ToolErrorKind.HostRefused,
				"Cheat Engine did not return the speedhack symbol state.", CheatEngineToolNames.SpeedhackGetState,
				ToolHostEffect.NotStarted, false));
		}

		return new SpeedhackState(state.Speed, hooksInstalled);
	}

	/// <summary>Installs or updates Cheat Engine speedhack, retaining an MCP restore action until speed returns to one.</summary>
	[McpServerTool(Name = CheatEngineToolNames.SpeedhackSetSpeed, Title = "Set speedhack speed", ReadOnly = false,
		Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.MayPrompt)]
	[RequiresFeature(McpFeature.TargetCodeExecution)]
	[Description(
		"Set Cheat Engine's speedhack multiplier from 0.01 through 1000. The first speed other than 1 in a process " +
		"activates the speedhack, which can inject code into the target or show a Cheat Engine dialog. Once the " +
		"speedhack_wantedspeed symbol exists, Cheat Engine never hooks that process again, even after a failed hook, " +
		"so hooksInstalled does not prove working hooks. While speed differs from 1, MCP tracks a resource that " +
		"runtime_release_resources restores to 1 before a target change. Speed 1 never activates the speedhack or " +
		"removes hooks: it restores 1 only where that symbol exists, does nothing while Cheat Engine's and the " +
		"target's speed are 1, and refuses (invalid_state) when Cheat Engine reports another speed for a process " +
		"without the symbol. The call is refused (invalid_state) without an attached process, and a change also " +
		"while the target is paused or stopped in the debugger. While hooksInstalled is false after a failed " +
		"activation, a retry attempts the hooks again.")]
	public SpeedhackSetResult SetSpeed(
		[Description(
			"The finite multiplier from 0.01 through 1000; 1 restores normal speed without ever activating the " +
			"speedhack, while 0 is not pause.")]
		double speed,
		CancellationToken cancellationToken = default)
	{
		_dispatch.Features.Require(McpFeature.TargetCodeExecution, CheatEngineToolNames.SpeedhackSetSpeed);
		if (!double.IsFinite(speed) || speed < MinimumSpeed || speed > MaximumSpeed)
		{
			throw CheatEngineToolException.InvalidArgument("speed",
				$"must be finite and between {MinimumSpeed} and {MaximumSpeed}.");
		}

		return _dispatch.Run(CheatEngineToolNames.SpeedhackSetSpeed, token => SetSpeedCore(speed, token),
			cancellationToken);
	}

	private SpeedhackSetResult SetSpeedCore(double speed, CancellationToken cancellationToken)
	{
		ITargetResource? previous;
		bool priorRestoreApplied = false;
		lock (_resourceLock)
		{
			previous = _restoreResource;
		}

		if (previous is not null)
		{
			_ = _dispatch.ExecuteLua(CheatEngineToolNames.SpeedhackSetSpeed, SpeedhackScripts.GetState,
				SpeedhackLuaJsonContext.Default.LuaSpeedhackState, cancellationToken);
			ResourceReleaseOutcome released = previous.Release(cancellationToken);
			if (!released.IsComplete)
			{
				throw CheatEngineToolException.PartialEffect(
					"Cheat Engine could not restore the prior speedhack state before applying a new speed.",
					released.HostEffect,
					new SpeedhackRestoreFailure(previous.Descriptor.Id, released),
					SpeedhackLuaJsonContext.Default.SpeedhackRestoreFailure, released.IsRetryable,
					released.IsRetryable
						? "Repeat speedhack_set_speed after Cheat Engine accepts the restore."
						: "Set speedhack to 1 in Cheat Engine and acknowledge the resource after manual recovery.");
			}

			priorRestoreApplied = released.HostEffect is not (ToolHostEffect.NotStarted or ToolHostEffect.NotApplied);
			_resources.Forget(previous);
			lock (_resourceLock)
			{
				if (ReferenceEquals(_restoreResource, previous))
				{
					_restoreResource = null;
				}
			}
		}

		try
		{
			if (speed == 1)
			{
				LuaSpeedhackState normal = _dispatch.ExecuteLua(CheatEngineToolNames.SpeedhackSetSpeed,
					SpeedhackScripts.SetNormal, SpeedhackLuaJsonContext.Default.LuaSpeedhackState, cancellationToken);
				return new SpeedhackSetResult(normal.Speed, normal.HooksInstalled, normal.FirstActivation);
			}

			string id = _resources.NextId("speedhack");
			LuaSpeedhackState changed = _dispatch.ExecuteLua(CheatEngineToolNames.SpeedhackSetSpeed,
				SpeedhackScripts.SetSpeed, SpeedhackLuaJsonContext.Default.LuaSpeedhackState, cancellationToken, speed, id,
				_resources.Namespace);
			ITargetResource tracked = _resources.TrackState(id, "speedhack", name: "speedhack",
				detail: "Restores the configured speed to 1.");
			lock (_resourceLock)
			{
				_restoreResource = tracked;
			}

			return new SpeedhackSetResult(changed.Speed, changed.HooksInstalled, changed.FirstActivation,
				tracked.Descriptor.Id);
		}
		catch (CheatEngineToolException exception) when (priorRestoreApplied &&
			exception.Error.HostEffect is ToolHostEffect.NotStarted or ToolHostEffect.NotApplied)
		{
			throw new CheatEngineToolException(exception.Error with
			{
				HostEffect = ToolHostEffect.Started,
				Retryable = false
			}, exception);
		}
	}
}
