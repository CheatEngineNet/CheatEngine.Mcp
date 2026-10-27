using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

using CheatEngine.Client.Tables;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.SDK.Engine.Enums;

namespace CheatEngine.Mcp.Tools.Record;

/// <summary>Shared validation and copied-record mapping for the record domain.</summary>
internal static partial class RecordArguments
{
	internal const int MaximumBatch = 256;
	internal const int MaximumListLimit = 1000;
	internal const int MaximumScannedRecords = 4096;
	internal const int MaximumDescriptionLength = 1024;
	internal const int MaximumAddressLength = 1024;
	internal const int MaximumValueLength = 65536;
	internal const int MaximumScriptLength = 1_048_576;

	/// <summary>
	///     The UTF-8 size bound of a script, the largest string a fixed Lua body receives as an argument; checked
	///     before any host change instead of failing when the Lua source is built.
	/// </summary>
	internal const int MaximumScriptBytes = 1_048_576;

	internal const int MaximumOffsets = 128;
	internal const int MaximumHierarchyDepth = 64;

	/// <summary>The largest string length in characters, or byte array length in bytes, a record is given.</summary>
	internal const int MaximumLength = 4096;

	/// <summary>
	///     The value type integers a record accepts as input, for parameter descriptions. Cheat Engine keeps 7, 10 and
	///     12 unchanged on a record, but its value code has no case for them, so they are refused.
	/// </summary>
	internal const string VariableTypes =
		"0 byte, 1 2-byte integer, 2 4-byte integer, 3 8-byte integer, 4 float, 5 double, 6 string, 8 byte array, " +
		"9 binary, 11 Auto Assembler script, 13 custom, 14 group header; 7 (UTF-16 string), 10 (all types) and 12 " +
		"(pointer-sized value) are refused because Cheat Engine can neither read nor write the value of a record " +
		"with one of them; use 6 with unicode true for UTF-16 text";

	/// <summary>
	///     The value type integers a record can carry, for output and search descriptions: Cheat Engine stores the type
	///     it is given, including 7, 10 and 12 when a Lua script or a loaded table gives one.
	/// </summary>
	internal const string StoredVariableTypes =
		"0 byte, 1 2-byte integer, 2 4-byte integer, 3 8-byte integer, 4 float, 5 double, 6 string, " +
		"7 UTF-16 string, 8 byte array, 9 binary, 10 all types, 11 Auto Assembler script, 12 pointer-sized " +
		"hexadecimal value (not a pointer chain), 13 custom, 14 group header; only a record created outside these " +
		"tools, such as by a Lua script or a loaded table, has 7, 10 or 12, and Cheat Engine reads its value as " +
		"empty text";

	/// <summary>The form and bounds of a pointer-offset list, for parameter descriptions.</summary>
	internal const string OffsetBounds =
		"up to 128 signed hexadecimal offsets from -80000000 to 7FFFFFFF such as \"10\", \"-8\" or \"4C8\", with an " +
		"optional + sign or 0x prefix, the form pointer_list_paths and pointer_read_chain use";

	/// <summary>The length option shared by the create and update descriptions.</summary>
	internal const string LengthText =
		"For a string record (6) its length in characters, for a byte array record (8) its number of bytes, from 1 " +
		"to 4096; other types refuse it. A longer written value lengthens the record: Cheat Engine lengthens a " +
		"string record, and the tool sets a byte array record to the number of bytes in the value.";

	/// <summary>The value rules of record_create.</summary>
	internal const string CreateValueText =
		"The value text written once the record is complete, at its final address after parentId and offsets apply, " +
		"up to 65536 characters. Empty, the default, writes nothing and leaves memory unchanged for every type; a " +
		"string or byte array record then needs length. Types 0 to 5 and 13 take a number: decimal such as 100, -5 " +
		"or 1.5, optionally with an unsigned exponent such as 1.5e3, or hexadecimal such as 0x64; other text, " +
		"including a + or - after the first character, is refused because Cheat Engine would evaluate it as a Lua " +
		"expression. A string record (6) takes its text. A byte array record (8) takes hexadecimal byte pairs such " +
		"as 48 8B 05, where ?? leaves a byte unchanged, and refuses Cheat Engine's (description) notation; the " +
		"record is lengthened to the number of bytes in the value. When Cheat Engine refuses a value, the call " +
		"deletes the records it created.";

