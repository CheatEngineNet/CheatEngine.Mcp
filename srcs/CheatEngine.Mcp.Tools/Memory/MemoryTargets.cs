using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Globalization;

using CheatEngine.Client;
using CheatEngine.Client.Inspection;
using CheatEngine.Client.Processes;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Mcp.Tools.Memory;

/// <summary>
///     Shared target helpers of the memory and AOB tools: argument checks that run before any dispatch, address
///     resolution and target facts that run inside one, value decoding and the contract forms of region facts.
/// </summary>
internal static class MemoryTargets
{
	/// <summary>The longest accepted address expression.</summary>
	internal const int MaximumExpressionLength = 4096;

	/// <summary>The longest accepted module name.</summary>
	internal const int MaximumModuleNameLength = 256;

	/// <summary>The most modules copied to find the one containing an address.</summary>
	internal const int MaximumModules = 4096;

	/// <summary>The bytes one chunked dispatch reads.</summary>
	internal const int ChunkBytes = 1 << 20;

	private const string ResolveHint = "Check the expression with symbol_resolve, or pass a hexadecimal address.";

	private const string TargetChangedHint =
		"Cheat Engine selected another process during the call; check process_get_current and repeat the reads.";

	/// <summary>Checks an address expression before any dispatch.</summary>
	/// <param name="expression">The caller's expression.</param>
	/// <param name="parameter">The parameter that carried it.</param>
	/// <returns>The expression, trimmed.</returns>
	internal static string RequireExpression(string? expression, string parameter)
	{
		if (string.IsNullOrWhiteSpace(expression))
		{
			throw CheatEngineToolException.InvalidArgument(parameter,
				"must be an address or Cheat Engine address expression, such as game.exe+1C or 7FF6A1B2C3D0.");
		}

		string trimmed = expression.Trim();
		return trimmed.Length <= MaximumExpressionLength
			? trimmed
			: throw CheatEngineToolException.LimitExceeded(parameter,
				$"must be at most {MaximumExpressionLength} characters.");
	}

	/// <summary>Checks an optional module name before any dispatch.</summary>
	/// <param name="module">The caller's module name.</param>
	/// <param name="parameter">The parameter that carried it.</param>
	/// <returns>The trimmed name, or <see langword="null" /> when none was given.</returns>
	internal static string? OptionalModule(string? module, string parameter = "module")
	{
		if (module is null)
		{
			return null;
		}

		if (string.IsNullOrWhiteSpace(module))
		{
			throw CheatEngineToolException.InvalidArgument(parameter, "must be a loaded module name such as game.exe.");
		}

		string trimmed = module.Trim();
		return trimmed.Length <= MaximumModuleNameLength
			? trimmed
			: throw CheatEngineToolException.LimitExceeded(parameter,
				$"must be at most {MaximumModuleNameLength} characters.");
	}

	/// <summary>Checks that a value type is one of the contract's, before any dispatch.</summary>
	/// <param name="type">The caller's value type.</param>
	/// <param name="parameter">The parameter that carried it.</param>
	internal static void RequireType(McpValueType type, string parameter)
	{
		if (!Enum.IsDefined(type))
		{
			throw CheatEngineToolException.InvalidArgument(parameter, "is not a value type.");
		}
	}

	/// <summary>Checks an inclusive integer range before any dispatch.</summary>
	/// <param name="value">The caller's value.</param>
	/// <param name="parameter">The parameter that carried it.</param>
	/// <param name="minimum">The smallest accepted value.</param>
	/// <param name="maximum">The largest accepted value; larger values are a limit, not a format error.</param>
	/// <returns>The value.</returns>
	internal static long RequireRange(long value, string parameter, long minimum, long maximum)
	{
		if (value < minimum)
		{
			throw CheatEngineToolException.InvalidArgument(parameter,
				$"must be at least {minimum.ToString(CultureInfo.InvariantCulture)}.");
		}

		return value <= maximum
			? value
			: throw CheatEngineToolException.LimitExceeded(parameter,
				$"must be at most {maximum.ToString(CultureInfo.InvariantCulture)}.");
	}

