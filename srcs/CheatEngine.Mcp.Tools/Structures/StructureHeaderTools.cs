using System.ComponentModel;
using System.Globalization;
using System.Text;

using CheatEngine.Mcp.Core.Contract;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools.Structures;

/// <summary>
///     Generates C declarations from Cheat Engine structures: with Cheat Engine's own <c>generate_c_header</c> when it
///     has one, and with <see cref="StructureHeaderWriter" /> otherwise or on request.
/// </summary>
[McpServerToolType]
public sealed class StructureHeaderTools
{
	internal const int MaxRequested = 64;
	internal const int MaxStructures = 256;
	internal const int MaxElements = 8192;
	internal const int MaxTextBytes = 1024 * 1024;

	/// <summary>The most element bytes Cheat Engine's generator may walk, one Lua table entry per byte.</summary>
	internal const int MaxCheatEngineBytes = 1024 * 1024;

	private readonly ToolDispatch _dispatch;

	/// <summary>Creates the tools; nothing is dispatched until a call.</summary>
	/// <param name="dispatch">The activation's dispatch facade.</param>
	public StructureHeaderTools(ToolDispatch dispatch)
	{
		ArgumentNullException.ThrowIfNull(dispatch);
		_dispatch = dispatch;
	}

	/// <summary>Generates C declarations for structures and the child structures they reach.</summary>
	[McpServerTool(Name = CheatEngineToolNames.StructureGenerateCHeader, Title = "Generate a C header", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Writes C declarations for structures and every child structure their pointer elements reach: forward " +
		"typedefs, then each structure with padding for gaps. By default Cheat Engine's own generator writes them, " +
		"as its Export to C header file menu does. managed writes fixed-width types, offset comments, pack(1) and " +
		"commented size checks, sizes byte-array, bit-field and custom elements exactly (Cheat Engine writes an int), " +
		"and is the fallback when Cheat Engine's generator is missing or fails or the elements span more than 1 MiB. " +
		"At most 256 structures, 8192 elements and 1 MiB of text.")]
	public StructureCHeader GenerateCHeader(
		[Description("The distinct case-sensitive names of the structures to declare (1-64).")]
		string[] names,
		[Description(
			"cheat_engine or managed; omit to use Cheat Engine's generator when it has one and managed otherwise.")]
		StructureHeaderGenerator? generator = null,
		CancellationToken cancellationToken = default)
	{
		string[] requested = StructureArguments.Names(names, "names", MaxRequested);
		string mode = generator switch
		{
			null => "auto",
			StructureHeaderGenerator.CheatEngine => "cheat_engine",
			StructureHeaderGenerator.Managed => "managed",
			_ => throw CheatEngineToolException.InvalidArgument("generator", "is not a supported value.")
		};
		StructureLuaHeader header = _dispatch.RunLua(CheatEngineToolNames.StructureGenerateCHeader,
			StructureLuaScripts.CHeader, StructureLuaJsonContext.Default.StructureLuaHeader, cancellationToken,
			requested, mode, MaxStructures, MaxElements, MaxTextBytes, MaxCheatEngineBytes);
		if (header.Text is { } text)
		{
			return new StructureCHeader(text, StructureHeaderGenerator.CheatEngine, header.Names);
		}

		string written = StructureHeaderWriter.Write(header.Structures ??
													 throw CheatEngineToolException.Internal(
														 "The C header script returned neither text nor structures."));
		int bytes = Encoding.UTF8.GetByteCount(written);
		return bytes <= MaxTextBytes
			? new StructureCHeader(written, StructureHeaderGenerator.Managed, header.Names)
			: throw CheatEngineToolException.LimitExceeded("names",
				$"declare {bytes.ToString(CultureInfo.InvariantCulture)} bytes of C; at most " +
				$"{MaxTextBytes.ToString(CultureInfo.InvariantCulture)} are returned, so request fewer structures.");
	}
}