	/// <summary>The value rules of record_update.</summary>
	internal const string UpdateValueText =
		"The replacement value text, written last at the record's final address, up to 65536 characters; omit it to " +
		"keep the current value. The number and byte array rules of record_create apply to the record's type after " +
		"the update. The number rule also applies to a record of any type whose Cheat Engine option " +
		"moRecursiveSetValue passes the value on to a nested record of type 0 to 5 or 13. An empty value is accepted " +
		"only for a string record (6), where it writes an empty string. A byte array record (8) is switched to " +
		"hexadecimal display and lengthened to the number of bytes in the value before the value is written.";

	/// <summary>
	///     Cheat Engine's record option that makes SetValue write the same text to every child record, which passes it
	///     on to its own children when it has the option too.
	/// </summary>
	internal const string RecursiveSetValueOption = "moRecursiveSetValue";

	/// <summary>The value types whose SetValue evaluates bracketed or arithmetic text as a Lua expression.</summary>
	internal static readonly ImmutableArray<int> NumberTypes =
	[
		(int) VariableType.Byte, (int) VariableType.Word, (int) VariableType.Dword, (int) VariableType.Qword,
		(int) VariableType.Single, (int) VariableType.Double, (int) VariableType.Custom
	];

	/// <summary>Maps a copied record with its copied pointer offsets and, when one was copied, its dropdown.</summary>
	internal static RecordEntry Entry(MemoryRecordSnapshot record, string[]? offsets, RecordDropdown? dropdown)
	{
		return new RecordEntry(record.Id.Value, record.Index, record.Content.Description,
			record.Content.AddressExpression, record.Content.Value, record.Content.VariableType, record.Content.Script,
			record.Content.OffsetCount, offsets,
			record.State.CurrentAddress is { } address ? HexFormat.Address(address) : null,
			record.State.IsActive, record.State.ChildCount, record.State.IsAsyncProcessing, dropdown);
	}

	internal static void Id(int id, string parameter)
	{
		if (id < 0)
		{
			throw CheatEngineToolException.InvalidArgument(parameter, "must be a non-negative record id.");
		}
	}

	internal static void Batch<T>(T[]? items, string parameter)
	{
		if (items is not { Length: > 0 })
		{
			throw CheatEngineToolException.InvalidArgument(parameter, "must contain at least one item.");
		}

		if (items.Length > MaximumBatch)
		{
			throw CheatEngineToolException.LimitExceeded(parameter, $"accepts at most {MaximumBatch} items.");
		}
	}

	internal static void Text(string? value, string parameter, int maximum, bool required = false)
	{
		if (value is null || (required && string.IsNullOrWhiteSpace(value)) || value.Length > maximum ||
			value.Any(char.IsControl))
		{
			string requirement = required ? "must be non-empty" : "must be supplied";
			throw CheatEngineToolException.InvalidArgument(parameter,
				$"{requirement}, at most {maximum} characters, and contain no control characters.");
		}
	}

	/// <summary>
	///     Checks a value type a record is given: a defined type other than 7 (UTF-16 string), 10 (all types) and 12
	///     (pointer-sized value). Cheat Engine's setVarType keeps those unchanged on the record, but its byte-size,
	///     read and write code has no case for them, so the record's value could never be read or written.
	/// </summary>
	internal static void Type(VariableType value, string parameter)
	{
		SearchType(value, parameter);
		switch (value)
		{
			case VariableType.WideString:
				throw CheatEngineToolException.InvalidArgument(parameter,
					"7 (UTF-16 string) gives a record whose value Cheat Engine can neither read nor write.",
					"Use 6 with unicode true for a UTF-16 string record.");
			case VariableType.All:
				throw CheatEngineToolException.InvalidArgument(parameter,
					"10 (all types) is a scan-only value type whose value Cheat Engine can neither read nor write on " +
					"a record.",
					"Use the type of the value, such as 2 for a 4-byte integer.");
			case VariableType.Pointer:
				throw CheatEngineToolException.InvalidArgument(parameter,
					"12 (pointer-sized value) gives a record whose value Cheat Engine can neither read nor write.",
					"Use 3 on a 64-bit process or 2 otherwise; pass offsets for a pointer chain.");
		}
	}

	/// <summary>
	///     Checks a value type a search compares with the type records carry: any defined type, because a Lua script or
	///     a loaded table can give a record even a type these tools refuse.
	/// </summary>
	internal static void SearchType(VariableType value, string parameter)
	{
		if (!Enum.IsDefined(value))
		{
			throw CheatEngineToolException.InvalidArgument(parameter, "must be a defined Cheat Engine value type.");
		}
	}

