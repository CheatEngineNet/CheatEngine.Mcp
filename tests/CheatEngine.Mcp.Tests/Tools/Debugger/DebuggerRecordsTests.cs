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

	[Fact]
	public void CaptureItem_SourceGeneratedContext_WritesEffectiveAddressGroupsAndOmitsThemForOtherItems()
	{
		DebuggerCaptureContext context = new("401010", 17, "401010", false, "mov eax,qword ptr [r12]",
			new Dictionary<string, string> { ["R12"] = "3000" });

		using JsonDocument grouped = JsonDocument.Parse(JsonSerializer.Serialize(
			new DebuggerCaptureItem(context, 2, context, context, "3000", 8),
			DebuggerJsonContext.Default.DebuggerCaptureItem));
		using JsonDocument single = JsonDocument.Parse(JsonSerializer.Serialize(new DebuggerCaptureItem(context, 1),
			DebuggerJsonContext.Default.DebuggerCaptureItem));

		Assert.Equal("3000", grouped.RootElement.GetProperty("effectiveAddress").GetString());
		Assert.Equal(8, grouped.RootElement.GetProperty("operandSize").GetInt32());
		Assert.Equal(2, grouped.RootElement.GetProperty("hitCount").GetInt64());
		Assert.False(single.RootElement.TryGetProperty("effectiveAddress", out _));
		Assert.False(single.RootElement.TryGetProperty("operandSize", out _));
		Assert.False(single.RootElement.TryGetProperty("firstContext", out _));
	}
}
