using CheatEngine.Client.Lua;
using CheatEngine.Mcp.Tools;

namespace CheatEngine.Mcp.Tests;

public sealed class LuaExecutionToolTests
{
	[Fact]
	public void ExecuteLua_UnsafeCapabilityDisabled_ReturnsFailureWithoutClientDispatch()
	{
		LuaExecutionTool tool = new(ClientTestDouble.Client());

		object result = tool.ExecuteLua("return 1");

		ToolResultAssert.IsFailure(result, "Unsafe Lua execution is disabled by this server.");
	}

	[Fact]
	public void ExecuteLua_UnsafeCapabilityEnabled_ExecutesTheRequestedChunk()
	{
		int executeCalls = 0;
		LuaScript? observedScript = null;
		IUnsafeLuaClient unsafeLua = ClientTestDouble.Create<IUnsafeLuaClient>((method, arguments) =>
		{
			if (method.Name == nameof(IUnsafeLuaClient.Execute))
			{
				executeCalls++;
				observedScript = (LuaScript) arguments![0]!;
				return null;
			}

			throw new Xunit.Sdk.XunitException($"Unexpected unsafe Lua method: {method.Name}");
		});
		LuaExecutionTool tool = new(ClientTestDouble.Client(), unsafeLua);

		object result = tool.ExecuteLua("return 42", "mcp-test");

		ToolResultAssert.IsSuccess(result);
		Assert.Equal(1, executeCalls);
		Assert.NotNull(observedScript);
		Assert.Equal("return 42", observedScript.Value.Source);
		Assert.Equal("mcp-test", observedScript.Value.ChunkName);
	}
}
