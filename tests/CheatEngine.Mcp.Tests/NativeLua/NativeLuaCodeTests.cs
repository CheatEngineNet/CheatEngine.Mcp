using System.Text;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Core.Jobs;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Code;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

using Microsoft.Extensions.Options;

namespace CheatEngine.Mcp.Tests.NativeLua;

/// <summary>The fixed Lua bodies of the <c>code_*</c> tools, compiled and run against CE-API stubs.</summary>
public sealed partial class NativeLuaToolRuntimeTests
{
	private const string CodeDissectorStubs = """
	                                          getAddressSafe = function(value) return value end
	                                          referencedAddress = nil
	                                          dissector = {}
	                                          dissector.getReferences = function(address)
	                                              referencedAddress = address
	                                              return { [0x10000] = 0, [0xFFFF] = 1, [0x2000] = 2, [-4096] = 3 }
	                                          end
	                                          dissector.getReferencedStrings = function()
	                                              return { [0x10000] = 'Alpha', [0xFFFF] = 'beta', [0x2000] = 'Graph', [-4096] = 'kernel' }
	                                          end
	                                          getDissectCode = function() return dissector end
	                                          """;

	public static TheoryData<string, string> CodeScriptBodies => new()
	{
		{ nameof(CodeScripts.DisassemblyColumns), CodeScripts.DisassemblyColumns },
		{ nameof(CodeScripts.DisassembleBytes), CodeScripts.DisassembleBytes },
		{ nameof(CodeScripts.GetFunction), CodeScripts.GetFunction },
		{ nameof(CodeScripts.Dissect), CodeScripts.Dissect },
		{ nameof(CodeScripts.FindReferences), CodeScripts.FindReferences },
		{ nameof(CodeScripts.FindStrings), CodeScripts.FindStrings },
		{ nameof(CodeScripts.ListFunctions), CodeScripts.ListFunctions },
		{ nameof(CodeScripts.GetComments), CodeScripts.GetComments },
		{ nameof(CodeScripts.SetComment), CodeScripts.SetComment },
		{ nameof(CodeScripts.ClearDissect), CodeScripts.ClearDissect }
	};

	[Theory]
	[MemberData(nameof(CodeScriptBodies))]
	public void CodeScript_WithRepresentativeArguments_CompilesAndNeverLoadsCode(string name, string body)
	{
		using RuntimeScope scope = CreateScope();
		object?[] arguments = name switch
		{
			nameof(CodeScripts.DisassemblyColumns) => [0x401000UL, "90"],
			nameof(CodeScripts.DisassembleBytes) => ["488B05", "1000"],
			nameof(CodeScripts.GetFunction) => ["game.exe+10", 4096],
			nameof(CodeScripts.Dissect) => [0x140001000UL, 4096],
			nameof(CodeScripts.FindReferences) => [0x140001000UL, 0, 100, CodeTools.MaximumScannedEntries],
			nameof(CodeScripts.FindStrings) => ["text", 0, 100, CodeTools.MaximumScannedEntries],
			nameof(CodeScripts.ListFunctions) => [0, 100],
			nameof(CodeScripts.GetComments) => [new[] { "game.exe+10", "1000" }],
			nameof(CodeScripts.SetComment) => ["game.exe+10", "comment"],
			_ => []
		};

		LuaFixedScriptAssert.NeverLoadsCode(body);
		Assert.Empty(LuaFeatureScan.Scan(body));
		CodeAssertCompiles(name, LuaToolRuntime.BuildSource(body, 100, arguments));
	}

