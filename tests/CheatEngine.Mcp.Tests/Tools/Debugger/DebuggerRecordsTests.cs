using System.Text.Json;

using CheatEngine.Mcp.Tools.Debugger;

namespace CheatEngine.Mcp.Tests.Tools.Debugger;

/// <summary>Wire-format assertions for debugger records that must remain available to Native AOT JSON serialization.</summary>
public sealed class DebuggerRecordsTests
{
	[Fact]
	public void BreakpointSet_SourceGeneratedContext_UsesContractNamesAndOmitsNullThread()
	{
		DebuggerBreakpointSet value = new("breakpoint-a1b2c3d4-1", "401000",
			DebuggerBreakpointTrigger.Execute, 1, DebuggerBreakpointMethod.PageException, null, false);

		string json = JsonSerializer.Serialize(value, DebuggerJsonContext.Default.DebuggerBreakpointSet);
		using JsonDocument document = JsonDocument.Parse(json);
		JsonElement result = document.RootElement;

		Assert.Equal("breakpoint-a1b2c3d4-1", result.GetProperty("resourceId").GetString());
		Assert.Equal("page_exception", result.GetProperty("method").GetString());
		Assert.Equal("execute", result.GetProperty("trigger").GetString());
		Assert.False(result.TryGetProperty("threadId", out _));
	}
}
