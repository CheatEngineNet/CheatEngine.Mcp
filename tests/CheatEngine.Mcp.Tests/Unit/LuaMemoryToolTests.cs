using CheatEngine.Client;
using CheatEngine.Client.Lua;
using CheatEngine.Mcp.Tools;

namespace CheatEngine.Mcp.Tests;

public sealed class LuaMemoryToolTests
{
	[Theory]
	[InlineData(0L)]
	[InlineData(1_048_577L)]
	public void SetMemoryProtection_SizeOutsideLimit_RefusesBeforeLuaExecution(long size)
	{
		int calls = 0;
		LuaMemoryTool tool = new(CreateClient(() => calls++));

		object result = tool.SetMemoryProtection("target+10", size);

		ToolResultAssert.IsFailure(result, "size must be between 1 and 1048576 bytes.");
		Assert.True(calls == 0, "Invalid range sizes must not reach the Lua runtime.");
	}

	[Fact]
	public void CopyMemory_EmptyDestination_RefusesBeforeLuaExecution()
	{
		int calls = 0;
		LuaMemoryTool tool = new(CreateClient(() => calls++));

		object result = tool.CopyMemory("source", 16, "");

		ToolResultAssert.IsFailure(result, "destinationAddress is required.");
		Assert.True(calls == 0, "A copy requires an explicit Client-owned destination address.");
	}

	[Fact]
	public void SetMemoryProtection_ValidInput_ResolvesAddressAndReturnsReadBackProtection()
	{
		LuaToolRuntime.LuaToolOperation? captured = null;
		ILuaClient lua = ClientTestDouble.Create<ILuaClient>((method, arguments) =>
		{
			Assert.Equal(nameof(ILuaClient.Execute), method.Name);
			captured = Assert.IsAssignableFrom<LuaToolRuntime.LuaToolOperation>(arguments![0]);
			return new Dictionary<string, object?> { ["applied"] = true };
		});
		LuaMemoryTool tool = new(ClientTestDouble.Client((nameof(ICheatEngineClient.Lua), lua)));

		object result = tool.SetMemoryProtection("module+10", 64, read: true, write: true);

		ToolResultAssert.IsSuccess(result);
		Assert.NotNull(captured);
		Assert.Contains("getAddressSafe(a[1])", captured.Source, StringComparison.Ordinal);
		Assert.Contains("getMemoryProtection(resolved)", captured.Source, StringComparison.Ordinal);
		Assert.Contains("actual.r == a[3]", captured.Source, StringComparison.Ordinal);
		Assert.Contains("string.format('0x%X', resolved)", captured.Source, StringComparison.Ordinal);
	}

	private static ICheatEngineClient CreateClient(Action call)
	{
		ILuaClient lua = ClientTestDouble.Create<ILuaClient>((_, _) =>
		{
			call();
			return null;
		});
		return ClientTestDouble.Client((nameof(ICheatEngineClient.Lua), lua));
	}
}
