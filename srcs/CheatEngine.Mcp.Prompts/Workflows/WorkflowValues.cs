using System.Text.Json;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Prompts.Workflows;

/// <summary>
///     The enumerated prompt argument values. Each list equals the <c>[AllowedValues]</c> of its prompt parameters,
///     which a test checks, so completion and validation offer the same values.
/// </summary>
/// <remarks>
///     The SDK completes a prompt argument with every allowed value that starts with the typed prefix, and the hosts'
///     completion bound returns the first <see cref="McpCompletions.MaximumValues" /> of them with the total and
///     <c>hasMore</c>. Only <see cref="ToolNames" /> is longer; <c>explain_error</c> checks it by its kind
///     (<see cref="WorkflowInputKind.ToolName" />) rather than as an enumeration.
/// </remarks>
internal static class WorkflowValues
{
	/// <summary>
	///     The scan value types of <c>find_known_value</c>: the <c>scan_first</c> vocabulary without <c>bytes</c>;
	///     <c>auto</c> lets the workflow choose.
	/// </summary>
	internal static IReadOnlyList<string> KnownValueTypes
	{
		get;
	} = ["auto", "byte", "int16", "int32", "int64", "float", "double", "string", "wstring"];

	/// <summary>The scan value types of <c>find_unknown_value</c>.</summary>
	internal static IReadOnlyList<string> UnknownValueTypes
	{
		get;
	} = ["auto", "int32", "float", "double"];

	/// <summary>
	///     The value types of <c>freeze_value</c>: the <c>memory_read</c> vocabulary without pointer, which is no
	///     record type. A UTF-16 text is a string record with <c>unicode</c> true, because <c>record_create</c>
	///     refuses variableType 7, and a byte array record takes its length from the hexadecimal byte pairs written
	///     to it.
	/// </summary>
	internal static IReadOnlyList<string> RecordValueTypes
	{
		get;
	} =
	[
		"int8", "uint8", "int16", "uint16", "int32", "uint32", "int64", "uint64", "float", "double", "string",
		"wstring", "bytes"
	];

	/// <summary>
	///     The record type code (<c>record_create</c> <c>variableType</c>) of each <see cref="RecordValueTypes" />
	///     entry, with the <c>unicode</c> option a UTF-16 string record needs: a code and option <c>record_create</c>
	///     accepts, which a test checks.
	/// </summary>
	internal static IReadOnlyDictionary<string, string> RecordTypeNotes
	{
		get;
	} = new Dictionary<string, string>(StringComparer.Ordinal)
	{
		["int8"] = RecordType("0"),
		["uint8"] = RecordType("0"),
		["int16"] = RecordType("1"),
		["uint16"] = RecordType("1"),
		["int32"] = RecordType("2"),
		["uint32"] = RecordType("2"),
		["int64"] = RecordType("3"),
		["uint64"] = RecordType("3"),
		["float"] = RecordType("4"),
		["double"] = RecordType("5"),
		["string"] = RecordType("6"),
		["wstring"] = RecordType("6") + " with unicode true",
		["bytes"] = RecordType("8")
	};

	/// <summary>The capture triggers of <c>find_writer</c>.</summary>
	internal static IReadOnlyList<string> Triggers
	{
		get;
	} = ["write", "access"];

	/// <summary>The value sizes of <c>find_writer</c>, in bytes.</summary>
	internal static IReadOnlyList<string> ValueSizes
	{
		get;
	} = ["1", "2", "4", "8"];

	/// <summary>The pointer levels of the pointer workflows.</summary>
	internal static IReadOnlyList<string> PointerLevels
	{
		get;
	} = ["1", "2", "3", "4", "5", "6", "7", "8"];

	/// <summary>The answers of <c>cleanup_session</c>'s keepAddressList.</summary>
	internal static IReadOnlyList<string> YesNo
	{
		get;
	} = ["yes", "no"];

	/// <summary>The practice targets of <c>ce_tutorial_walkthrough</c>.</summary>
	internal static IReadOnlyList<string> Tutorials
	{
		get;
	} = ["tutorial", "gtutorial"];

	/// <summary>The tutorial steps; gtutorial has levels 1 to 3 only.</summary>
	internal static IReadOnlyList<string> TutorialSteps
	{
		get;
	} = ["1", "2", "3", "4", "5", "6", "7", "8", "9"];

	/// <summary>The text encodings of <c>find_code_by_string</c> and <c>find_text</c>; <c>auto</c> tries both.</summary>
	internal static IReadOnlyList<string> TextEncodings
	{
		get;
	} = ["auto", "utf8", "utf16"];

	/// <summary>What <c>patch_branch</c> does to a conditional jump.</summary>
	internal static IReadOnlyList<string> BranchModes
	{
		get;
	} = ["always", "never", "invert"];

