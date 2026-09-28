using System.ComponentModel;

namespace CheatEngine.Mcp.Tools.Table;

/// <summary>What the table inspection found before Cheat Engine was allowed to load it.</summary>
public sealed record TableInspectionResult(
	[property: Description("xml when completely inspected, or the opaque format that forced a conservative policy.")]
	string Format,
	[property: Description("Whether a Lua script is present or the table could not be inspected.")]
	bool ContainsLua,
	[property: Description("Whether forms are present or the table could not be inspected.")]
	bool ContainsForms,
	[property: Description("Whether the table sets UsesMono or could not be inspected.")]
	bool UsesMono,
	[property: Description("The CheatEntry count for an inspected XML table; zero for an opaque table.")]
	int RecordCount,
	[property: Description("The Auto Assembler script count for an inspected XML table; zero for an opaque table.")]
	int AssemblerScriptCount,
	[property:
		Description("The feature switches this particular table required and the tool enforced before loading it.")]
	string[] Requires);

/// <summary>The result of loading an inspected trusted table.</summary>
public sealed record TableLoadResult(
	[property: Description("The absolute table path admitted by CheatEngine.Client's allowed table roots.")]
	string Path,
	[property: Description("Whether Cheat Engine merged the table into the existing address list.")]
	bool Merge,
	[property: Description("The inspection that ran while the source file was pinned.")]
	TableInspectionResult Inspection,
	[property: Description("Whether this load could trigger Cheat Engine's Mono data-collector auto attach.")]
	bool MonoAutoAttach);

/// <summary>The result of saving the current table.</summary>
public sealed record TableSaveResult(
	[property: Description("The absolute table destination admitted by CheatEngine.Client's allowed table roots.")]
	string Path,
	[property: Description("Whether a file existed at the destination immediately before the save began.")]
	bool ReplacedExisting);

/// <summary>One table file that lies below an allowed Client table root.</summary>
public sealed record TableFileEntry(
	[property: Description("The absolute table file path to pass to table_load.")]
	string Path,
	[property: Description("The file size in bytes.")]
	long Size,
	[property: Description("The file's last-write time in UTC.")]
	DateTimeOffset LastWriteUtc);

/// <summary>A bounded page of table files in the configured Client table roots.</summary>
public sealed record TableFilePage(
	[property: Description("The configured allowed table roots that were inspected.")]
	string[] Roots,
	[property:
		Description("How many matching files were retained for paging; it is a lower bound when exact is false.")]
	int Total,
	[property: Description("Whether every matching file below the roots was considered.")]
	bool Exact,
	[property: Description("The requested zero-based offset.")]
	int Offset,
	[property: Description("The page of .CT, .XML and .CETRAINER files.")]
	TableFileEntry[] Files,
	[property: Description("The next offset within the retained page set, omitted after its final page.")]
	int? NextOffset = null);
