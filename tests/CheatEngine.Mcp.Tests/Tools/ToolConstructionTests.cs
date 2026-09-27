using System.Reflection;

using CheatEngine.Client;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools;

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
		Type[] toolTypes = typeof(ProcessTool).Assembly.GetTypes()
			.Where(static type => type.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
			.OrderBy(static type => type.FullName, StringComparer.Ordinal)
			.ToArray();

		using TestActivation activation = new(client);
		McpPrimitiveTargets targets = activation.Targets;
		Assert.NotNull(activation.Module);

		Assert.Equal(28, toolTypes.Length);
		Assert.All(toolTypes, type => Assert.IsType(type, targets.Get(type)));
		activation.DisposeScope();
		Assert.Empty(accessed);
	}

	[Fact]
	public void Activation_ProcessChangingTools_ReceiveEveryTargetTransitionGuard()
	{
		using TestActivation activation = new(ClientTestDouble.Client());
		foreach (Type type in new[] { typeof(ProcessTool), typeof(LuaProcessTool) })
		{
			object tool = activation.Targets.Get(type);
			foreach (string field in new[] { "_targetResources", "_debuggerGuard", "_scans" })
			{
				Assert.NotNull(type.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(tool));
			}
		}

		// One activation shares one scan registry between the scan tools and the process transition guard.
		Assert.Same(activation.Targets.Get(typeof(ScanTool)),
			typeof(ProcessTool).GetField("_scans", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(
				activation.Targets.Get(typeof(ProcessTool))));
	}
}
