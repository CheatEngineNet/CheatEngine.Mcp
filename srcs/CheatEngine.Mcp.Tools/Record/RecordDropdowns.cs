using System.Text;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Tools.Record;

/// <summary>
///     Validation of replacement dropdown lists and the bounded copy of the dropdowns of address-list records.
/// </summary>
/// <remarks>
///     Cheat Engine stores a dropdown as <c>value:description</c> lines and splits each line at its first colon, so a
///     value never contains one. A record whose list is linked to another record's list reports that record's items
///     and options.
/// </remarks>
internal static class RecordDropdowns
{
	/// <summary>The most items a replacement list holds, and the most items one record's copy holds.</summary>
	internal const int MaximumItems = 1024;

	/// <summary>The longest value or description of one item, in characters.</summary>
	internal const int MaximumTextLength = 1024;

	/// <summary>The UTF-8 bytes of value and description text that a replacement list holds.</summary>
	internal const int MaximumListBytes = 262_144;

	/// <summary>The most items one copy returns across all its records.</summary>
	internal const int MaximumCopiedItems = 4096;

	/// <summary>The UTF-8 bytes of value and description text one copy returns across all its records.</summary>
	internal const int MaximumCopiedBytes = 1_048_576;

	/// <summary>Checks a replacement list and returns the <c>value:description</c> lines Cheat Engine stores.</summary>
	/// <remarks>
	///     An empty value is allowed: Cheat Engine reads a line without a colon as an empty value and the whole line as
	///     its description, and the <c>:description</c> line written for it reads back the same way, so a copied list
	///     can be written back. Non-empty values must differ with ASCII letters folded, as Cheat Engine's LowerCase and
	///     UpperCase compare them; other letters keep their case, so <c>É</c> and <c>é</c> are different values.
	/// </remarks>
	/// <param name="items">The replacement items in display order.</param>
	/// <param name="parameter">The parameter name for errors.</param>
	/// <returns>One line per item, in order.</returns>
	internal static string[] Lines(RecordDropdownItem[]? items, string parameter)
	{
		if (items is null)
		{
			throw CheatEngineToolException.InvalidArgument(parameter, "is required; pass [] to remove the list.");
		}

		if (items.Length > MaximumItems)
		{
			throw CheatEngineToolException.LimitExceeded(parameter, $"accepts at most {MaximumItems} items.");
		}

		string[] lines = new string[items.Length];
		HashSet<string> values = new(StringComparer.Ordinal);
		long bytes = 0;
		for (int index = 0; index < items.Length; index++)
		{
			RecordDropdownItem? item = items[index];
			if (item is null)
			{
				throw CheatEngineToolException.InvalidArgument($"{parameter}[{index}]", "must not be null.");
			}

			string value = $"{parameter}[{index}].value";
			RecordArguments.Text(item.Value, value, MaximumTextLength);
			if (item.Value.Contains(':', StringComparison.Ordinal))
			{
				throw CheatEngineToolException.InvalidArgument(value,
					"must not contain a colon, which separates a value from its description.");
			}

			RecordArguments.Text(item.Description, $"{parameter}[{index}].description", MaximumTextLength);
			if (item.Value.Length > 0 && !values.Add(AsciiLowerCase(item.Value)))
			{
				throw CheatEngineToolException.InvalidArgument(value,
					"repeats an earlier value; Cheat Engine compares values ignoring the case of ASCII letters.");
			}

			bytes += Encoding.UTF8.GetByteCount(item.Value) + Encoding.UTF8.GetByteCount(item.Description);
			lines[index] = item.Value + ":" + item.Description;
		}

		if (bytes > MaximumListBytes)
		{
			throw CheatEngineToolException.LimitExceeded(parameter,
				$"must hold at most {MaximumListBytes} bytes of value and description text when encoded as UTF-8.");
		}

		return lines;
	}

	/// <summary>
	///     Copies the dropdowns of records that the enclosing dispatch has just copied through the Client, in the same
	///     order, within <see cref="MaximumItems" /> items per record and the per-call copy bounds.
	/// </summary>
	/// <param name="dispatch">The dispatch whose body is running.</param>
	/// <param name="operation">The tool name for errors.</param>
	/// <param name="ids">The record ids, already copied in this dispatch.</param>
	/// <param name="cancellationToken">The token the enclosing body received.</param>
	/// <returns>One dropdown per id, in order.</returns>
	internal static RecordDropdown[] Read(ToolDispatch dispatch, string operation, int[] ids,
		CancellationToken cancellationToken)
	{
		RecordLuaDropdowns copy = dispatch.ExecuteLua(operation, RecordLuaScripts.ReadDropdowns,
			RecordLuaJsonContext.Default.RecordLuaDropdowns, cancellationToken, ids, MaximumItems, MaximumCopiedItems,
			MaximumCopiedBytes);
		if (copy.Records is not { } records || records.Length != ids.Length)
		{
			throw CheatEngineToolException.Internal("The dropdown copy did not return one entry per requested record.");
		}

		RecordDropdown[] dropdowns = new RecordDropdown[ids.Length];
		for (int index = 0; index < ids.Length; index++)
		{
			if (records[index] is not { } record || record.Id != ids[index] || record.Dropdown?.Items is null)
			{
				throw CheatEngineToolException.Internal("The dropdown copy returned an entry for a different record.");
			}

			dropdowns[index] = record.Dropdown;
		}

		return dropdowns;
	}

	/// <summary>
	///     Lowercases only the ASCII letters <c>A</c> to <c>Z</c>, as Cheat Engine's LowerCase and UpperCase do when
	///     they compare a dropdown value with a record's displayed value; every other character keeps its case.
	/// </summary>
	/// <param name="value">The dropdown value.</param>
	/// <returns>The value with its ASCII letters lowercased.</returns>
	internal static string AsciiLowerCase(string value)
	{
		return string.Create(value.Length, value, static (characters, text) =>
		{
			for (int index = 0; index < text.Length; index++)
			{
				char character = text[index];
				characters[index] = char.IsAsciiLetterUpper(character) ? char.ToLowerInvariant(character) : character;
			}
		});
	}
}
