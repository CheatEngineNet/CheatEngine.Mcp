using System.ComponentModel;
using System.Text.Json;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Tools.Lua;

/// <summary>The bounded outcome of one caller-authored Lua chunk.</summary>
public sealed record LuaExecuteResult(
	[property: Description("Whether Lua compiled and completed without raising an error.")]
	bool Ok,
	[property: Description("compile or runtime when Ok is false.")]
	string? Phase = null,
	[property: Description("The Lua compiler or runtime message when Ok is false.")]
	string? Error = null,
	[property:
		Description(
			"The confirmed effect of the caller chunk: completed on success, not_applied after a compile error, or unknown after a runtime error.")]
	ToolHostEffect HostEffect = ToolHostEffect.Completed,
	[property:
		Description(
			"Every returned Lua value in order; opaque values are represented as null or omitted from objects.")]
	JsonElement[]? ReturnValues = null,
	[property: Description("Functions, userdata and threads omitted while copying returned values.")]
	int DroppedOpaqueCount = 0);

/// <summary>The private, fixed-stage result of caller-authored Lua before the tool adds copy metadata.</summary>
internal sealed record LuaExecuteOutcome(
	bool Ok,
	string? Phase = null,
	string? Error = null,
	JsonElement[]? ReturnValues = null);

/// <summary>A page of matching lines from the installed Cheat Engine Lua API reference.</summary>
public sealed record LuaApiSearchResult(
	[property: Description("The matching lines found in celua.txt, or in its bounded prefix when truncated is true.")]
	int Total,
	[property:
		Description(
			"The offset to pass for the next page, or null when this is the last page or a source-file bound stopped the search.")]
	int? NextOffset,
	[property: Description("Whether a reviewed source-file bound prevented a complete search.")]
	bool Truncated,
	[property: Description("Matching celua.txt lines in this page.")]
	LuaApiSearchLine[] Lines);

/// <summary>One numbered line from the installed Cheat Engine Lua API reference.</summary>
public sealed record LuaApiSearchLine(
	[property: Description("The one-based line number in celua.txt.")]
	int Number,
	[property: Description("The matching line, up to the reviewed per-line bound.")]
	string Text);