	/// <summary>The in-game state of the flag <c>find_flag</c> looks for.</summary>
	internal static IReadOnlyList<string> FlagStates
	{
		get;
	} = ["on", "off"];

	/// <summary>The scan types of <c>find_flag</c>; <c>auto</c> tries byte, then int32.</summary>
	internal static IReadOnlyList<string> FlagWidths
	{
		get;
	} = ["auto", "byte", "int32"];

	/// <summary>The scan types of <c>find_position</c>; <c>auto</c> picks double on UE5 and float otherwise.</summary>
	internal static IReadOnlyList<string> PositionTypes
	{
		get;
	} = ["auto", "float", "double"];

	/// <summary>Whether <c>find_text</c> only finds the text or also edits it.</summary>
	internal static IReadOnlyList<string> TextPurposes
	{
		get;
	} = ["find", "edit"];

	/// <summary>The failure kinds of <c>explain_error</c>: every <see cref="ToolErrorKind" /> wire name.</summary>
	internal static IReadOnlyList<string> ErrorKinds
	{
		get;
	} = [.. Enum.GetNames<ToolErrorKind>().Select(JsonNamingPolicy.SnakeCaseLower.ConvertName)];

	/// <summary>
	///     The tool names <c>explain_error</c> completes: every frozen tool name
	///     (<see cref="CheatEngineToolNames.All" />, <c>instance_list</c> included), in ordinal order. It is longer
	///     than one completion may return, so the hosts bound its completion; the prompt checks the value by its kind
	///     instead of as an enumeration.
	/// </summary>
	internal static IReadOnlyList<string> ToolNames
	{
		get;
	} = [.. CheatEngineToolNames.All.Order(StringComparer.Ordinal)];

	/// <summary>The host effects of <c>explain_error</c>: every <see cref="ToolHostEffect" /> wire name.</summary>
	internal static IReadOnlyList<string> HostEffects
	{
		get;
	} = [.. Enum.GetNames<ToolHostEffect>().Select(JsonNamingPolicy.SnakeCaseLower.ConvertName)];

	/// <summary>How much <c>session_report</c> reports.</summary>
	internal static IReadOnlyList<string> ReportDetails
	{
		get;
	} = ["summary", "full"];

	/// <summary>Whether a <c>write_lua_script</c> script only reads or also changes state.</summary>
	internal static IReadOnlyList<string> ScriptEffects
	{
		get;
	} = ["read_only", "mutating"];

	/// <summary>The cheat goals <c>plan_cheat</c> routes, one body section each.</summary>
	internal static IReadOnlyList<string> CheatGoals
	{
		get;
	} =
	[
		"infinite_health", "infinite_ammo", "currency", "one_hit_kill", "no_cooldown", "teleport", "game_speed",
		"toggle_feature", "unlock_items", "other"
	];

	/// <summary>The runtimes <c>find_entity_list</c> distinguishes; <c>auto</c> classifies the target first.</summary>
	internal static IReadOnlyList<string> EntityRuntimes
	{
		get;
	} = ["auto", "native", "mono", "dotnet"];

	/// <summary>The timer shapes of <c>find_timer</c>.</summary>
	internal static IReadOnlyList<string> TimerKinds
	{
		get;
	} = ["countdown", "countup", "cooldown", "unknown"];

	/// <summary>Where <c>group_scan</c> searches.</summary>
	internal static IReadOnlyList<string> ScanRegions
	{
		get;
	} = ["heap", "module", "any"];

	/// <summary>The value types <c>compare_snapshots</c> interprets changed slots as.</summary>
	internal static IReadOnlyList<string> SnapshotValueTypes
	{
		get;
	} = ["int8", "uint8", "int16", "uint16", "int32", "uint32", "int64", "uint64", "float", "double"];

	/// <summary>The modes of <c>unity_il2cpp_recon</c>: offline (no injection) or CE's data collector.</summary>
	internal static IReadOnlyList<string> Il2CppModes
	{
		get;
	} = ["offline", "collector"];

	/// <summary>The engine versions of <c>unreal_recon</c>; <c>auto</c> reads the version string first.</summary>
	internal static IReadOnlyList<string> UnrealVersions
	{
		get;
	} = ["auto", "ue4", "ue5"];

	/// <summary>The emulators <c>emulator_memory</c> knows.</summary>
	internal static IReadOnlyList<string> Emulators
	{
		get;
	} = ["dolphin", "pcsx2", "ppsspp", "rpcs3", "cemu", "other"];

	private static string RecordType(string code)
	{
		return CheatEngineToolNames.RecordCreate + " variableType " + code;
	}
}
