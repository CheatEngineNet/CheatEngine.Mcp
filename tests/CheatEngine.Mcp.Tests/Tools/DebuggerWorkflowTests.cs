using CheatEngine.Client;
using CheatEngine.Client.Lua;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools;

namespace CheatEngine.Mcp.Tests.Tools;

public sealed class DebuggerWorkflowTests
{
	[Fact]
	public void StartCapture_InvalidBounds_RefusesBeforeLuaExecution()
	{
		int calls = 0;
		LuaDebuggerCaptureTool tool = new(CreateClient(() => calls++));

		object result = tool.StartCapture("game+10", size: 3, maximumHits: 0);

		ToolResultAssert.IsFailure(result,
			"address is required, size must be 1, 2, 4, or 8, maximumHits must be between 1 and 1024, and lifetimeSeconds must be between 1 and 300.");
		Assert.Equal(0, calls);
	}

	[Fact]
	public void SetRegister_InvalidName_RefusesBeforeLuaExecution()
	{
		int calls = 0;
		LuaDebuggerTool tool = new(CreateClient(() => calls++));

		object result = tool.SetRegister("XMM0", 1);

		ToolResultAssert.IsFailure(result, "register must name a supported general-purpose register.");
		Assert.Equal(0, calls);
	}

	[Fact]
	public void StartStepTrace_InvalidBounds_RefusesBeforeLuaExecution()
	{
		int calls = 0;
		LuaDebuggerTraceTool tool = new(CreateClient(() => calls++));

		object result = tool.Start("game+10", 0, 61);

		ToolResultAssert.IsFailure(result,
			"address is required, maximumSteps must be between 1 and 256, and lifetimeSeconds must be between 1 and 60.");
		Assert.Equal(0, calls);
	}

	[Fact]
	public void PollCapture_InvalidBounds_RefusesBeforeLuaExecution()
	{
		int calls = 0;
		LuaDebuggerCaptureTool tool = new(CreateClient(() => calls++));

		object result = tool.PollCapture("capture", 1_025);

		ToolResultAssert.IsFailure(result, "maximumHits must be between 1 and 1024.");
		Assert.Equal(0, calls);
	}

	[Fact]
	public void AddBreakpoint_ActiveCaptureGuard_RefusesBeforeMutationExecution()
	{
		int guardCalls = 0;
		int mutationCalls = 0;
		ILuaClient lua = ClientTestDouble.Create<ILuaClient>((method, arguments) =>
		{
			Assert.Equal(nameof(ILuaClient.Execute), method.Name);
			LuaToolRuntime.LuaToolOperation operation =
				Assert.IsAssignableFrom<LuaToolRuntime.LuaToolOperation>(arguments![0]);
			if (operation.Operation == "debugger_capture_guard")
			{
				guardCalls++;
				return true;
			}

			mutationCalls++;
			return new Dictionary<string, object?>();
		});
		ICheatEngineClient client = ClientTestDouble.Client((nameof(ICheatEngineClient.Lua), lua));
		LuaDebuggerTool tool = new(client, new LuaDebuggerCaptureGuard(client));

		object result = tool.AddBreakpoint("game+10");

		ToolResultAssert.IsFailure(result,
			"Debugger capture or step trace is still active; stop it before changing debugger state or target.");
		Assert.Equal(1, guardCalls);
		Assert.True(mutationCalls == 0, "An active debugger workflow must prevent the mutation script from executing.");
	}

	private static ICheatEngineClient CreateClient(Action call)
	{
		ILuaClient lua = ClientTestDouble.Create<ILuaClient>((_, _) =>
		{
			call();
			return new Dictionary<string, object?>();
		});
		return ClientTestDouble.Client((nameof(ICheatEngineClient.Lua), lua));
	}
}
