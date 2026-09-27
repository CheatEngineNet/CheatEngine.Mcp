using System.ComponentModel;
using System.Globalization;

using CheatEngine.Client;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.SDK.Engine.Values;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools.Structures;

/// <summary>Reads structure values at several addresses and writes one element through the typed memory API.</summary>
[McpServerToolType]
public sealed class StructureValueTools
{
	internal const int MaxAddresses = 16;
	internal const int MaxReadLimit = 256;
	internal const int MaxSpan = 65536;
	internal const int MaxValueLength = 262144;

	private readonly ToolDispatch _dispatch;

	/// <summary>Creates the tools; nothing is dispatched until a call.</summary>
	/// <param name="dispatch">The activation's dispatch facade.</param>
	public StructureValueTools(ToolDispatch dispatch)
	{
		ArgumentNullException.ThrowIfNull(dispatch);
		_dispatch = dispatch;
	}

	/// <summary>Reads structure values at up to 16 base addresses.</summary>
	[McpServerTool(Name = CheatEngineToolNames.StructureRead, Title = "Read structure values", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Reads a page of a structure's elements at up to 16 base addresses, like Cheat Engine's dissect window: one " +
		"column per address and one row per element, values in the contract format of the element's valueType, null " +
		"where unreadable. fromOffset and toOffset keep only elements in that offset range; one page spans at most " +
		"65536 bytes.")]
	public StructureReadResult Read(
		[Description("The structure's case-sensitive name.")]
		string name,
		[Description("The base addresses or symbol expressions (1-16), one column each.")]
		string[] addresses,
		[Description("Only elements at or after this offset, as signed hexadecimal.")]
		string? fromOffset = null,
		[Description("Only elements at or before this offset, as signed hexadecimal.")]
		string? toOffset = null,
		[Description("The zero-based index of the first element of the range to return.")]
		int offset = 0,
		[Description("The most elements to return (1-256).")]
		int limit = 100,
		[Description("concise, or detailed to add each element's byteSize, display and raw bytes.")]
		ResultFormat format = ResultFormat.Concise,
		CancellationToken cancellationToken = default)
	{
		string structure = StructureArguments.Name(name, "name");
		string[] expressions = StructureArguments.Expressions(addresses, "addresses", 1, MaxAddresses);
		int? lowest = fromOffset is null ? null : StructureArguments.Offset(fromOffset, "fromOffset");
		int? highest = toOffset is null ? null : StructureArguments.Offset(toOffset, "toOffset");
		if (lowest > highest)
		{
			throw CheatEngineToolException.InvalidArgument("toOffset", "must not be below fromOffset.");
		}

		StructureArguments.Page(offset, limit, MaxReadLimit);
		bool detailed = format is ResultFormat.Detailed;
		return _dispatch.Run(CheatEngineToolNames.StructureRead, token =>
		{
			ICheatEngineClient client = _dispatch.Client;
			StructureLuaDefinition page = _dispatch.ExecuteLua(CheatEngineToolNames.StructureRead,
				StructureLuaScripts.Elements, StructureLuaJsonContext.Default.StructureLuaDefinition, token, structure,
				lowest, highest, offset, limit, false);
			ulong[] bases = new ulong[expressions.Length];
			for (int index = 0; index < bases.Length; index++)
			{
				bases[index] = StructureArguments.Resolve(client, expressions[index],
					StructureArguments.Item("addresses", index), token);
			}

			(long start, int span) = Span(page.Elements);
			byte[][] columns = new byte[bases.Length][];
			for (int index = 0; index < bases.Length; index++)
			{
				columns[index] = ReadSpan(client, bases[index], start, span, token);
			}

			string?[][]? formatted = null;
			int[] formattedElements =
			[
				.. page.Elements.Where(static element => !StructureValueCodec.IsManaged(
						StructureValueCodec.ElementType(element.Vartype, element.Display)))
					.Select(static element => element.Index)
			];
			if (formattedElements.Length > 0)
			{
				formatted = _dispatch.ExecuteLua(CheatEngineToolNames.StructureRead,
					StructureLuaScripts.FormattedValues, StructureLuaJsonContext.Default.StructureLuaValues, token,
					page.Name, formattedElements, bases).Values;
			}

			StructureReadRow[] rows = new StructureReadRow[page.Elements.Length];
			int next = 0;
			for (int row = 0; row < rows.Length; row++)
			{
				StructureLuaElement element = page.Elements[row];
				StructureElementType type = StructureValueCodec.ElementType(element.Vartype, element.Display);
				bool managed = StructureValueCodec.IsManaged(type);
				string?[]? lua = managed ? null : formatted![next++];
				string?[] values = new string?[columns.Length];
				string?[]? raw = detailed ? new string?[columns.Length] : null;
				for (int column = 0; column < columns.Length; column++)
				{
					bool readable = TryElement(columns[column], element, start, out ReadOnlySpan<byte> bytes);
					values[column] = managed
						? readable ? StructureValueCodec.Decode(type, bytes) : null
						: column < lua!.Length
							? lua[column]
							: null;
					if (raw is not null && readable)
					{
						raw[column] = HexFormat.Bytes(bytes);
					}
				}

				rows[row] = new StructureReadRow(element.Index, HexFormat.Offset(element.Offset),
					element.Name ?? string.Empty, type, values, detailed ? element.ByteSize : null,
					detailed ? StructureValueCodec.Display(element.Vartype, element.Display) : null, raw);
			}

			StructureReadColumn[] header = new StructureReadColumn[bases.Length];
			for (int index = 0; index < header.Length; index++)
			{
				header[index] = new StructureReadColumn(HexFormat.Address(bases[index]), columns[index].Length);
			}

			return new StructureReadResult(page.Name, header, rows, page.Total, page.NextOffset);
		}, cancellationToken);
	}

