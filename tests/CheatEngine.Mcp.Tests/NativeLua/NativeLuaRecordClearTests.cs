using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Tools.Record;
using CheatEngine.Mcp.Tools.Symbol;

namespace CheatEngine.Mcp.Tests.NativeLua;

/// <summary>Record clearing and system symbols against real Lua with only the CE globals stubbed.</summary>
public sealed partial class NativeLuaToolRuntimeTests
{
	[Fact]
	public void RecordClear_NestedStubbedAddressList_CountsEveryChildBeforeClearing()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
		             vtAutoAssembler = 17
		             clearCalls = 0
		             childA = {Type = 0, Active = false, Count = 0, Child = {}}
		             childB = {Type = 0, Active = false, Count = 0, Child = {}}
		             childC = {Type = 0, Active = false, Count = 0, Child = {}}
		             topA = {Type = 0, Active = false, Count = 2, Child = {[0] = childA, [1] = childB}}
		             topB = {Type = 0, Active = false, Count = 1, Child = {[0] = childC}}
		             addressList = {Count = 2}
		             addressList.getMemoryRecord = function(index) return index == 0 and topA or topB end
		             addressList.clear = function() clearCalls = clearCalls + 1; addressList.Count = 0 end
		             getAddressList = function() return addressList end
		             """);

		RecordClearResult result = new RecordClearTools(CreateNativeDispatch(new McpFeatureOptions())).Clear(Token);

		Assert.Equal(new RecordClearResult(5), result);
		Assert.Equal(1L, ReadGlobal("clearCalls"));
		Assert.Equal(0L, ModuleSymbolRead("addressList.Count"));
	}

	[Fact]
	public void RecordClear_ActiveAutoAssemblerWithoutGate_LeavesTheAddressListUntouched()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
		             vtAutoAssembler = 17
		             clearCalls = 0
		             active = {Type = vtAutoAssembler, Active = true, Count = 0, Child = {}}
		             addressList = {Count = 1}
		             addressList.getMemoryRecord = function(_) return active end
		             addressList.clear = function() clearCalls = clearCalls + 1; addressList.Count = 0 end
		             getAddressList = function() return addressList end
		             """);

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new RecordClearTools(CreateNativeDispatch(new McpFeatureOptions { EnableAutoAssembler = false }))
				.Clear(Token));

		Assert.Equal((ToolErrorKind.CapabilityDisabled, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal((1L, 0L), (ModuleSymbolRead("addressList.Count"), ReadGlobal("clearCalls")));
	}

	[Fact]
	public void SymbolEnableSources_WindowsStub_ReportsExternalAccess()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
		             windowsCalls = 0
		             kernelCalls = 0
		             enableWindowsSymbols = function() windowsCalls = windowsCalls + 1 end
		             enableKernelSymbols = function() kernelCalls = kernelCalls + 1 end
		             """);

		SymbolSourcesEnabled result = ModuleSymbolTools(CreateNativeDispatch(new McpFeatureOptions()))
			.EnableSources(true, false, Token);

		Assert.Equal(new SymbolSourcesEnabled(true, false, true), result);
		Assert.Equal((1L, 0L), (ReadGlobal("windowsCalls"), ReadGlobal("kernelCalls")));
	}

	[Fact]
	public void SymbolEnableSources_KernelFailureAfterWindows_ReportsPartialEffect()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
		             windowsCalls = 0
		             kernelCalls = 0
		             enableWindowsSymbols = function() windowsCalls = windowsCalls + 1 end
		             enableKernelSymbols = function() kernelCalls = kernelCalls + 1; error('kernel driver refused') end
		             """);

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			ModuleSymbolTools(CreateNativeDispatch(new McpFeatureOptions { EnableKernelAccess = true }))
				.EnableSources(true, true, Token));

		Assert.Equal((ToolErrorKind.PartialEffect, ToolHostEffect.Started),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal((1L, 1L), (ReadGlobal("windowsCalls"), ReadGlobal("kernelCalls")));
	}
}
