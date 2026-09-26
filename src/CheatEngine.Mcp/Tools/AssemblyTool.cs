using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;

using CheatEngine.Client;
using CheatEngine.Client.Assembly;
using CheatEngine.Client.Inspection;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Values;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools;

/// <summary>Disassembles target instructions and resolves target symbol expressions through the Client.</summary>
[McpServerToolType]
public sealed class AssemblyTool
{
	private readonly ICheatEngineClient _client;

	public AssemblyTool(ICheatEngineClient client)
	{
		_client = client;
	}

	[McpServerTool(Name = "disassemble_range"), Description("Disassemble a bounded sequence of target instructions using the Client's typed assembly API.")]
	public object DisassembleRange([Description("Start address or symbol expression.")] string address,
		[Description("Instruction count (1-1024).")] int count = 20)
	{
		return ToolExecution.Run(_client, () =>
		{
			if (count is < 1 or > 1024)
			{
				return ToolExecution.Error("count must be between 1 and 1024.");
			}

			Address current = ToolExecution.Address(_client, address);
			List<object> instructions = [];
			for (int index = 0; index < count; index++)
			{
				AssemblyInstructionSnapshot instruction = _client.Assembly.Disassemble(current, _client.Stopping);
				instructions.Add(new
				{
					address = $"0x{current.Value:X}",
					opcode = instruction.Opcode,
					extra = instruction.Extra,
					bytes = Convert.ToHexString(instruction.Bytes.AsSpan()),
					size = instruction.Length
				});
				current = new Address(checked(current.Value + (ulong) instruction.Length));
			}
			return new
			{
				success = true,
				count = instructions.Count,
				instructions
			};
		});
	}

	[McpServerTool(Name = "disassemble"), Description("Disassemble one instruction or obtain its exact length at a target address.")]
	public object Disassemble(
		[Description("Target address as hexadecimal.")] string address,
		[Description("disassemble (default) or get-instruction-size.")] string? requestType = null)
	{
		return ToolExecution.Run(_client, () =>
		{
			Address target = ToolExecution.Address(_client, address);
			if (string.IsNullOrWhiteSpace(requestType) || string.Equals(requestType, "disassemble", StringComparison.OrdinalIgnoreCase))
			{
				AssemblyInstructionSnapshot instruction = _client.Assembly.Disassemble(target);
				return new
				{
					success = true,
					address = $"0x{instruction.Address.Value:X}",
					instruction.AddressText,
					instruction.Opcode,
					instruction.Extra,
					text = instruction.Text,
					bytes = instruction.Bytes.Select(static value => value.ToString("X2", CultureInfo.InvariantCulture)).ToArray(),
					size = instruction.Length
				};
			}

			if (string.Equals(requestType, "get-instruction-size", StringComparison.OrdinalIgnoreCase))
			{
				return new
				{
					success = true,
					result = _client.Assembly.GetInstructionLength(target).ToString(CultureInfo.InvariantCulture)
				};
			}

			return ToolExecution.Error("Request type must be disassemble or get-instruction-size.");
		});
	}

	[McpServerTool(Name = "resolve_address"), Description("Resolve a target-process symbol expression, such as game.exe+10.")]
	public object ResolveAddress(
		[Description("Target-process symbol expression.")] string addressString,
		[Description("Use Cheat Engine shallow symbol resolution.")] bool shallow = false)
	{
		return ToolExecution.Run(_client, () =>
		{
			ArgumentException.ThrowIfNullOrWhiteSpace(addressString);
			Address address = _client.Inspection.ResolveAddress(
				new SymbolExpression(addressString),
				shallow ? AddressResolutionMode.Shallow : AddressResolutionMode.Default);
			return new
			{
				success = true,
				address = $"0x{address.Value:X}"
			};
		});
	}
}