	/// <summary>Writes one structure element and reads it back.</summary>
	[McpServerTool(Name = CheatEngineToolNames.StructureWriteElement, Title = "Write a structure element",
		ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Writes value into the target at address plus the element's offset, encoded as the element's valueType " +
		"(integers in decimal or 0x hexadecimal, pointers in hexadecimal, bytes as 48 8B 05, text with a terminator " +
		"when it fits), then reads it back. Bit fields and custom types use Cheat Engine's own text format. It is a " +
		"mutation: after an error, check hostEffect before trying again.")]
	public StructureWriteResult WriteElement(
		[Description("The structure's case-sensitive name.")]
		string name,
		[Description("The structure instance's base address or symbol expression.")]
		string address,
		[Description("The element's zero-based index, from structure_get.")]
		int index,
		[Description("The value to write, in the format of the element's valueType.")]
		string value,
		CancellationToken cancellationToken = default)
	{
		string structure = StructureArguments.Name(name, "name");
		string expression = StructureArguments.Expression(address, "address");
		if (index < 0)
		{
			throw CheatEngineToolException.InvalidArgument("index", "must be zero or greater.");
		}

		if (value is null)
		{
			throw CheatEngineToolException.InvalidArgument("value", "is required.");
		}

		if (value.Length > MaxValueLength)
		{
			throw CheatEngineToolException.LimitExceeded("value", $"accepts at most {MaxValueLength} characters.");
		}

		return _dispatch.Run(CheatEngineToolNames.StructureWriteElement, token =>
		{
			ICheatEngineClient client = _dispatch.Client;
			StructureLuaElement element = _dispatch.ExecuteLua(CheatEngineToolNames.StructureWriteElement,
				StructureLuaScripts.ElementAt, StructureLuaJsonContext.Default.StructureLuaElementRef, token, structure,
				index).Element;
			ulong resolved = StructureArguments.Resolve(client, expression, "address", token);
			ulong target = StructureArguments.Add(resolved, element.Offset) ??
						   throw CheatEngineToolException.InvalidArgument("address",
							   "plus the element's offset leaves the address space.");
			StructureElementType type = StructureValueCodec.ElementType(element.Vartype, element.Display);
			if (!StructureValueCodec.IsManaged(type))
			{
				StructureLuaWritten written = _dispatch.ExecuteLua(CheatEngineToolNames.StructureWriteElement,
					StructureLuaScripts.SetValue, StructureLuaJsonContext.Default.StructureLuaWritten, token, structure,
					index, resolved, value);
				return new StructureWriteResult(HexFormat.Address(target), element.ByteSize, written.Value);
			}

			byte[] bytes = StructureValueCodec.Encode(type, value, element.ByteSize);
			client.Memory.WriteBytes(new MemoryBytesWriteRequest(new Address(target), bytes), token);
			// The write happened: from here on only the activation's stopping token may interrupt the call.
			int length = Math.Max(bytes.Length, Math.Max(element.ByteSize, 1));
			MemoryBytesReadOutcome readBack = client.Memory.ReadBytesDetailed(
				new MemoryBytesReadRequest(new Address(target), length), client.Stopping);
			string? current = readBack.IsSuccess ? StructureValueCodec.Decode(type, readBack.Bytes.AsSpan()) : null;
			return new StructureWriteResult(HexFormat.Address(target), bytes.Length, current);
		}, cancellationToken);
	}

	/// <summary>Returns the lowest element offset of a page and the bytes from it to the end of the last element.</summary>
	internal static (long Start, int Span) Span(IReadOnlyList<StructureLuaElement> elements)
	{
		if (elements.Count == 0)
		{
			return (0, 0);
		}

		long start = long.MaxValue, end = long.MinValue;
		foreach (StructureLuaElement element in elements)
		{
			start = Math.Min(start, element.Offset);
			end = Math.Max(end, element.Offset + Math.Max(element.ByteSize, 1));
		}

		long span = end - start;
		return span <= MaxSpan
			? (start, (int) span)
			: throw CheatEngineToolException.LimitExceeded("limit",
				$"the page spans {span.ToString(CultureInfo.InvariantCulture)} bytes; one page spans at most {MaxSpan}. " +
				"Narrow fromOffset and toOffset or lower limit.");
	}

	/// <summary>Reads the confirmed prefix of a span, or nothing when it leaves the address space.</summary>
	internal static byte[] ReadSpan(ICheatEngineClient client, ulong address, long start, int span,
		CancellationToken cancellationToken)
	{
		if (span <= 0 || StructureArguments.Add(address, start) is not { } first ||
			StructureArguments.Add(first, span - 1) is null)
		{
			return [];
		}

		MemoryBytesReadOutcome outcome =
			client.Memory.ReadBytesDetailed(new MemoryBytesReadRequest(new Address(first), span), cancellationToken);
		if (outcome.Failure is { Kind: not CheatEngineFailureKind.MemoryReadFailed } failure)
		{
			throw CheatEngineToolException.FromFailure(failure, client.Stopping.IsCancellationRequested);
		}

		return outcome.Bytes.IsDefault ? [] : [.. outcome.Bytes];
	}

	private static bool TryElement(byte[] column, StructureLuaElement element, long start,
		out ReadOnlySpan<byte> bytes)
	{
		long relative = element.Offset - start;
		if (element.ByteSize > 0 && relative + element.ByteSize <= column.Length)
		{
			bytes = column.AsSpan((int) relative, element.ByteSize);
			return true;
		}

		bytes = default;
		return false;
	}
}
