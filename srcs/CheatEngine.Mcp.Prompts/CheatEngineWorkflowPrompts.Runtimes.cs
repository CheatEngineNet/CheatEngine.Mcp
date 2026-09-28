using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

using CheatEngine.Mcp.Prompts.Workflows;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Prompts;

public sealed partial class CheatEngineWorkflowPrompts
{
	/// <summary>Explore a Unity (Mono) game.</summary>
	[McpServerPrompt(Name = "unity_mono_recon", Title = "Explore a Unity (Mono) game")]
	[Description("Unity Mono classes, fields, statics and methods through CE's Mono data collector, after explicit "
				 + "consent." + NeedsTargetCodeExecution)]
	public static GetPromptResult UnityMonoReconPrompt(
		[Display(Name = "Class name")]
		[Description("The class to find, such as PlayerController.")]
		string? className = null,
		[Display(Name = "Field name")]
		[Description("The field to find, such as health.")]
		string? fieldName = null,
		McpServer? server = null)
	{
		return WorkflowPrompt.Render(server, RuntimeWorkflows.UnityMonoRecon, className, fieldName);
	}

	/// <summary>Explore a Unity (IL2CPP) game.</summary>
	[McpServerPrompt(Name = "unity_il2cpp_recon", Title = "Explore a Unity (IL2CPP) game")]
	[Description("Unity IL2CPP classes and fields: offline from offsets the user supplies, or through CE's data "
				 + "collector after explicit consent." + NeedsTargetCodeExecution)]
	public static GetPromptResult UnityIl2CppReconPrompt(
		[Display(Name = "Mode")]
		[Description("offline (default) reads memory only; collector injects CE's data collector into the game.")]
		[AllowedValues("offline", "collector")]
		string? mode = null,
		[Display(Name = "Class name")]
		[Description("The class to find, such as PlayerController.")]
		string? className = null,
		[Display(Name = "Field name")]
		[Description("The field to find, such as health.")]
		string? fieldName = null,
		McpServer? server = null)
	{
		return WorkflowPrompt.Render(server, RuntimeWorkflows.UnityIl2CppRecon, mode, className, fieldName);
	}

	/// <summary>Explore a .NET game.</summary>
	[McpServerPrompt(Name = "dotnet_recon", Title = "Explore a .NET game")]
	[Description(".NET types, fields, statics and instances through CE's out-of-process .NET collector.")]
	public static GetPromptResult DotNetReconPrompt(
		[Display(Name = "Type name")]
		[Description("The type to find, such as Game.Player.")]
		string? typeName = null,
		McpServer? server = null)
	{
		return WorkflowPrompt.Render(server, RuntimeWorkflows.DotNetRecon, typeName);
	}

	/// <summary>Explore an Unreal Engine game.</summary>
	[McpServerPrompt(Name = "unreal_recon", Title = "Explore a UE4 or UE5 game")]
	[Description("Unreal Engine 4 or 5 objects and names: the version string, the object header and the name pool, "
				 + "then an anchor for a value found by scanning.")]
	public static GetPromptResult UnrealReconPrompt(
		[Display(Name = "UE version")]
		[Description("auto (default) reads the version string; ue4 or ue5 skips that step.")]
		[AllowedValues("auto", "ue4", "ue5")]
		string? ueVersion = null,
		[Display(Name = "Goal")]
		[Description("What the user wants to find, such as the player's health.")]
		string? goal = null,
		McpServer? server = null)
	{
		return WorkflowPrompt.Render(server, RuntimeWorkflows.UnrealRecon, ueVersion, goal);
	}

	/// <summary>Find values in an emulated console's memory.</summary>
	[McpServerPrompt(Name = "emulator_memory", Title = "Find values in an emulated console's memory")]
	[Description("Values inside an emulated console: find the guest RAM base, convert guest addresses and handle "
				 + "big-endian values.")]
	public static GetPromptResult EmulatorMemoryPrompt(
		[Display(Name = "Emulator")]
		[Description("The emulator: dolphin, pcsx2, ppsspp, rpcs3, cemu or other.")]
		[AllowedValues("dolphin", "pcsx2", "ppsspp", "rpcs3", "cemu", "other")]
		string emulator,
		[Display(Name = "Guest address")]
		[Description("A guest address from the user's own notes, such as 80001234.")]
		string? guestAddress = null,
		McpServer? server = null)
	{
		return WorkflowPrompt.Render(server, RuntimeWorkflows.EmulatorMemory, emulator, guestAddress);
	}

	/// <summary>Change game speed.</summary>
	[McpServerPrompt(Name = "speedhack", Title = "Change game speed")]
	[Description("Change the game speed with CE's speedhack, verify it and restore normal speed."
				 + NeedsTargetCodeExecution)]
	public static GetPromptResult SpeedhackPrompt(
		[Display(Name = "Speed multiplier")]
		[Description("The speed multiplier between 0.01 and 1000, such as 0.5 or 2.")]
		string speed,
		McpServer? server = null)
	{
		return WorkflowPrompt.Render(server, RuntimeWorkflows.Speedhack, speed);
	}

	/// <summary>Write and run a lua_execute script safely.</summary>
	[McpServerPrompt(Name = "write_lua_script", Title = "Write and run a lua_execute script safely")]
	[Description("Only when no tool covers a Cheat Engine feature: draft, show and run a bounded Lua script, then "
				 + "report what it left behind." + NeedsUnsafeLua)]
	public static GetPromptResult WriteLuaScriptPrompt(
		[Display(Name = "Purpose")]
		[Description("What the script must do, such as list the loaded plugins.")]
		string purpose,
		[Display(Name = "Effect")]
		[Description("read_only (default) or mutating: whether the script changes the game or Cheat Engine.")]
		[AllowedValues("read_only", "mutating")]
		string? effect = null,
		McpServer? server = null)
	{
		return WorkflowPrompt.Render(server, RuntimeWorkflows.WriteLuaScript, purpose, effect);
	}
}