	/// <summary>Checks the size of Auto Assembler text that a fixed Lua body stores on a record.</summary>
	internal static void Script(string script, string parameter)
	{
		if (script.Length > MaximumScriptLength)
		{
			throw CheatEngineToolException.LimitExceeded(parameter,
				$"must be at most {MaximumScriptLength} characters.");
		}

		if (Encoding.UTF8.GetByteCount(script) > MaximumScriptBytes)
		{
			throw CheatEngineToolException.LimitExceeded(parameter,
				$"must be at most {MaximumScriptBytes} bytes when encoded as UTF-8.");
		}
	}

	/// <summary>
	///     Parses a pointer-offset list in the pointer tools' signed hexadecimal form: Cheat Engine stores at most 128
	///     signed 32-bit offsets per record.
	/// </summary>
	/// <param name="offsets">The offsets in dereference order.</param>
	/// <param name="parameter">The parameter name for errors; items are reported as <c>parameter[i]</c>.</param>
	/// <returns>The offsets in the same order.</returns>
	internal static int[] Offsets(string[] offsets, string parameter)
	{
		long[] parsed = HexParse.Offsets(offsets, parameter, MaximumOffsets);
		int[] values = new int[parsed.Length];
		for (int index = 0; index < parsed.Length; index++)
		{
			if (parsed[index] is < int.MinValue or > int.MaxValue)
			{
				throw CheatEngineToolException.InvalidArgument($"{parameter}[{index}]",
					"must be a signed 32-bit offset from -80000000 to 7FFFFFFF.",
					"Write a negative offset with a minus sign, such as -8 rather than FFFFFFF8.");
			}

			values[index] = (int) parsed[index];
		}

		return values;
	}

