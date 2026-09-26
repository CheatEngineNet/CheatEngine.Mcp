using CheatEngine.Client;
using CheatEngine.Client.Assembly;
using CheatEngine.Client.Inspection;
using CheatEngine.Client.Lua;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Tools;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Mcp.Tests;

public sealed unsafe partial class NativeLuaToolRuntimeTests
{
	[Fact]
	public void AssemblyTool_Disassemble_UsesNativeLuaColumnsAndTypedBytes()
	{
		using RuntimeScope scope = CreateScope();
		InstallDisassemblyStubs();
		AssemblyTool tool = new(CreateAssemblyLuaClient(CreateTypedSnapshot));

		object result = tool.Disassemble("0x401000");

		ToolResultAssert.IsSuccess(result);
		Assert.Equal("push rbx", ToolResultAssert.GetProperty<string>(result, "Opcode"));
		Assert.Equal("[annotation]", ToolResultAssert.GetProperty<string>(result, "Extra"));
		Assert.Equal("0000000000401000", ToolResultAssert.GetProperty<string>(result, "AddressText"));
		string[] bytes = ToolResultAssert.GetProperty<string[]>(result, "bytes");
		Assert.Single(bytes);
		Assert.Equal("53", bytes[0]);
		Assert.Equal(1, ToolResultAssert.GetProperty<int>(result, "size"));
	}

	[Fact]
	public void AssemblyTool_DisassembleRange_UsesNativeLuaColumnsAndTypedLengths()
	{
		using RuntimeScope scope = CreateScope();
		InstallDisassemblyStubs();
		AssemblyTool tool = new(CreateAssemblyLuaClient(CreateTypedSnapshot));

		object result = tool.DisassembleRange("0x401000", 2);

		ToolResultAssert.IsSuccess(result);
		List<object> instructions = ToolResultAssert.GetProperty<List<object>>(result, "instructions");
		Assert.Equal(2, instructions.Count);
		object push = instructions[0];
		object ret = instructions[1];
		Assert.Equal("0x401000", ToolResultAssert.GetProperty<string>(push, "address"));
		Assert.Equal("push rbx", ToolResultAssert.GetProperty<string>(push, "opcode"));
		Assert.Equal("[annotation]", ToolResultAssert.GetProperty<string>(push, "extra"));
		Assert.Equal("53", ToolResultAssert.GetProperty<string>(push, "bytes"));
		Assert.Equal(1, ToolResultAssert.GetProperty<int>(push, "size"));
		Assert.Equal("0x401001", ToolResultAssert.GetProperty<string>(ret, "address"));
		Assert.Equal("ret", ToolResultAssert.GetProperty<string>(ret, "opcode"));
		Assert.Equal(string.Empty, ToolResultAssert.GetProperty<string>(ret, "extra"));
		Assert.Equal("C3", ToolResultAssert.GetProperty<string>(ret, "bytes"));
		Assert.Equal(1, ToolResultAssert.GetProperty<int>(ret, "size"));
	}

	[Fact]
	public void LuaCodeTool_GetPreviousOpcodes_MapsNativeLuaColumnsInStackOrder()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
			getAddressSafe=function(_) return 0x401002 end
			getPreviousOpcode=function(address) if address==0x401002 then return 0x401001 end return nil end
			disassemble=function(address) return address end
			splitDisassembledString=function(address) return '', 'ret', 'C3', '0000000000401001' end
			""");
		LuaCodeTool tool = new(CreateDirectLuaClient());

		Dictionary<string, object?> result = ResultMap(tool.GetPreviousOpcodes("target"));

		Dictionary<string, object?> instruction = Assert.IsType<Dictionary<string, object?>>(Assert.Single(Assert.IsType<object?[]>(result["instructions"])));
		Assert.Equal("ret", instruction["opcode"]);
		Assert.Equal(string.Empty, instruction["extra"]);
		Assert.Equal("0000000000401001", instruction["addressText"]);
		Assert.Equal("C3", instruction["bytes"]);
	}

	private static ICheatEngineClient CreateAssemblyLuaClient(Func<Address, AssemblyInstructionSnapshot> disassemble)
	{
		ILuaClient lua = ClientTestDouble.Create<ILuaClient>((method, arguments) =>
		{
			Assert.Equal(nameof(ILuaClient.Execute), method.Name);
			ILuaOperation<object?> operation = Assert.IsAssignableFrom<ILuaOperation<object?>>(arguments![0]);
			if (!operation.TryExecute(ActiveContext.Instance, out object? result, out CheatEngineFailure failure))
			{
				failure.Throw();
			}

			return result;
		});
		IAssemblyClient assembly = ClientTestDouble.Create<IAssemblyClient>((method, arguments) => method.Name == nameof(IAssemblyClient.Disassemble)
			? disassemble((Address) arguments![0]!)
			: throw new Xunit.Sdk.XunitException($"Unexpected assembly call: {method.Name}."));
		IInspectionClient inspection = ClientTestDouble.Create<IInspectionClient>((method, _) => method.Name == nameof(IInspectionClient.ResolveAddress)
			? new Address(0x401000)
			: throw new Xunit.Sdk.XunitException($"Unexpected inspection call: {method.Name}."));
		return ClientTestDouble.Client((nameof(ICheatEngineClient.Lua), lua), (nameof(ICheatEngineClient.Assembly), assembly),
			(nameof(ICheatEngineClient.Inspection), inspection));
	}

	private static AssemblyInstructionSnapshot CreateTypedSnapshot(Address address) => address.Value switch
	{
		0x401000 => new AssemblyInstructionSnapshot(address, 1, string.Empty, "53", "100048290", [0x53]),
		0x401001 => new AssemblyInstructionSnapshot(address, 1, string.Empty, "C3", "100048291", [0xC3]),
		_ => throw new Xunit.Sdk.XunitException($"Unexpected typed disassembly address: 0x{address.Value:X}.")
	};

	private static void InstallDisassemblyStubs() => InstallStubs("""
		disassemble=function(address) return address end
		splitDisassembledString=function(address)
		  if address==0x401000 then return '[annotation]', 'push rbx', '53', '0000000000401000' end
		  if address==0x401001 then return '', 'ret', 'C3', '0000000000401001' end
		  error('unexpected disassembly address')
		end
		""");
}