	/// <summary>Resolves an address expression inside the current dispatch.</summary>
	/// <param name="client">The activation's Client.</param>
	/// <param name="expression">A checked expression.</param>
	/// <param name="parameter">The parameter that carried it, named when it does not resolve.</param>
	/// <param name="cancellationToken">The token of the enclosing dispatch body.</param>
	/// <returns>The address.</returns>
	internal static Address Resolve(ICheatEngineClient client, string expression, string parameter,
		CancellationToken cancellationToken)
	{
		return TryResolve(client, expression, out Address address, out CheatEngineFailure failure,
			cancellationToken)
			? address
			: throw ResolveFailure(client, failure, expression, parameter);
	}

	/// <summary>Resolves an address expression inside the current dispatch, reporting a failure instead of throwing.</summary>
	/// <param name="client">The activation's Client.</param>
	/// <param name="expression">A checked expression.</param>
	/// <param name="address">The address on success.</param>
	/// <param name="failure">The Client failure otherwise.</param>
	/// <param name="cancellationToken">The token of the enclosing dispatch body.</param>
	/// <returns><see langword="true" /> when the expression resolved.</returns>
	internal static bool TryResolve(ICheatEngineClient client, string expression, out Address address,
		out CheatEngineFailure failure, CancellationToken cancellationToken)
	{
		return client.Inspection.TryResolveAddress(new SymbolExpression(expression), AddressResolutionMode.Default,
			out address, out failure, cancellationToken);
	}

	/// <summary>The contract error for an expression that did not resolve.</summary>
	/// <param name="client">The activation's Client.</param>
	/// <param name="failure">The resolution failure.</param>
	/// <param name="expression">The expression.</param>
	/// <param name="parameter">The parameter that carried it.</param>
	/// <returns>The exception to throw.</returns>
	internal static CheatEngineToolException ResolveFailure(ICheatEngineClient client, CheatEngineFailure failure,
		string expression, string parameter)
	{
		return failure.Kind is CheatEngineFailureKind.NotFound
			? CheatEngineToolException.NotFound($"{parameter}: '{expression}' does not resolve to an address.",
				ResolveHint)
			: CheatEngineToolException.FromFailure(failure, client.Stopping.IsCancellationRequested);
	}

	/// <summary>
	///     Checks that changing the requested range's protection can return one previous access that restores the whole
	///     range.
	/// </summary>
	/// <param name="client">The activation's Client.</param>
	/// <param name="address">The resolved start address.</param>
	/// <param name="size">The checked, positive range size.</param>
	/// <param name="cancellationToken">The token of the enclosing dispatch body.</param>
	internal static void RequireCommittedProtectionRange(ICheatEngineClient client, Address address, long size,
		CancellationToken cancellationToken)
	{
		if (!client.Inspection.TryGetMemoryRegion(address, out MemoryRegionInfo region, out CheatEngineFailure failure,
				cancellationToken))
		{
			throw failure.Kind is CheatEngineFailureKind.NotFound
				? CheatEngineToolException.NotFound(
					"Cheat Engine cannot identify the memory region containing address, so its previous protection cannot be restored safely.",
					"Choose an address reported by memory_get_address_info.")
				: CheatEngineToolException.FromFailure(failure, client.Stopping.IsCancellationRequested);
		}

		if (region.State is not MemoryRegionState.Committed)
		{
			throw CheatEngineToolException.InvalidState(
				"The requested range is not committed memory, so its previous protection cannot be restored safely.",
				"Choose a range within one committed memory region.");
		}

		ulong target = address.ToUInt64();
		ulong start = region.BaseAddress.ToUInt64();
		ulong length = region.Size.Value;
		if (length == 0 || target < start || target - start >= length)
		{
			throw CheatEngineToolException.InvalidState(
				"Cheat Engine returned a memory region that does not contain address, so its previous protection cannot be restored safely.",
				"Inspect the address with memory_get_address_info and try again.");
		}

		ulong remaining = length - (target - start);
		if ((ulong) size > remaining)
		{
			throw CheatEngineToolException.InvalidArgument("size",
				"must fit entirely within the committed memory region containing address, so the previous protection can restore the range.");
		}
	}

	/// <summary>Whether a per-item failure stays in band, or ends the whole call.</summary>
	/// <param name="failure">The failure.</param>
	/// <returns><see langword="true" /> for a failure that concerns only its item.</returns>
	internal static bool IsItemFailure(CheatEngineFailure failure)
	{
		return failure.Kind is CheatEngineFailureKind.NotFound or CheatEngineFailureKind.MemoryReadFailed
			or CheatEngineFailureKind.AmbiguousMatch;
	}

