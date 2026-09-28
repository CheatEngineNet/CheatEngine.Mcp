using System.ComponentModel;
using System.Globalization;
using System.Text.Json;

using CheatEngine.Client;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.SDK.Engine.Values;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools.Pointer;

/// <summary>Follows one pointer chain hop by hop in live memory.</summary>
[McpServerToolType]
public sealed class PointerChainTools
{
	/// <summary>The most offsets of one chain, the Client's pointer-chain limit.</summary>
	internal const int MaximumOffsets = 64;

	internal const string UnreadableHint =
		"The object may not exist yet (menu, loading screen): retry in the right game state rather than rewriting the chain.";

	private readonly ToolDispatch _dispatch;

	/// <summary>Creates the tool; nothing touches Cheat Engine until a call.</summary>
	/// <param name="dispatch">The activation's dispatch facade.</param>
	public PointerChainTools(ToolDispatch dispatch)
	{
		ArgumentNullException.ThrowIfNull(dispatch);
		_dispatch = dispatch;
	}

	/// <summary>Follows a pointer chain and optionally reads the final value.</summary>
	/// <param name="base">The address that holds the first pointer.</param>
	/// <param name="offsets">The offsets in dereference order.</param>
	/// <param name="valueType">The type of the final value, if it is read.</param>
	/// <param name="length">The byte count or maximum string length of the final value.</param>
	/// <param name="cancellationToken">The MCP request's token.</param>
	/// <returns>Every hop, the final address and the value.</returns>
	[McpServerTool(Name = CheatEngineToolNames.PointerReadChain, Title = "Read a pointer chain", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Follows a pointer chain hop by hop at the target's pointer width and optionally reads the final value. " +
		"Offsets are signed hexadecimal in dereference order: base game.exe+1A2B30 with offsets [\"10\",\"4C8\"] " +
		"reads [[game.exe+1A2B30]+10]+4C8. An unreadable hop fails with memory_read_failed and details " +
		"{hopIndex, readAt}.")]
	public PointerChainResult ReadChain(
		[Description(
			"The address that holds the first pointer: an address, symbol or expression such as game.exe+1A2B30.")]
		string @base,
		[Description("One to 64 signed hexadecimal offsets in dereference order, such as [\"10\", \"-8\"].")]
		string[] offsets,
		[Description("The type of the final value to read; omit it to follow the chain only.")]
		McpValueType? valueType = null,
		[Description(
			"For valueType bytes the byte count (required), for string and wstring the maximum length; 1 to 65536.")]
		int? length = null,
		CancellationToken cancellationToken = default)
	{
		string root = PointerSupport.Expression(@base, "base");
		if (offsets is null || offsets.Length == 0)
		{
			throw CheatEngineToolException.InvalidArgument("offsets", string.Create(CultureInfo.InvariantCulture,
				$"must contain 1 to {MaximumOffsets} offsets; a chain dereferences at least once."));
		}

		long[] parsed = HexParse.Offsets(offsets, "offsets", MaximumOffsets);
		if (length is { } requested)
		{
			PointerSupport.Range(requested, 1, McpValueCodec.MaxLength, "length");
		}
		else if (valueType is McpValueType.Bytes)
		{
			throw CheatEngineToolException.InvalidArgument("length", "is required for the bytes value type.");
		}

		return _dispatch.Run(CheatEngineToolNames.PointerReadChain, token =>
		{
			ICheatEngineClient client = _dispatch.Client;
			int width = PointerSupport.Width(client.Processes.GetCurrentProcess(token));
			ulong baseAddress = PointerSupport.Resolve(client, root, token);
			ulong current = baseAddress;
			PointerHop[] hops = new PointerHop[parsed.Length];
			for (int index = 0; index < parsed.Length; index++)
			{
				if (!client.Memory.TryReadPrimitive(new Address(current), out Address pointer,
						out CheatEngineFailure failure, token))
				{
					throw failure.Kind is CheatEngineFailureKind.MemoryReadFailed
						? Unreadable(index, current,
							$"Hop {index}: the pointer at {HexFormat.Address(current)} could not be read.",
							failure.Operation, ToolFailureMapping.MapHostEffect(failure.HostEffect))
						: CheatEngineToolException.FromFailure(failure, client.Stopping.IsCancellationRequested);
				}

				if (!PointerMap.TryAdd(pointer.ToUInt64(), parsed[index], width, out ulong next))
				{
					throw Unreadable(index, current,
						$"Hop {index}: pointer {HexFormat.Address(pointer)} plus offset {HexFormat.Offset(parsed[index])} leaves the address space.",
						null, ToolHostEffect.NotStarted);
				}

				hops[index] = new PointerHop(HexFormat.Address(current), HexFormat.Address(pointer),
					HexFormat.Offset(parsed[index]), HexFormat.Address(next));
				current = next;
			}

			string? value = valueType is { } type
				? ReadValue(client, current, type, length, parsed.Length, token)
				: null;
			return new PointerChainResult(PointerSupport.ChainExpression(root, parsed),
				HexFormat.Address(baseAddress), hops, HexFormat.Address(current), value);
		}, cancellationToken);
	}

	private static string ReadValue(ICheatEngineClient client, ulong address, McpValueType type, int? length,
		int hopIndex, CancellationToken cancellationToken)
	{
		try
		{
			return McpValueCodec.Read(client, new Address(address), type, length, cancellationToken);
		}
		catch (CheatEngineClientException exception) when (exception.Failure.Kind is
															   CheatEngineFailureKind.MemoryReadFailed)
		{
			throw Unreadable(hopIndex, address,
				$"The value at the final address {HexFormat.Address(address)} could not be read.",
				exception.Failure.Operation, ToolFailureMapping.MapHostEffect(exception.Failure.HostEffect));
		}
	}

	private static CheatEngineToolException Unreadable(int hopIndex, ulong readAt, string message, string? operation,
		ToolHostEffect effect)
	{
		JsonElement details = JsonSerializer.SerializeToElement(
			new PointerHopFailure(hopIndex, HexFormat.Address(readAt)), PointerJsonContext.Default.PointerHopFailure);
		return new CheatEngineToolException(new ToolError(ToolErrorKind.MemoryReadFailed, message,
			string.IsNullOrEmpty(operation) ? null : operation, effect, false, UnreadableHint, details));
	}
}
