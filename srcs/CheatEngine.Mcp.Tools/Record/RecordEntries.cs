using CheatEngine.Client.Tables;
using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Tools.Record;

/// <summary>
///     Maps records that the typed Client copied to contract entries, adding the pointer offsets that the Client's
///     snapshot counts but does not copy.
/// </summary>
/// <remarks>
///     One fixed Lua pass copies the offsets of every record of a result that has any, so a result without pointer
///     records costs no Lua call. The pass reads the stored offset texts and never evaluates a symbolic offset.
/// </remarks>
internal static class RecordEntries
{
	/// <summary>The most offsets one call copies across all its records.</summary>
	internal const int MaximumCopiedOffsets = 65_536;

	/// <summary>The most bytes of offset text one call copies across all its records.</summary>
	internal const int MaximumCopiedOffsetBytes = 1_048_576;

	/// <summary>Maps one copied record, with its dropdown when one was copied.</summary>
	/// <param name="dispatch">The dispatch whose body is running.</param>
	/// <param name="operation">The tool name for errors.</param>
	/// <param name="record">The record, copied in this dispatch.</param>
	/// <param name="cancellationToken">The token the enclosing body received.</param>
	/// <param name="dropdown">The record's copied dropdown, if any.</param>
	/// <returns>The contract entry.</returns>
	internal static RecordEntry One(ToolDispatch dispatch, string operation, MemoryRecordSnapshot record,
		CancellationToken cancellationToken, RecordDropdown? dropdown = null)
	{
		return Map(dispatch, operation, [record], cancellationToken, dropdown is null ? null : [dropdown])[0];
	}

	/// <summary>Maps records copied in this dispatch, in order, with their dropdowns when they were copied.</summary>
	/// <param name="dispatch">The dispatch whose body is running.</param>
	/// <param name="operation">The tool name for errors.</param>
	/// <param name="records">The records, copied in this dispatch.</param>
	/// <param name="cancellationToken">The token the enclosing body received.</param>
	/// <param name="dropdowns">One copied dropdown per record, or <see langword="null" /> for none.</param>
	/// <returns>One contract entry per record, in order.</returns>
	internal static RecordEntry[] Map(ToolDispatch dispatch, string operation,
		IReadOnlyList<MemoryRecordSnapshot> records, CancellationToken cancellationToken,
		IReadOnlyList<RecordDropdown>? dropdowns = null)
	{
		int[] pointers =
		[
			.. records.Where(static record => record.Content.OffsetCount > 0)
				.Select(static record => record.Id.Value).Distinct()
		];
		Dictionary<int, string[]> offsets =
			pointers.Length == 0 ? [] : ReadOffsets(dispatch, operation, pointers, cancellationToken);
		RecordEntry[] entries = new RecordEntry[records.Count];
		for (int index = 0; index < entries.Length; index++)
		{
			MemoryRecordSnapshot record = records[index];
			entries[index] = RecordArguments.Entry(record, offsets.GetValueOrDefault(record.Id.Value),
				dropdowns?[index]);
		}

		return entries;
	}

	/// <summary>Copies the offsets of the given records in dereference order, in the contract's offset form.</summary>
	private static Dictionary<int, string[]> ReadOffsets(ToolDispatch dispatch, string operation, int[] ids,
		CancellationToken cancellationToken)
	{
		RecordLuaOffsets copy = dispatch.ExecuteLua(operation, RecordLuaScripts.ReadOffsets,
			RecordLuaJsonContext.Default.RecordLuaOffsets, cancellationToken, ids, MaximumCopiedOffsets,
			MaximumCopiedOffsetBytes);
		if (copy.Records is not { } records || records.Length != ids.Length)
		{
			throw CheatEngineToolException.Internal("The offset copy did not return one entry per requested record.");
		}

		Dictionary<int, string[]> offsets = [];
		for (int index = 0; index < ids.Length; index++)
		{
			if (records[index] is not { } record || record.Id != ids[index] || record.Offsets is null)
			{
				throw CheatEngineToolException.Internal("The offset copy returned an entry for a different record.");
			}

			offsets[record.Id] = [.. record.Offsets.Select(RecordArguments.CopiedOffset)];
		}

		return offsets;
	}
}
