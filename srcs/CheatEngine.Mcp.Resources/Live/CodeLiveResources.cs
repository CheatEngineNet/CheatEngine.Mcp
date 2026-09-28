using System.ComponentModel;
using System.Text.Json;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tools.Code;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Resources.Live;

/// <summary>The read-only code projection of one Cheat Engine instance: the disassembly at one address.</summary>
[McpServerResourceType]
public sealed class CodeLiveResources(CodeTools code)
{
	private const string DisassemblyPath = McpResourceUris.InstancePrefix + "disassembly";
	private const int DefaultInstructions = 20;
	private const int MaximumInstructions = 1024;

	/// <summary>Disassembles forward from one address.</summary>
	/// <param name="address">The instruction address or address expression.</param>
	/// <param name="count">The number of instructions; 20 when absent.</param>
	/// <param name="cancellationToken">The resource read cancellation.</param>
	/// <returns>The structured result of <c>code_disassemble</c>.</returns>
	[McpServerResource(UriTemplate = DisassemblyPath + "/{address}{?count}", Name = "instance_disassembly",
		Title = "Disassembly", MimeType = McpResourceUris.JsonMimeType)]
	[McpSourceTool(typeof(CodeTools), CheatEngineToolNames.CodeDisassemble)]
	[McpResourceAnnotations(Role.Assistant, Priority = LiveResourceResults.Priority)]
	[Description("The instructions of the attached target from one address forward: 20 by default or ?count=.. " +
				 "from 1 to 1024, with their bytes. The address is an address or Cheat Engine expression such as " +
				 "game.exe+1C0, percent-encoded where it contains /, ?, # or &. Its JSON is the structured result of " +
				 CheatEngineToolNames.CodeDisassemble + "; use that tool to include preceding instructions.")]
	public ReadResourceResult Disassembly(
		[Description("An instruction address or Cheat Engine expression, such as game.exe+1C0.")]
		string address,
		[Description("The number of instructions, 1 to 1024, 20 by default.")]
		string? count = null,
		CancellationToken cancellationToken = default)
	{
		string expression = McpResourceQuery.Required(address, nameof(address));
		int? instructions = McpResourceQuery.Number(count, nameof(count), 1, MaximumInstructions);
		CodeDisassembly disassembly = code.Disassemble(expression, instructions ?? DefaultInstructions,
			cancellationToken: cancellationToken);
		return LiveResourceResults.Json(
			McpResourceQuery.WithQuery(DisassemblyPath + "/" + McpResourceQuery.Segment(expression),
				("count", instructions)),
			JsonSerializer.Serialize(disassembly, CodeJsonContext.Default.CodeDisassembly));
	}
}
