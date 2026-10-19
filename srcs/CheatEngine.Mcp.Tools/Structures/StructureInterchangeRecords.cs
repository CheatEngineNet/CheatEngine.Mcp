using System.ComponentModel;

namespace CheatEngine.Mcp.Tools.Structures;

/// <summary>Receipt for a versioned Structure Dissect interchange file.</summary>
public sealed record StructureInterchangeResult(
	[property: Description("The destination or source file name, without its host path.")] string FileName,
	[property: Description("The interchange format version.")] int Version,
	[property: Description("The target pointer width in bytes, 4 or 8.")] int PointerWidth,
	[property: Description("The selected root structure names, in order.")] string[] Names,
	[property: Description("The number of layout definitions in the file.")] int LayoutCount);

internal sealed record StructureInterchangeDocument(int Version, int PointerWidth, string[] Roots,
	StructureInterchangeLayout[] Layouts);
internal sealed record StructureInterchangeLayout(string Name, bool Internal, StructureInterchangeField[] Fields);
internal sealed record StructureInterchangeField(string Offset, string Name, string ValueType, string? Display,
	int? ByteSize, string? ChildStructure, string? ChildStructureStart, string? CustomType, int? BitStart, int? BitSize);
internal sealed record StructureInterchangeLuaExport(StructureInterchangeLuaLayout[] Layouts);
internal sealed record StructureInterchangeLuaLayout(string Name, bool Internal, StructureInterchangeLuaField[] Fields);
internal sealed record StructureInterchangeLuaField(int Index, long Offset, string? Name, int Vartype, string? Display,
	int ByteSize, string? ChildStructure, long? ChildStructureStart, string? CustomType, int? BitStart, int? BitSize,
	bool Nested);
internal sealed record StructureInterchangeLuaImport(string[] Names, int LayoutCount);
