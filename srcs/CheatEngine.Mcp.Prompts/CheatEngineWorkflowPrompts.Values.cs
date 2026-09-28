using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

using CheatEngine.Mcp.Prompts.Workflows;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Prompts;

public sealed partial class CheatEngineWorkflowPrompts
{
	/// <summary>Find a known value.</summary>
	[McpServerPrompt(Name = "find_known_value", Title = "Find a known value")]
	[Description("The value is shown as a number: exact first and next scans, verification one candidate at a time, "
				 + "then a record.")]
	public static GetPromptResult FindKnownValuePrompt(
		[Display(Name = "Current value")]
		[Description("The number the game shows now, such as 100 or 87.5.")]
		string currentValue,
		[Display(Name = "Value type")]
		[Description("The scan type: auto (default), byte, int16, int32, int64, float, double, string or wstring. "
					 + "byte is unsigned (-1 is 255); scan an unsigned value above the signed maximum as the signed "
					 + "type of the same width, which util_convert_value computes.")]
		[AllowedValues("auto", "byte", "int16", "int32", "int64", "float", "double", "string", "wstring")]
		string? valueType = null,
		[Display(Name = "What the value is")]
		[Description("What the value is, such as player health.")]
		string? description = null,
		McpServer? server = null)
	{
		return WorkflowPrompt.Render(server, ValueWorkflows.FindKnownValue, currentValue, valueType, description);
	}

	/// <summary>Find an unknown value.</summary>
	[McpServerPrompt(Name = "find_unknown_value", Title = "Find an unknown value")]
	[Description("Only a bar or a change is visible: an unknown first scan, then increased, decreased, changed and "
				 + "unchanged scans.")]
	public static GetPromptResult FindUnknownValuePrompt(
		[Display(Name = "Value type")]
		[Description("The value type: auto (default), int32, float or double.")]
		[AllowedValues("auto", "int32", "float", "double")]
		string? valueType = null,
		[Display(Name = "What the value is")]
		[Description("What the value is, such as the stamina bar.")]
		string? description = null,
		McpServer? server = null)
	{
		return WorkflowPrompt.Render(server, ValueWorkflows.FindUnknownValue, valueType, description);
	}

	/// <summary>Find a floating-point value.</summary>
	[McpServerPrompt(Name = "find_float_value", Title = "Find a floating-point value")]
	[Description("Floats, doubles, percentages and positions: range scans around the displayed value, then "
				 + "neighbouring fields.")]
	public static GetPromptResult FindFloatValuePrompt(
		[Display(Name = "Displayed value")]
		[Description("The value the game shows, such as 87.3 or 75%; omit it for a bar without a number.")]
		string? displayedValue = null,
		[Display(Name = "What the value is")]
		[Description("What the value is, such as the player's X position.")]
		string? description = null,
		McpServer? server = null)
	{
		return WorkflowPrompt.Render(server, ValueWorkflows.FindFloatValue, displayedValue, description);
	}

	/// <summary>Find a boolean flag or toggle.</summary>
	[McpServerPrompt(Name = "find_flag", Title = "Find a boolean flag or toggle")]
	[Description("An on/off state such as god mode, a key or an unlocked feature: exact scans while the user toggles "
				 + "it, then its bits and the code that tests it.")]
	public static GetPromptResult FindFlagPrompt(
		[Display(Name = "Current state")]
		[Description("The flag's state in game now: on or off.")] [AllowedValues("on", "off")]
		string state,
		[Display(Name = "Scan type")]
		[Description("The scan type: auto (default: byte, then int32), byte or int32.")]
		[AllowedValues("auto", "byte", "int32")]
		string? width = null,
		[Display(Name = "What the flag controls")]
		[Description("What the flag controls, such as god mode.")]
		string? description = null,
		McpServer? server = null)
	{
		return WorkflowPrompt.Render(server, ValueWorkflows.FindFlag, state, width, description);
	}

	/// <summary>Find coordinates and teleport.</summary>
	[McpServerPrompt(Name = "find_position", Title = "Find coordinates and teleport")]
	[Description("Coordinates to save, restore or teleport in a single-player game: unknown scans while the user moves "
				 + "along one axis, then the neighbouring axes and a verified write.")]
	public static GetPromptResult FindPositionPrompt(
		[Display(Name = "Coordinate type")]
		[Description("The coordinate type: auto (default; double on UE5, float otherwise), float or double.")]
		[AllowedValues("auto", "float", "double")]
		string? valueType = null,
		[Display(Name = "X coordinate address")]
		[Description("The address of the X coordinate, when known, to skip the scan. " + AddressHelp)]
		string? address = null,
		[Display(Name = "Whose position")]
		[Description("Whose position it is, such as the player.")]
		string? description = null,
		McpServer? server = null)
	{
		return WorkflowPrompt.Render(server, ValueWorkflows.FindPosition, valueType, address, description);
	}

	/// <summary>Find a timer or cooldown.</summary>
	[McpServerPrompt(Name = "find_timer", Title = "Find a timer or cooldown")]
	[Description("A timer or cooldown: scans while it runs and when it is ready, then the code that sets and "
				 + "decrements it, and the remedy.")]
	public static GetPromptResult FindTimerPrompt(
		[Display(Name = "Timer shape")]
		[Description("The timer's shape: countdown, countup, cooldown or unknown (default).")]
		[AllowedValues("countdown", "countup", "cooldown", "unknown")]
		string? kind = null,
		[Display(Name = "Displayed time")]
		[Description("The time the game shows, such as 30, when it shows one.")]
		string? displayedValue = null,
		[Display(Name = "What the timer controls")]
		[Description("What the timer controls, such as the dash cooldown.")]
		string? description = null,
		McpServer? server = null)
	{
		return WorkflowPrompt.Render(server, ValueWorkflows.FindTimer, kind, displayedValue, description);
	}

