using CheatEngine.Client;
using CheatEngine.Client.Lua;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Lua;

using Microsoft.Extensions.Options;

using Xunit.Sdk;

namespace CheatEngine.Mcp.Tests.Tools.Lua;

/// <summary>Contract tests for bounds that must refuse <c>lua_find_api</c> before Cheat Engine receives Lua.</summary>
public sealed class LuaToolsTests
{
	[Theory]
	[InlineData("")]
	[InlineData(" ")]
	[InlineData("get\tAddress")]
	public void FindApi_EmptyWhitespaceOrControlQuery_RefusesBeforeClientDispatch(string query)
	{
		LuaToolHarness harness = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			harness.Tools.FindApi(query, cancellationToken: TestContext.Current.CancellationToken));

		Assert.Equal(ToolErrorKind.InvalidArgument, exception.Error.Kind);
		Assert.Equal(ToolHostEffect.NotStarted, exception.Error.HostEffect);
		Assert.Contains("query:", exception.Error.Message, StringComparison.Ordinal);
		Assert.Equal(0, harness.LuaCalls);
	}

	[Fact]
	public void FindApi_QueryOverTheUtf8Bound_RefusesBeforeClientDispatch()
	{
		LuaToolHarness harness = new();
		string query = new('\u00e9', (LuaTools.MaximumQueryBytes / 2) + 1);

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			harness.Tools.FindApi(query, cancellationToken: TestContext.Current.CancellationToken));

		Assert.Equal(ToolErrorKind.LimitExceeded, exception.Error.Kind);
		Assert.Equal(ToolHostEffect.NotStarted, exception.Error.HostEffect);
		Assert.Contains("query:", exception.Error.Message, StringComparison.Ordinal);
		Assert.Equal(0, harness.LuaCalls);
	}

	[Theory]
	[InlineData(0, 0, "limit")]
	[InlineData(101, 0, "limit")]
	[InlineData(1, -1, "offset")]
	[InlineData(1, 65537, "offset")]
	public void FindApi_PageBounds_RefuseBeforeClientDispatch(int limit, int offset, string parameter)
	{
		LuaToolHarness harness = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			harness.Tools.FindApi("getAddress", limit, offset, TestContext.Current.CancellationToken));

		Assert.Equal(ToolErrorKind.LimitExceeded, exception.Error.Kind);
		Assert.Equal(ToolHostEffect.NotStarted, exception.Error.HostEffect);
		Assert.StartsWith(parameter + ":", exception.Error.Message, StringComparison.Ordinal);
		Assert.Equal(0, harness.LuaCalls);
	}

	private sealed class LuaToolHarness
	{
		private int _luaCalls;

		internal LuaToolHarness()
		{
			ILuaClient lua = ClientTestDouble.Create<ILuaClient>((method, _) =>
			{
				Assert.Equal(nameof(ILuaClient.Execute), method.Name);
				Interlocked.Increment(ref _luaCalls);
				throw new XunitException("lua_find_api should have been refused before Client dispatch.");
			});
			ICheatEngineClient client = ClientTestDouble.Client((nameof(ICheatEngineClient.Lua), lua));
			IOptions<McpExecutionOptions> execution = Options.Create(new McpExecutionOptions());
			ToolDispatch dispatch = new(client, new McpFeatureGate(Options.Create(new McpFeatureOptions())), execution,
				new DispatchStatistics(execution), TimeProvider.System, new RecordingLogger<ToolDispatch>());
			Tools = new LuaTools(dispatch);
		}

		internal LuaTools Tools
		{
			get;
		}

		internal int LuaCalls => Volatile.Read(ref _luaCalls);
	}
}
