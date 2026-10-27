namespace CheatEngine.Mcp.Tools.Record;

/// <summary>
///     The fixed Lua response used after creating a record or changing an Auto Assembler script, a string or byte array
///     layout, pointer offsets or a dropdown.
/// </summary>
public sealed record RecordLuaChanged(int Id);

/// <summary>The fixed Lua copy of one page of a record's immediate children.</summary>
/// <param name="Total">The parent's current number of immediate children.</param>
/// <param name="Ids">The ids of the children on the page, in address-list order.</param>
internal sealed record RecordLuaChildren(int Total, int[] Ids);

/// <summary>The fixed Lua copy of the dropdowns of the requested records, in request order.</summary>
/// <param name="Records">One entry per requested record.</param>
internal sealed record RecordLuaDropdowns(RecordLuaDropdown[] Records);

/// <summary>The dropdown of one record as the fixed Lua copy reports it.</summary>
/// <param name="Id">The record id Cheat Engine returned for the requested id.</param>
/// <param name="Dropdown">The bounded dropdown copy.</param>
internal sealed record RecordLuaDropdown(int Id, RecordDropdown Dropdown);

/// <summary>The fixed Lua copy of the pointer offsets of the requested records, in request order.</summary>
/// <param name="Records">One entry per requested record.</param>
internal sealed record RecordLuaOffsets(RecordLuaOffsetList[] Records);

/// <summary>The stored offset texts of one record, in dereference order.</summary>
/// <param name="Id">The record id Cheat Engine returned for the requested id.</param>
/// <param name="Offsets">The stored offset texts, nearest the base first, cut by the copy bounds.</param>
internal sealed record RecordLuaOffsetList(int Id, string[] Offsets);

/// <summary>Whether a change of each requested record would reach a nested record of one of the given types.</summary>
/// <param name="Reaches">One flag per requested record, in request order.</param>
internal sealed record RecordLuaReaches(bool[] Reaches);

/// <summary>The state of one record after the fixed Lua state change.</summary>
/// <param name="Active">The record's state after the change.</param>
/// <param name="Pending">Whether an asynchronous activation is still running.</param>
/// <param name="Reason">The last reason Cheat Engine passed to <c>OnActivationFailure</c>, if it called it.</param>
/// <param name="Text">The last reason text Cheat Engine passed with it.</param>
internal sealed record RecordLuaActivation(bool Active, bool Pending, int? Reason = null, string? Text = null);