	/// <summary>
	///     Formats an offset text that Cheat Engine stores: a plain signed hexadecimal number, which is how Cheat
	///     Engine stores an offset given as a number, becomes the signed 32-bit value Cheat Engine applies, in the form
	///     <see cref="HexFormat.Offset" /> writes; an empty text is the offset 0; any other text, a symbol or Lua
	///     expression, is kept unchanged.
	/// </summary>
	/// <param name="text">The stored offset text.</param>
	/// <returns>The contract form of the offset.</returns>
	internal static string CopiedOffset(string? text)
	{
		ReadOnlySpan<char> span = (text ?? string.Empty).AsSpan().Trim();
		if (span.IsEmpty)
		{
			return "0";
		}

		bool negative = span[0] == '-';
		ReadOnlySpan<char> digits = span[0] is '+' or '-' ? span[1..] : span;
		if (digits.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
		{
			digits = digits[2..];
		}
		else if (digits.StartsWith('$'))
		{
			digits = digits[1..];
		}

		if (digits.IsEmpty || digits.Length > 16 || !IsHexDigits(digits))
		{
			return text!;
		}

		ulong magnitude = ulong.Parse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
		// Cheat Engine keeps an offset as a signed 32-bit integer and drops the higher bits of a longer number.
		int value = unchecked((int) (negative ? 0UL - magnitude : magnitude));
		return HexFormat.Offset(value);
	}

	/// <summary>
	///     Checks the length and unicode options against the type the record has once the request applies.
	/// </summary>
	/// <param name="type">The record's type after the request.</param>
	/// <param name="length">The requested length, if any.</param>
	/// <param name="unicode">The requested encoding, if any.</param>
	/// <param name="prefix">The parameter path of the record, such as <c>records[0]</c>.</param>
	internal static void Layout(VariableType type, int? length, bool? unicode, string prefix)
	{
		if (length is { } count)
		{
			if (type is not (VariableType.String or VariableType.ByteArray))
			{
				throw CheatEngineToolException.InvalidArgument($"{prefix}.length",
					"applies only to a string (6) or byte array (8) record.");
			}

			if (count is < 1 or > MaximumLength)
			{
				throw CheatEngineToolException.InvalidArgument($"{prefix}.length",
					$"must be from 1 to {MaximumLength}.");
			}
		}

		if (unicode is not null && type is not VariableType.String)
		{
			throw CheatEngineToolException.InvalidArgument($"{prefix}.unicode", "applies only to a string (6) record.");
		}
	}

	/// <summary>
	///     Whether a record needs the layout step: a string record given a length or encoding, and every byte array
	///     record that is created, retyped, resized or written, so that its value text is hexadecimal byte pairs.
	/// </summary>
	internal static bool NeedsLayout(VariableType type, int? length, bool? unicode, bool bytesChange)
	{
		return type switch
		{
			VariableType.String => length is not null || unicode is not null,
			VariableType.ByteArray => bytesChange || length is not null,
			_ => false
		};
	}

	/// <summary>Whether Cheat Engine's SetValue can evaluate a value of this type as a Lua expression.</summary>
	internal static bool IsNumberType(VariableType type)
	{
		return type is VariableType.Byte or VariableType.Word or VariableType.Dword or VariableType.Qword or
			VariableType.Single or VariableType.Double or VariableType.Custom;
	}

	/// <summary>
	///     Refuses a value that Cheat Engine would hand to Lua or that the tool cannot size. For types 0 to 5 and 13
	///     Cheat Engine evaluates text in brackets, or with a <c>+ - * /</c> after its first character, as a Lua
	///     expression that also embeds the record's current value text, so only a plain decimal or hexadecimal number
	///     without such a character is accepted there. For a byte array Cheat Engine replaces a whole
	///     <c>(description)</c> value by another record's value, whose byte count the tool cannot know beforehand.
	/// </summary>
	/// <param name="type">The record's type when the value is written.</param>
	/// <param name="value">The value text.</param>
	/// <param name="parameter">The parameter name for errors.</param>
	internal static void Value(VariableType type, string value, string parameter)
	{
		if (value.Length == 0)
		{
			return;
		}

		if (IsNumberType(type) && !NumberPattern().IsMatch(value))
		{
			throw CheatEngineToolException.InvalidArgument(parameter,
				$"must be a number for value type {(int) type}: decimal such as 100, -5 or 1.5, optionally with an " +
				"unsigned exponent such as 1.5e3, or hexadecimal such as 0x64; Cheat Engine would evaluate other " +
				"text, including a + or - after the first character, as a Lua expression.");
		}

		ReadOnlySpan<char> trimmed = value.AsSpan().Trim(' ');
		if (type is VariableType.ByteArray && trimmed.Length > 2 && trimmed[0] == '(' && trimmed[^1] == ')')
		{
			throw CheatEngineToolException.InvalidArgument(parameter,
				"cannot use Cheat Engine's (description) notation on a byte array record (8), because the tool sets " +
				"the record's byte count from the value text.",
				"Write the bytes as hexadecimal pairs such as 48 8B 05.");
		}
	}

	/// <summary>
	///     Refuses a value that a record passes on to a nested record of a number type through Cheat Engine's
	///     <c>moRecursiveSetValue</c> option: that nested record would evaluate anything but a plain number as a Lua
	///     expression, whatever the type of the record written.
	/// </summary>
	/// <param name="value">The value text.</param>
	/// <param name="parameter">The parameter name for errors.</param>
	internal static void RecursiveValue(string value, string parameter)
	{
		if (value.Length > 0 && !NumberPattern().IsMatch(value))
		{
			throw CheatEngineToolException.InvalidArgument(parameter,
				"must be a number such as 100, -5, 1.5 or 0x64, because the record's moRecursiveSetValue option " +
				"passes the value on to a nested record of type 0 to 5 or 13, and Cheat Engine would evaluate other " +
				"text there as a Lua expression.",
				"Update the nested records one at a time, each with a value its own type takes.");
		}
	}

	/// <summary>
	///     Counts the bytes Cheat Engine writes for a byte array value shown as hexadecimal: it splits the value at
	///     spaces, commas and dashes and reads every word as consecutive pairs of characters, one byte per pair and a
	///     last single character, where a pair that is not hexadecimal, such as <c>??</c>, is a byte left unchanged.
	///     Cheat Engine counts characters as UTF-8 bytes.
	/// </summary>
	/// <param name="value">The byte array value text.</param>
	/// <returns>The number of bytes the value covers.</returns>
	internal static int ByteCount(string value)
	{
		int count = 0;
		foreach (string word in value.Split([' ', ',', '-'], StringSplitOptions.RemoveEmptyEntries))
		{
			count += (Encoding.UTF8.GetByteCount(word) + 1) / 2;
		}

		return count;
	}

	private static bool IsHexDigits(ReadOnlySpan<char> text)
	{
		foreach (char character in text)
		{
			if (!char.IsAsciiHexDigit(character))
			{
				return false;
			}
		}

		return true;
	}

	/// <summary>
	///     A signed decimal or hexadecimal integer, or a signed decimal number with an optional unsigned exponent: no
	///     <c>+ - * /</c> after the first character, which would make Cheat Engine evaluate the text as Lua.
	/// </summary>
	[GeneratedRegex(
		@"^ *[+-]?(?:(?:0[xX]|\$)?[0-9A-Fa-f]+|(?:[0-9]+(?:[.,][0-9]*)?|[.,][0-9]+)(?:[eE][0-9]+)?) *$",
		RegexOptions.CultureInvariant)]
	private static partial Regex NumberPattern();
}