	[Fact]
	public void CodeDisassemblyColumns_Ce77StackOrder_PreservesDisplayColumns()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
		             disassemble = function(address) assert(address == 0x401000); return 'game.exe+1000 - 90 - nop' end
		             splitDisassembledString = function(text)
		                 assert(text == 'game.exe+1000 - 90 - nop')
		                 return 'annotation', 'nop', '90', 'game.exe+1000'
		             end
		             """);
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());

		CodeLuaDisassemblyColumns columns = dispatch.RunLua("code_decode", CodeScripts.DisassemblyColumns,
			CodeLuaJsonContext.Default.CodeLuaDisassemblyColumns, CancellationToken.None, 0x401000UL, "90");

		Assert.Equal(new CodeLuaDisassemblyColumns("game.exe+1000", "nop", "annotation"), columns);
	}

	[Theory]
	[InlineData("91")]
	[InlineData("90 90")]
	[InlineData("")]
	public void CodeDisassemblyColumns_InstructionBytesChanged_RefusesInconsistentColumns(string bytes)
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
			disassemble = function(_) return 'changed instruction' end
			splitDisassembledString = function(_) return '', 'changed opcode', changedBytes, '401000' end
			""" + "\nchangedBytes = " + System.Text.Json.JsonSerializer.Serialize(bytes));
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			dispatch.RunLua("code_decode", CodeScripts.DisassemblyColumns,
				CodeLuaJsonContext.Default.CodeLuaDisassemblyColumns, CancellationToken.None, 0x401000UL, "90"));

		Assert.Equal((ToolErrorKind.HostRefused, ToolHostEffect.Completed),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Contains("instruction changed", exception.Error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void CodeFindReferences_Stubbed_SortsFromAddressesNumericallyAndUnsigned()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(CodeDissectorStubs);
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());

		CodeLuaReferencePage all = dispatch.RunLua("code_find_references", CodeScripts.FindReferences,
			CodeLuaJsonContext.Default.CodeLuaReferencePage, CancellationToken.None, 0x5000UL, 0, 10,
			CodeTools.MaximumScannedEntries);
		CodeLuaReferencePage page = dispatch.RunLua("code_find_references", CodeScripts.FindReferences,
			CodeLuaJsonContext.Default.CodeLuaReferencePage, CancellationToken.None, 0x5000UL, 1, 2,
			CodeTools.MaximumScannedEntries);

		Assert.Equal(0x5000L, ReadGlobal("referencedAddress"));
		Assert.Equal(("5000", 4, true, (int?) null), (all.Address, all.Total, all.Exact, all.NextOffset));
		Assert.Equal(
		[
			new CodeLuaReference("2000", "5000", "2"), new CodeLuaReference("FFFF", "5000", "1"),
			new CodeLuaReference("10000", "5000", "0"), new CodeLuaReference("FFFFFFFFFFFFF000", "5000", "3")
		], all.References);
		Assert.Equal(["FFFF", "10000"], page.References.Select(static reference => reference.FromAddress));
		Assert.Equal(3, page.NextOffset);
	}

	[Fact]
	public void CodeFindStrings_Stubbed_FiltersThenSortsAddressesNumerically()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(CodeDissectorStubs);
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());

		CodeLuaStringPage all = dispatch.RunLua("code_find_strings", CodeScripts.FindStrings,
			CodeLuaJsonContext.Default.CodeLuaStringPage, CancellationToken.None, null, 0, 10,
			CodeTools.MaximumScannedEntries);
		CodeLuaStringPage filtered = dispatch.RunLua("code_find_strings", CodeScripts.FindStrings,
			CodeLuaJsonContext.Default.CodeLuaStringPage, CancellationToken.None, "PH", 0, 10,
			CodeTools.MaximumScannedEntries);

		Assert.Equal(
		[
			new CodeLuaString("2000", "Graph"), new CodeLuaString("FFFF", "beta"),
			new CodeLuaString("10000", "Alpha"), new CodeLuaString("FFFFFFFFFFFFF000", "kernel")
		], all.Strings);
		Assert.Equal((4, true), (all.Total, all.Exact));
		Assert.Equal(["2000", "10000"], filtered.Strings.Select(static value => value.Address));
		Assert.Equal(2, filtered.Total);
	}

	[Fact]
	public void CodeDisassembleBytes_SpacedText_ReachesDisassembleBytesAsContiguousDigits()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
		             getAddressSafe = function(value) return tonumber(value, 16) end
		             receivedBytes = nil; receivedOrigin = nil
		             disassembleBytes = function(bytes, origin)
		                 receivedBytes = bytes; receivedOrigin = origin
		                 return 'mov rax,[00001007]', {}
		             end
		             """);
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());
		IOptions<McpExecutionOptions> execution = Options.Create(new McpExecutionOptions());
		using JobRegistry jobs = new(dispatch, new TargetResources(), execution, TimeProvider.System);

		CodeByteDisassembly result = new CodeTools(dispatch, jobs).DisassembleBytes("48 8b\t05\n00 00 00 00", "1000",
			Token);

		Assert.Equal(new CodeByteDisassembly("1000", "mov rax,[00001007]"), result);
		Assert.Equal(("488B0500000000", 0x1000L), (ReadGlobal("receivedBytes"), ReadGlobal("receivedOrigin")));
	}

	/// <summary>Loads a code script without running it, and fails with Lua's message when it does not compile.</summary>
	private static void CodeAssertCompiles(string name, string source)
	{
		LuaAdmissionStatus admission = LuaRuntime.TryAcquireOperationWithOutcome(out LuaRuntimeOperation acquired);
		Assert.Equal(LuaAdmissionStatus.Admitted, admission);
		using LuaRuntimeOperation operation = acquired;
		LuaState state = operation.State;
		using LuaFrame frame = new(state);
		LuaStatus status = state.TryLoad(Encoding.UTF8.GetBytes(source),
			Encoding.UTF8.GetBytes("=CheatEngine.Mcp/" + name));
		Assert.True(status.IsOk, status.IsOk ? null : LuaError.FromStack(state, status).Message);
	}
}