	/// <summary>Find and edit text in memory.</summary>
	[McpServerPrompt(Name = "find_text", Title = "Find and edit text in memory")]
	[Description("Text in memory such as a player name or a dialogue buffer: UTF-8 and UTF-16 byte searches, its "
				 + "container and owner, and an in-place edit with consent.")]
	public static GetPromptResult FindTextPrompt(
		[Display(Name = "Text")]
		[Description("The exact text, case preserved, such as the player's name.")]
		string text,
		[Display(Name = "Encoding")]
		[Description("auto (default) tries UTF-8 and UTF-16; utf8 or utf16 searches one encoding.")]
		[AllowedValues("auto", "utf8", "utf16")]
		string? encoding = null,
		[Display(Name = "Purpose")]
		[Description("find (default) locates the text; edit also changes it in place, at the same length or shorter.")]
		[AllowedValues("find", "edit")]
		string? purpose = null,
		McpServer? server = null)
	{
		return WorkflowPrompt.Render(server, ValueWorkflows.FindText, text, encoding, purpose);
	}

	/// <summary>Find an object by several known values.</summary>
	[McpServerPrompt(Name = "group_scan", Title = "Find an object by several known values")]
	[Description("Several known values sit at fixed offsets in one object, such as health and ammo: search them as "
				 + "one byte pattern, then narrow and verify.")]
	public static GetPromptResult GroupScanPrompt(
		[Display(Name = "Values and offsets")]
		[Description("The values as type:value@hexOffset separated by commas, such as int32:100@0,int32:12@10.")]
		string values,
		[Display(Name = "Region")]
		[Description("heap (default) searches writable private memory; module searches one module; any searches "
					 + "everything.")]
		[AllowedValues("heap", "module", "any")]
		string? region = null,
		McpServer? server = null)
	{
		return WorkflowPrompt.Render(server, ValueWorkflows.GroupScan, values, region);
	}

	/// <summary>Find what changed in a memory range.</summary>
	[McpServerPrompt(Name = "compare_snapshots", Title = "Find what changed in a memory range")]
	[Description("Which fields of a memory range change when the user does one thing in game: a snapshot before, a "
				 + "comparison after, typed changes and cleanup.")]
	public static GetPromptResult CompareSnapshotsPrompt(
		[Display(Name = "Start address")]
		[Description("The start of the range. " + AddressHelp)]
		string address,
		[Display(Name = "Size in bytes")]
		[Description("How many bytes to snapshot, a decimal number from 1 to 16777216 (default 256).")]
		string? size = null,
		[Display(Name = "Value type")]
		[Description("How to read changed slots: int8, uint8, int16, uint16, int32 (default), uint32, int64, uint64, "
					 + "float or double.")]
		[AllowedValues("int8", "uint8", "int16", "uint16", "int32", "uint32", "int64", "uint64", "float", "double")]
		string? valueType = null,
		[Display(Name = "Action between snapshots")]
		[Description("What the user will do between the snapshots, such as take damage.")]
		string? description = null,
		McpServer? server = null)
	{
		return WorkflowPrompt.Render(server, ValueWorkflows.CompareSnapshots, address, size, valueType, description);
	}

	/// <summary>Explain what is at an address.</summary>
	[McpServerPrompt(Name = "identify_address", Title = "Explain what is at an address")]
	[Description("Classify an address as code, static data, a heap object, a stack or an unreadable page, with its "
				 + "symbol, its owners and the next workflow.")]
	public static GetPromptResult IdentifyAddressPrompt(
		[Display(Name = "Address")]
		[Description("The address to explain. " + AddressHelp)]
		string address,
		McpServer? server = null)
	{
		return WorkflowPrompt.Render(server, ValueWorkflows.IdentifyAddress, address);
	}

	/// <summary>Freeze a value.</summary>
	[McpServerPrompt(Name = "freeze_value", Title = "Freeze a value")]
	[Description("Hold a value constant with an address-list record, verify that the freeze holds and undo it.")]
	public static GetPromptResult FreezeValuePrompt(
		[Display(Name = "Value address")]
		[Description("The address of the value. " + AddressHelp)]
		string address,
		[Display(Name = "Value type")]
		[Description("The value type: int8, uint8, int16, uint16, int32, uint32, int64, uint64, float, double, "
					 + "string (UTF-8), wstring (UTF-16) or bytes (a byte array). The prompt shows the matching "
					 + "record_create variableType code.")]
		[AllowedValues("int8", "uint8", "int16", "uint16", "int32", "uint32", "int64", "uint64", "float", "double",
			"string", "wstring", "bytes")]
		string valueType,
		[Display(Name = "Value to hold")]
		[Description("The value to hold, as text or, for bytes, hexadecimal byte pairs such as 90 90; omit it to "
					 + "hold the current value.")]
		string? value = null,
		[Display(Name = "Record description")]
		[Description("The record description, such as Infinite health.")]
		string? description = null,
		McpServer? server = null)
	{
		return WorkflowPrompt.Render(server, ValueWorkflows.FreezeValue, address, valueType, value, description);
	}
}
