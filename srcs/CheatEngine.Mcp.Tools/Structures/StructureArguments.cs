using System.Globalization;

using CheatEngine.Client;
using CheatEngine.Client.Inspection;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Mcp.Tools.Structures;

/// <summary>
///     Validates the structure tools' arguments before anything is dispatched, and encodes element changes as the
///     positional tables the fixed scripts read.
/// </summary>
internal static class StructureArguments
{
	internal const int MaxNameLength = 256;
	internal const int MaxExpressionLength = 1024;

	/// <summary>Validates a structure or type name: 1 to 256 characters, not only whitespace.</summary>
	internal static string Name(string? value, string parameter)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			throw CheatEngineToolException.InvalidArgument(parameter, "must contain a name.");
		}

		return value.Length <= MaxNameLength
			? value
			: throw CheatEngineToolException.LimitExceeded(parameter, $"accepts at most {MaxNameLength} characters.");
	}

	/// <summary>Validates an element name: at most 256 characters, possibly empty.</summary>
	internal static string ElementName(string? value, string parameter)
	{
		if (value is null)
		{
			throw CheatEngineToolException.InvalidArgument(parameter, "is required; pass an empty string for no name.");
		}

		return value.Length <= MaxNameLength
			? value
			: throw CheatEngineToolException.LimitExceeded(parameter, $"accepts at most {MaxNameLength} characters.");
	}

	/// <summary>Validates a Cheat Engine address expression.</summary>
	internal static string Expression(string? value, string parameter)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			throw CheatEngineToolException.InvalidArgument(parameter,
				"must be an address or symbol expression, such as 7FF6A1B2C3D0 or game.exe+1C.");
		}

		return value.Length <= MaxExpressionLength
			? value.Trim()
			: throw CheatEngineToolException.LimitExceeded(parameter,
				$"accepts at most {MaxExpressionLength} characters.");
	}

	/// <summary>Validates a list of address expressions.</summary>
	internal static string[] Expressions(string[]? values, string parameter, int minimum, int maximum)
	{
		values ??= [];
		if (values.Length < minimum)
		{
			throw CheatEngineToolException.InvalidArgument(parameter,
				$"must contain at least {minimum} address{(minimum == 1 ? string.Empty : "es")}.");
		}

		if (values.Length > maximum)
		{
			throw CheatEngineToolException.LimitExceeded(parameter, $"accepts at most {maximum} addresses.");
		}

		string[] expressions = new string[values.Length];
		for (int index = 0; index < values.Length; index++)
		{
			expressions[index] = Expression(values[index], Item(parameter, index));
		}

		return expressions;
	}

	/// <summary>Parses a signed hexadecimal offset that fits Cheat Engine's 32-bit element offsets.</summary>
	internal static int Offset(string? text, string parameter)
	{
		long offset = HexParse.Offset(text, parameter);
		return offset is >= int.MinValue and <= int.MaxValue
			? (int) offset
			: throw CheatEngineToolException.InvalidArgument(parameter,
				"must fit a 32-bit signed offset, -80000000 to 7FFFFFFF.");
	}

	/// <summary>Validates <c>offset</c> and <c>limit</c> with the contract's paging rules.</summary>
	internal static void Page(int offset, int limit, int maximum)
	{
		Paging.Slice<byte>([], offset, limit, maximum);
	}

	/// <summary>Validates a batch's size.</summary>
	internal static T[] Batch<T>(T[]? items, string parameter, int minimum, int maximum)
	{
		items ??= [];
		if (items.Length < minimum)
		{
			throw CheatEngineToolException.InvalidArgument(parameter,
				$"must contain at least {minimum} item{(minimum == 1 ? string.Empty : "s")}.");
		}

		return items.Length <= maximum
			? items
			: throw CheatEngineToolException.LimitExceeded(parameter, $"accepts at most {maximum} items.");
	}

	/// <summary>
	///     Validates element specifications and encodes each as
	///     <c>{offset, name, vartype, displayMethod, byteSize, childName, childStart}</c>.
	/// </summary>
	internal static object?[][] Specs(StructureElementSpec[] specs, string parameter)
	{
		object?[][] encoded = new object?[specs.Length][];
		for (int index = 0; index < specs.Length; index++)
		{
			string item = Item(parameter, index);
			StructureElementSpec spec = specs[index] ??
										throw CheatEngineToolException.InvalidArgument(item, "must be an element.");
			int offset = Offset(spec.Offset, item + ".offset");
			string name = ElementName(spec.Name, item + ".name");
			McpValueType type = Defined(spec.ValueType, item + ".valueType");
			string? display = StructureValueCodec.DisplayMethodFor(type, spec.Display, item + ".display");
			int? byteSize = ByteSize(type, spec.ByteSize, item + ".byteSize");
			string? child = null;
			int? childStart = null;
			if (spec.ChildStructure is not null)
			{
				if (type is not McpValueType.Pointer)
				{
					throw CheatEngineToolException.InvalidArgument(item + ".childStructure",
						"applies only to pointer elements.");
				}

				child = Name(spec.ChildStructure, item + ".childStructure");
				childStart = spec.ChildStructureStart is null
					? null
					: Offset(spec.ChildStructureStart, item + ".childStructureStart");
			}
			else if (spec.ChildStructureStart is not null)
			{
				throw CheatEngineToolException.InvalidArgument(item + ".childStructureStart",
					"applies only together with childStructure.");
			}

			encoded[index] =
				[offset, name, StructureValueCodec.Vartype(type), display, byteSize, child, childStart];
		}

		return encoded;
	}

	/// <summary>
	///     Validates element updates and encodes each as <c>{index, offset, name, vartype, displayMethod, byteSize}</c>.
	/// </summary>
	internal static object?[][] Updates(StructureElementUpdate[] updates, string parameter)
	{
		object?[][] encoded = new object?[updates.Length][];
		for (int index = 0; index < updates.Length; index++)
		{
			string item = Item(parameter, index);
			StructureElementUpdate update = updates[index] ??
											throw CheatEngineToolException.InvalidArgument(item, "must be an update.");
			if (update.Index < 0)
			{
				throw CheatEngineToolException.InvalidArgument(item + ".index", "must be zero or greater.");
			}

			if (update is { Offset: null, Name: null, ValueType: null, Display: null, ByteSize: null })
			{
				throw CheatEngineToolException.InvalidArgument(item,
					"changes nothing; set at least one of offset, name, valueType, display and byteSize.");
			}

			int? offset = update.Offset is null ? null : Offset(update.Offset, item + ".offset");
			string? name = update.Name is null ? null : ElementName(update.Name, item + ".name");
			int? vartype = null;
			string? display;
			int? byteSize;
			if (update.ValueType is { } requested)
			{
				McpValueType type = Defined(requested, item + ".valueType");
				vartype = StructureValueCodec.Vartype(type);
				display = StructureValueCodec.DisplayMethodFor(type, update.Display, item + ".display");
				byteSize = ByteSize(type, update.ByteSize, item + ".byteSize");
			}
			else
			{
				// Without a new type, the script checks the display and byte size against the element's current type.
				display = update.Display is { } shown ? StructureValueCodec.DisplayMethod(Defined(shown, item + ".display")) : null;
				byteSize = update.ByteSize is { } size ? SizeInRange(size, item + ".byteSize") : null;
			}

			encoded[index] = [update.Index, offset, name, vartype, display, byteSize];
		}

		return encoded;
	}

	/// <summary>Validates element indices and returns them distinct, highest first.</summary>
	internal static int[] Indices(int[] indices, string parameter)
	{
		HashSet<int> seen = [];
		for (int index = 0; index < indices.Length; index++)
		{
			if (indices[index] < 0)
			{
				throw CheatEngineToolException.InvalidArgument(Item(parameter, index), "must be zero or greater.");
			}

			if (!seen.Add(indices[index]))
			{
				throw CheatEngineToolException.InvalidArgument(Item(parameter, index),
					$"repeats element {indices[index].ToString(CultureInfo.InvariantCulture)}.");
			}
		}

		int[] ordered = [.. indices];
		Array.Sort(ordered);
		Array.Reverse(ordered);
		return ordered;
	}

	/// <summary>Resolves an address expression inside the current dispatch.</summary>
	/// <exception cref="CheatEngineToolException"><c>not_found</c> when Cheat Engine cannot resolve it.</exception>
	internal static ulong Resolve(ICheatEngineClient client, string expression, string parameter,
		CancellationToken cancellationToken)
	{
		if (client.Inspection.TryResolveAddress(new SymbolExpression(expression), AddressResolutionMode.Default,
				out Address address, out CheatEngineFailure failure, cancellationToken))
		{
			return address.ToUInt64();
		}

		if (failure.Kind is CheatEngineFailureKind.NotFound)
		{
			throw CheatEngineToolException.NotFound($"{parameter}: '{expression}' does not resolve to an address.",
				"Check the symbol or module name, or pass a hexadecimal address such as 7FF6A1B2C3D0.");
		}

		throw CheatEngineToolException.FromFailure(failure, client.Stopping.IsCancellationRequested);
	}

	/// <summary>Adds a signed offset to an address, or returns <see langword="null" /> when it leaves the address space.</summary>
	internal static ulong? Add(ulong address, long offset)
	{
		if (offset >= 0)
		{
			ulong forward = address + (ulong) offset;
			return forward >= address ? forward : null;
		}

		ulong backward = unchecked((ulong) -(offset + 1)) + 1;
		return address >= backward ? address - backward : null;
	}

	internal static string Item(string parameter, int index)
	{
		return $"{parameter}[{index.ToString(CultureInfo.InvariantCulture)}]";
	}

	private static int? ByteSize(McpValueType type, int? byteSize, string parameter)
	{
		if (StructureValueCodec.IsSized(type))
		{
			return byteSize is { } size
				? SizeInRange(size, parameter)
				: throw CheatEngineToolException.InvalidArgument(parameter,
					"is required for string, wstring and bytes elements.");
		}

		return byteSize is null
			? null
			: throw CheatEngineToolException.InvalidArgument(parameter,
				"applies only to string, wstring and bytes elements; other types have a fixed size.");
	}

	private static int SizeInRange(int size, string parameter)
	{
		if (size < 1)
		{
			throw CheatEngineToolException.InvalidArgument(parameter, "must be at least 1.");
		}

		return size <= StructureValueCodec.MaxByteSize
			? size
			: throw CheatEngineToolException.LimitExceeded(parameter,
				$"must be at most {StructureValueCodec.MaxByteSize}.");
	}

	private static TEnum Defined<TEnum>(TEnum value, string parameter) where TEnum : struct, Enum
	{
		return Enum.IsDefined(value)
			? value
			: throw CheatEngineToolException.InvalidArgument(parameter, "is not a supported value.");
	}
}
