using System.ComponentModel;

using CheatEngine.Mcp.Tools.Modules;

namespace CheatEngine.Mcp.Tools.Investigation;

/// <summary>A durable, historical investigation observation.</summary>
public sealed record InvestigationNote(
	[property: Description("The canonical lower-case note name.")] string Name,
	[property: Description("The immutable revision number.")] int Revision,
	[property: Description("The nonempty note title.")] string Title,
	[property: Description("The note body.")] string Body,
	[property: Description("The descriptive tags.")] string[] Tags,
	[property: Description("The optional confidence: low, medium, or high.")] string? Confidence,
	[property: Description("The recorded observations.")] string[] Observations,
	[property: Description("The optional historical module-relative binding. It never exposes a current address.")] ModuleRvaBinding? Binding = null);

/// <summary>A bounded page of latest revisions, ordered by canonical name.</summary>
public sealed record InvestigationNotePage(
	[property: Description("The matching latest notes in this read.")] int Total,
	[property: Description("The offset for the next page, or null at the end.")] int? NextOffset,
	[property: Description("Whether another page follows in this read.")] bool Truncated,
	[property: Description("The requested page of historical notes.")] InvestigationNote[] Notes);

/// <summary>The result of writing a versioned investigation export.</summary>
public sealed record InvestigationExportResult(
	[property: Description("The destination file name only, not a host path.")] string FileName,
	[property: Description("The number of exported latest notes.")] int Notes,
	[property: Description("The export envelope format.")] string Format);

internal sealed record InvestigationNoteFile(string Schema, int Version, string Name, int Revision, string Title,
	string Body, string[] Tags, string? Confidence, string[] Observations, ModuleRvaBinding? Binding);

internal sealed record InvestigationExportFile(string Schema, int Version, InvestigationNote[] Notes);
