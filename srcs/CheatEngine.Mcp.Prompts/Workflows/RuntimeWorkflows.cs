using CheatEngine.Mcp.Core.Features;

using static CheatEngine.Mcp.Prompts.Workflows.WorkflowValues;

namespace CheatEngine.Mcp.Prompts.Workflows;

/// <summary>The workflows for managed runtimes, engines and emulators, the speedhack and caller-authored Lua.</summary>
internal static class RuntimeWorkflows
{
	internal static readonly WorkflowDefinition UnityMonoRecon =
		new("unity_mono_recon", "unity-mono-recon", ["mono-and-dotnet", "safety"])
		{
			Arguments = [new WorkflowArgument("className"), new WorkflowArgument("fieldName")],
			Gates = [McpFeature.TargetCodeExecution]
		};

	internal static readonly WorkflowDefinition UnityIl2CppRecon =
		new("unity_il2cpp_recon", "unity-il2cpp-recon", ["unity-il2cpp", "mono-and-dotnet", "safety"])
		{
			Arguments =
			[
				new WorkflowArgument("mode") { Default = "offline", Allowed = Il2CppModes },
				new WorkflowArgument("className"),
				new WorkflowArgument("fieldName")
			],
			Gates = [McpFeature.TargetCodeExecution],
			Selectors = ["mode"]
		};

	internal static readonly WorkflowDefinition DotNetRecon = new("dotnet_recon", "dotnet-recon", ["mono-and-dotnet"])
	{
		Arguments = [new WorkflowArgument("typeName")]
	};

	internal static readonly WorkflowDefinition UnrealRecon =
		new("unreal_recon", "unreal-recon", ["unreal-engine", "pointers", "structures"])
		{
			Arguments =
			[
				new WorkflowArgument("ueVersion") { Default = "auto", Allowed = UnrealVersions },
				new WorkflowArgument("goal", WorkflowInputKind.Prose)
			]
		};

	internal static readonly WorkflowDefinition EmulatorMemory =
		new("emulator_memory", "emulator-memory", ["emulators", "value-types"])
		{
			Arguments =
			[
				new WorkflowArgument("emulator", Required: true) { Allowed = Emulators },
				new WorkflowArgument("guestAddress")
			]
		};

	internal static readonly WorkflowDefinition Speedhack = new("speedhack", "speedhack", ["speedhack", "safety"])
	{
		Arguments = [new WorkflowArgument("speed", WorkflowInputKind.Speed, true)],
		Gates = [McpFeature.TargetCodeExecution]
	};

	internal static readonly WorkflowDefinition WriteLuaScript =
		new("write_lua_script", "write-lua-script", ["lua", "lua-api", "safety", "tool-map"])
		{
			Arguments =
			[
				new WorkflowArgument("purpose", WorkflowInputKind.Prose, true),
				new WorkflowArgument("effect") { Default = "read_only", Allowed = ScriptEffects }
			],
			Gates = [McpFeature.UnsafeLua]
		};
}
