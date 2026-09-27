using System.ComponentModel;
using System.Globalization;

using CheatEngine.Mcp.Core.Contract;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools.Structures;

/// <summary>Adds, updates and removes the elements of a structure in batches.</summary>
[McpServerToolType]
public sealed class StructureElementTools
{
	internal const int MaxBatch = 256;

	private readonly ToolDispatch _dispatch;

	/// <summary>Creates the tools; nothing is dispatched until a call.</summary>
	/// <param name="dispatch">The activation's dispatch facade.</param>
	public StructureElementTools(ToolDispatch dispatch)
	{
		ArgumentNullException.ThrowIfNull(dispatch);
		_dispatch = dispatch;
	}

	/// <summary>Adds elements to a structure, all or nothing.</summary>
	[McpServerTool(Name = CheatEngineToolNames.StructureAddElements, Title = "Add structure elements",
		ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Adds elements to a structure, all or nothing: when Cheat Engine refuses one, the elements this call added are " +
		"removed again. Cheat Engine orders elements by offset, so indices returns where each added element landed.")]
	public StructureChange AddElements(
		[Description("The structure's case-sensitive name.")]
		string name,
		[Description("The elements to add (1-256); offsets are signed hexadecimal.")]
		StructureElementSpec[] elements,
		CancellationToken cancellationToken = default)
	{
		string structure = StructureArguments.Name(name, "name");
		object?[][] specs = StructureArguments.Specs(StructureArguments.Batch(elements, "elements", 1, MaxBatch),
			"elements");
		StructureLuaBatch batch = _dispatch.RunLua(CheatEngineToolNames.StructureAddElements,
			StructureLuaScripts.AddElements, StructureLuaJsonContext.Default.StructureLuaBatch, cancellationToken,
			structure, specs);
		return Change(batch);
	}

	/// <summary>Updates structure elements in order until the first refusal.</summary>
	[McpServerTool(Name = CheatEngineToolNames.StructureUpdateElements, Title = "Update structure elements",
		ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Renames, retypes or moves elements. Every update names an element by its index before this call; all are " +
		"checked first, then applied in order. The first refusal stops the batch with partial_effect (details.applied, " +
		"details.failedIndex); read the structure with structure_get before retrying the rest.")]
	public StructureChange UpdateElements(
		[Description("The structure's case-sensitive name.")]
		string name,
		[Description("The updates (1-256); each sets at least one field.")]
		StructureElementUpdate[] updates,
		CancellationToken cancellationToken = default)
	{
		string structure = StructureArguments.Name(name, "name");
		object?[][] encoded = StructureArguments.Updates(StructureArguments.Batch(updates, "updates", 1, MaxBatch),
			"updates");
		StructureLuaBatch batch = _dispatch.RunLua(CheatEngineToolNames.StructureUpdateElements,
			StructureLuaScripts.UpdateElements, StructureLuaJsonContext.Default.StructureLuaBatch, cancellationToken,
			structure, encoded);
		if (batch.FailedIndex is { } failed)
		{
			throw Partial(batch, failed, $"updates[{failed.ToString(CultureInfo.InvariantCulture)}]");
		}

		return Change(batch);
	}

	/// <summary>Removes structure elements by index.</summary>
	[McpServerTool(Name = CheatEngineToolNames.StructureRemoveElements, Title = "Remove structure elements",
		ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Removes elements by their current indices, highest first so the others keep their index. Every index is " +
		"checked first; a refusal stops the batch with partial_effect. indices returns the removed indices.")]
	public StructureChange RemoveElements(
		[Description("The structure's case-sensitive name.")]
		string name,
		[Description("The distinct zero-based indices to remove (1-256), from structure_get.")]
		int[] indices,
		CancellationToken cancellationToken = default)
	{
		string structure = StructureArguments.Name(name, "name");
		int[] requested = StructureArguments.Batch(indices, "indices", 1, MaxBatch);
		int[] ordered = StructureArguments.Indices(requested, "indices");
		StructureLuaBatch batch = _dispatch.RunLua(CheatEngineToolNames.StructureRemoveElements,
			StructureLuaScripts.RemoveElements, StructureLuaJsonContext.Default.StructureLuaBatch, cancellationToken,
			structure, ordered);
		if (batch.FailedIndex is { } failed)
		{
			// Report the refused element's position in the caller's list, not in the descending removal order.
			int position = Array.IndexOf(requested, ordered[failed]);
			throw Partial(batch, position,
				$"indices[{position.ToString(CultureInfo.InvariantCulture)}]");
		}

		return Change(batch);
	}

	private static StructureChange Change(StructureLuaBatch batch)
	{
		return new StructureChange(batch.Name, batch.Size, batch.ElementCount, batch.Indices);
	}

	private static CheatEngineToolException Partial(StructureLuaBatch batch, int failedIndex, string item)
	{
		string failure = batch.Failure ?? "Cheat Engine refused the change.";
		return CheatEngineToolException.PartialEffect(
			$"{batch.Applied.ToString(CultureInfo.InvariantCulture)} change(s) applied; Cheat Engine refused {item}: {failure}",
			batch.Applied > 0 ? ToolHostEffect.Started : ToolHostEffect.Unknown,
			new StructureBatchFailure(batch.Applied, failedIndex, failure),
			StructuresJsonContext.Default.StructureBatchFailure, false,
			"Read the structure with structure_get before repeating the remaining changes.");
	}
}
