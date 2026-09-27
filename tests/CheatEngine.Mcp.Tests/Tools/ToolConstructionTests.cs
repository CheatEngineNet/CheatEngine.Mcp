using System.Reflection;

using CheatEngine.Client;
using CheatEngine.Mcp.Core.Jobs;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools;

using Microsoft.Extensions.DependencyInjection;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tests.Tools;

/// <summary>The real activation composition constructs every tool before OnEnabled, so construction must stay inert.</summary>
public sealed class ToolConstructionTests
{
	[Fact]
	public void Activation_StrictClient_ResolvesEveryToolTargetWithoutTouchingTheClient()
	{
		List<string> accessed = [];
		ICheatEngineClient client = ClientTestDouble.Create<ICheatEngineClient>((method, _) =>
		{
			accessed.Add(method.Name);
			throw new NotSupportedException($"Tool construction must not use Client member {method.Name}.");
		});
		Type[] toolTypes = typeof(CheatEngineToolsBuilderExtensions).Assembly.GetTypes()
			.Where(static type => type.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
			.OrderBy(static type => type.FullName, StringComparer.Ordinal)
			.ToArray();

		using TestActivation activation = new(client);
		McpPrimitiveTargets targets = activation.Targets;
		Assert.NotNull(activation.Module);

		// Every tool container in the assembly is composed, whichever domains have replaced their legacy tools.
		Assert.Equal(toolTypes, TestComposition.BackendManifest.Primitives
			.Where(static primitive => primitive.Kind == CheatEngineMcpPrimitiveKind.Tool)
			.Select(static primitive => primitive.Type).OrderBy(static type => type.FullName, StringComparer.Ordinal));
		Assert.All(toolTypes, type => Assert.IsType(type, targets.Get(type)));
		activation.DisposeScope();
		Assert.Empty(accessed);
	}

	[Fact]
	public void Activation_ProcessChangingTools_ReceiveEveryTargetTransitionGuard()
	{
		using TestActivation activation = new(ClientTestDouble.Client());
		TargetTransitionGuards guards = activation.Services.GetRequiredService<TargetTransitionGuards>();

		// Every guard is consulted: retained resources and orphans, active debugger jobs and the running main scan.
		Assert.Equal([typeof(TargetResources), typeof(LuaDebuggerCaptureGuard), typeof(MainScannerTransitionGuard)],
			guards.Guards.Select(static guard => guard.GetType()).ToArray());
		Assert.Same(activation.Services.GetRequiredService<TargetResources>(), guards.Guards[0]);
		Assert.Same(activation.Services.GetRequiredService<LuaDebuggerCaptureGuard>(), guards.Guards[1]);
		Assert.Same(activation.Services.GetRequiredService<JobRegistry>(),
			activation.Services.GetRequiredService<JobRegistry>());
		Assert.Same(activation.Services.GetRequiredService<McpStateLedger>(),
			activation.Services.GetRequiredService<TargetResources>().Ledger);
		foreach (Type type in new[] { typeof(ProcessTool), typeof(LuaProcessTool) })
		{
			Assert.Same(guards, type.GetField("_guards", BindingFlags.Instance | BindingFlags.NonPublic)!
				.GetValue(activation.Targets.Get(type)));
		}

		Assert.Same(activation.Services.GetRequiredService<TargetResources>(),
			typeof(ProcessTool).GetField("_targetResources", BindingFlags.Instance | BindingFlags.NonPublic)!
				.GetValue(activation.Targets.Get(typeof(ProcessTool))));
	}
}