	/// <summary>The in-band form of an item failure.</summary>
	/// <param name="client">The activation's Client.</param>
	/// <param name="failure">The failure.</param>
	/// <returns>The item error.</returns>
	internal static MemoryItemError ItemError(ICheatEngineClient client, CheatEngineFailure failure)
	{
		ToolError error = ToolFailureMapping.Map(failure, client.Stopping.IsCancellationRequested);
		return new MemoryItemError(error.Kind, error.Message);
	}

	/// <summary>The size in bytes of the selected target's pointers, inside the current dispatch.</summary>
	/// <param name="client">The activation's Client.</param>
	/// <param name="cancellationToken">The token of the enclosing dispatch body.</param>
	/// <returns>4 or 8.</returns>
	internal static int PointerBytes(ICheatEngineClient client, CancellationToken cancellationToken)
	{
		ProcessSnapshot process = client.Processes.GetCurrentProcess(cancellationToken);
		return process.Bitness.IsKnown
			? process.Bitness.Bytes
			: throw CheatEngineToolException.Unsupported(
				"Cheat Engine does not report the target's pointer size, so pointer values cannot be sized.");
	}

	/// <summary>The target-selection epoch, inside the current dispatch.</summary>
	/// <param name="client">The activation's Client.</param>
	/// <param name="cancellationToken">The token of the enclosing dispatch body.</param>
	/// <returns>The epoch; a different value means Cheat Engine selected another target.</returns>
	internal static long SelectionEpoch(ICheatEngineClient client, CancellationToken cancellationToken)
	{
		return client.Processes.GetCurrentProcess(cancellationToken).SelectionEpoch;
	}

	/// <summary>Refuses a later chunk of a multi-dispatch operation once Cheat Engine selected another target.</summary>
	/// <param name="client">The activation's Client.</param>
	/// <param name="expected">The epoch observed by the first chunk.</param>
	/// <param name="operation">The tool name.</param>
	/// <param name="cancellationToken">The token of the enclosing dispatch body.</param>
	internal static void RequireSameTarget(ICheatEngineClient client, long expected, string operation,
		CancellationToken cancellationToken)
	{
		if (SelectionEpoch(client, cancellationToken) != expected)
		{
			throw new CheatEngineToolException(new ToolError(ToolErrorKind.TargetChanged,
				"Cheat Engine selected another target between two chunks of this call; its result was discarded.",
				operation, ToolHostEffect.NotApplied, false, TargetChangedHint));
		}
	}

	/// <summary>The loaded module that contains <paramref name="address" />, when one does and its size is known.</summary>
	/// <param name="modules">The copied modules.</param>
	/// <param name="address">The address.</param>
	/// <returns>The module, or <see langword="null" />.</returns>
	internal static ModuleInfo? ContainingModule(ImmutableArray<ModuleInfo> modules, Address address)
	{
		foreach (ModuleInfo module in modules)
		{
			if (Contains(module, address))
			{
				return module;
			}
		}

		return null;
	}

	/// <summary>Whether a module with a known size contains an address.</summary>
	/// <param name="module">The module.</param>
	/// <param name="address">The address.</param>
	/// <returns><see langword="true" /> when the address is inside the module's image.</returns>
	internal static bool Contains(ModuleInfo module, Address address)
	{
		ulong start = module.BaseAddress.ToUInt64();
		ulong value = address.ToUInt64();
		return module.ImageSize is { } size && value >= start && value - start < size.Value;
	}

	/// <summary>Finds a loaded module by name, ignoring case.</summary>
	/// <param name="modules">The copied modules.</param>
	/// <param name="name">The module name.</param>
	/// <returns>The module, or <see langword="null" />.</returns>
	internal static ModuleInfo? FindModule(ImmutableArray<ModuleInfo> modules, string name)
	{
		foreach (ModuleInfo module in modules)
		{
			if (string.Equals(module.Name, name, StringComparison.OrdinalIgnoreCase))
			{
				return module;
			}
		}

		return null;
	}

