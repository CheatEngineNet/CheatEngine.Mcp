using System.ComponentModel;
using System.Text.Json;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tools.Asm;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Resources.Live;

/// <summary>The read-only Auto Assembler projection of one Cheat Engine instance: the retained patches.</summary>
[McpServerResourceType]
public sealed class AsmLiveResources(AsmTools asm)
{
	private const string PatchesPath = McpResourceUris.InstancePrefix + "patches";

	/// <summary>Reads the Auto Assembler patches this activation retains.</summary>
	/// <returns>The structured result of <c>asm_list_patches</c>.</returns>
	[McpServerResource(UriTemplate = PatchesPath, Name = "instance_patches", Title = "Auto Assembler patches",
		MimeType = McpResourceUris.JsonMimeType)]
	[McpSourceTool(typeof(AsmTools), CheatEngineToolNames.AsmListPatches)]
	[McpResourceAnnotations(Role.Assistant, Priority = LiveResourceResults.Priority)]
	[Description("The Auto Assembler patches this activation applied and still retains, which block a target change " +
				 "until released; read without any Cheat Engine call. Its JSON is the structured result of " +
				 CheatEngineToolNames.AsmListPatches + ".")]
	public ReadResourceResult Patches()
	{
		return LiveResourceResults.Json(PatchesPath,
			JsonSerializer.Serialize(asm.ListPatches(), AsmJsonContext.Default.AsmPatchList));
	}
}
