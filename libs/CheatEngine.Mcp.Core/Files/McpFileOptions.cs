namespace CheatEngine.Mcp.Core.Files;

/// <summary>The host-file policy of one activation, bound once from the <c>Mcp:Files</c> section.</summary>
/// <remarks>
///     Reads (a file opened as a process, a symbol module, a module file compared for patches) need no root; they only
///     pass the path rules of <see cref="McpFilePaths" />. Cheat tables are governed by the Client's own
///     <c>CheatEngineClient:AllowedTableRoots</c>.
/// </remarks>
public sealed class McpFileOptions
{
	/// <summary>The configuration section that holds the host-file policy.</summary>
	public const string SectionName = "Mcp:Files";

	/// <summary>
	///     The absolute local directories under which tools may write files, such as memory dumps or the saved image of a
	///     file opened as a process. Empty by default, which refuses every write. The MCP data directory and the instance
	///     registry stay refused even under a listed root.
	/// </summary>
	[LocalDirectories]
	public string[] AllowedRoots
	{
		get;
		set;
	} = [];
}