	/// <summary>Decodes one little-endian fixed-size value as contract text.</summary>
	/// <param name="type">A fixed-size value type.</param>
	/// <param name="bytes">At least the value's size in bytes.</param>
	/// <param name="pointerBytes">The target pointer size, 4 or 8.</param>
	/// <returns>The value text.</returns>
	internal static string Decode(McpValueType type, ReadOnlySpan<byte> bytes, int pointerBytes)
	{
		return type switch
		{
			McpValueType.Int8 => McpValueCodec.Format(unchecked((sbyte) bytes[0])),
			McpValueType.UInt8 => McpValueCodec.Format(bytes[0]),
			McpValueType.Int16 => McpValueCodec.Format(BinaryPrimitives.ReadInt16LittleEndian(bytes)),
			McpValueType.UInt16 => McpValueCodec.Format(BinaryPrimitives.ReadUInt16LittleEndian(bytes)),
			McpValueType.Int32 => McpValueCodec.Format(BinaryPrimitives.ReadInt32LittleEndian(bytes)),
			McpValueType.UInt32 => McpValueCodec.Format(BinaryPrimitives.ReadUInt32LittleEndian(bytes)),
			McpValueType.Int64 => McpValueCodec.Format(BinaryPrimitives.ReadInt64LittleEndian(bytes)),
			McpValueType.UInt64 => McpValueCodec.Format(BinaryPrimitives.ReadUInt64LittleEndian(bytes)),
			McpValueType.Float => McpValueCodec.Format(BinaryPrimitives.ReadSingleLittleEndian(bytes)),
			McpValueType.Double => McpValueCodec.Format(BinaryPrimitives.ReadDoubleLittleEndian(bytes)),
			McpValueType.Pointer => HexFormat.Address(pointerBytes == 4
				? BinaryPrimitives.ReadUInt32LittleEndian(bytes)
				: BinaryPrimitives.ReadUInt64LittleEndian(bytes)),
			_ => throw new ArgumentOutOfRangeException(nameof(type), type, "Not a fixed-size value type.")
		};
	}

	/// <summary>The access a Windows page protection grants.</summary>
	/// <param name="protection">The <c>PAGE_*</c> value.</param>
	/// <returns>The access; modifier bits such as <c>PAGE_GUARD</c> are ignored.</returns>
	internal static MemoryAccess Access(MemoryProtection protection)
	{
		uint access = (uint) protection & 0xFF;
		bool copyOnWrite = access is 0x08 or 0x80;
		bool write = copyOnWrite || access is 0x04 or 0x40;
		bool execute = access is 0x10 or 0x20 or 0x40 or 0x80;
		bool read = access is 0x02 or 0x04 or 0x08 or 0x20 or 0x40 or 0x80;
		return new MemoryAccess(read, write, execute, copyOnWrite);
	}

	/// <summary>The contract form of a region state.</summary>
	/// <param name="state">The Windows <c>MEM_*</c> state.</param>
	/// <returns>The state.</returns>
	internal static RegionState State(MemoryRegionState state)
	{
		return state switch
		{
			MemoryRegionState.Committed => RegionState.Committed,
			MemoryRegionState.Reserved => RegionState.Reserved,
			MemoryRegionState.Free => RegionState.Free,
			_ => RegionState.Unknown
		};
	}

	/// <summary>The contract form of a region backing.</summary>
	/// <param name="type">The Windows <c>MEM_*</c> type.</param>
	/// <returns>The backing.</returns>
	internal static RegionType Type(MemoryRegionType type)
	{
		return type switch
		{
			MemoryRegionType.Image => RegionType.Image,
			MemoryRegionType.Mapped => RegionType.Mapped,
			MemoryRegionType.Private => RegionType.Private,
			0 => RegionType.None,
			_ => RegionType.Unknown
		};
	}

	/// <summary>The region detail of <c>memory_get_address_info</c>.</summary>
	/// <param name="region">The copied region.</param>
	/// <returns>The detail.</returns>
	internal static MemoryRegionDetail Detail(MemoryRegionInfo region)
	{
		return new MemoryRegionDetail(HexFormat.Address(region.BaseAddress), Size(region.Size),
			State(region.State), Access(region.Protection), Type(region.Type),
			HexFormat.Address(region.AllocationBase));
	}

	/// <summary>A region size as the contract's signed byte count.</summary>
	/// <param name="size">The size.</param>
	/// <returns>The size, saturated at <see cref="long.MaxValue" />.</returns>
	internal static long Size(MemorySize size)
	{
		return size.Value > long.MaxValue ? long.MaxValue : (long) size.Value;
	}
}
