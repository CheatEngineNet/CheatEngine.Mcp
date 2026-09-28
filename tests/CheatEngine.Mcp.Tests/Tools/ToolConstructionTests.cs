using System.Reflection;

using CheatEngine.Client;
using CheatEngine.Mcp.Core.Files;
using CheatEngine.Mcp.Core.Jobs;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Processes;
using CheatEngine.Mcp.Tools.Scan;

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
		Type[] toolTypes = TestComposition.BackendManifest.Primitives
			.Where(static primitive => primitive.Kind == CheatEngineMcpPrimitiveKind.Tool)
			.Select(static primitive => primitive.Type)
			.OrderBy(static type => type.FullName, StringComparer.Ordinal)
			.ToArray();

		using TestActivation activation = new(client);
		McpPrimitiveTargets targets = activation.Targets;
		Assert.NotNull(activation.Module);

		// Every registered tool container can be constructed without touching the Client.
		Assert.All(toolTypes.Where(static type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
				.Any(static method => method.GetCustomAttribute<McpServerToolAttribute>() is not null)),
			type => Assert.IsType(type, targets.Get(type)));
		activation.DisposeScope();
		Assert.Empty(accessed);
	}

	[Fact]
	public void Activation_ProcessChangingTools_ReceiveEveryTargetTransitionGuard()
	{
		using TestActivation activation = new(ClientTestDouble.Client());
		TargetTransitionGuards guards = activation.Services.GetRequiredService<TargetTransitionGuards>();

		// Every guard is consulted: retained resources and jobs, plus the running main scan.
		Assert.Equal([typeof(TargetResources), typeof(MainScannerTransitionGuard)],
			guards.Guards.Select(static guard => guard.GetType()).ToArray());
		Assert.Same(activation.Services.GetRequiredService<TargetResources>(), guards.Guards[0]);
		Assert.IsType<MainScannerTransitionGuard>(guards.Guards[1]);
		Assert.Same(activation.Services.GetRequiredService<JobRegistry>(),
			activation.Services.GetRequiredService<JobRegistry>());
		Assert.Same(activation.Services.GetRequiredService<McpStateLedger>(),
			activation.Services.GetRequiredService<TargetResources>().Ledger);
		Assert.Same(guards, typeof(ProcessTools).GetField("_guards", BindingFlags.Instance | BindingFlags.NonPublic)!
			.GetValue(activation.Targets.Get(typeof(ProcessTools))));

		Assert.Same(activation.Services.GetRequiredService<TargetResources>(),
			typeof(ProcessTools).GetField("_resources", BindingFlags.Instance | BindingFlags.NonPublic)!
				.GetValue(activation.Targets.Get(typeof(ProcessTools))));
		Assert.Same(activation.Services.GetRequiredService<McpFilePaths>(),
			typeof(ProcessTools).GetField("_files", BindingFlags.Instance | BindingFlags.NonPublic)!
				.GetValue(activation.Targets.Get(typeof(ProcessTools))));
	}
}
