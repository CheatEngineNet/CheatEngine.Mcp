using CheatEngine.Mcp.Tools.Structures;

namespace CheatEngine.Mcp.Tests.Tools.Structures;

/// <summary>Protocol guards for fixed Lua structure scripts whose failures occur after host mutations.</summary>
public sealed class StructureLuaScriptsTests
{
	public static TheoryData<string, string> MutatingBatches => new()
	{
		{ nameof(StructureLuaScripts.AddElements), StructureLuaScripts.AddElements },
		{ nameof(StructureLuaScripts.UpdateElements), StructureLuaScripts.UpdateElements },
		{ nameof(StructureLuaScripts.RemoveElements), StructureLuaScripts.RemoveElements }
	};

	[Theory]
	[MemberData(nameof(MutatingBatches))]
	public void MutatingBatch_EndUpdateRefusal_IsDeclaredAsUnknownPartialEffect(string name, string script)
	{
		const string close = "local closed, closeFailure = pcall(function() s.endUpdate() end)";
		const string error = "return mcp.err('partial_effect'";
		int closeIndex = script.IndexOf(close, StringComparison.Ordinal);
		int branchIndex = closeIndex < 0
			? -1
			: script.IndexOf("if not closed then", closeIndex, StringComparison.Ordinal);
		int errorIndex = branchIndex < 0 ? -1 : script.IndexOf(error, branchIndex, StringComparison.Ordinal);
		int successIndex = script.LastIndexOf("return ", StringComparison.Ordinal);

		Assert.True(closeIndex >= 0, $"{name} must capture an endUpdate refusal.");
		Assert.True(branchIndex > closeIndex, $"{name} must branch on an endUpdate refusal.");
		Assert.True(errorIndex > branchIndex, $"{name} must declare partial_effect when endUpdate fails.");
		Assert.True(successIndex > errorIndex, $"{name} must return the declared error before its normal result.");
		Assert.Contains("tostring(closeFailure), 'unknown'", script[errorIndex..successIndex],
			StringComparison.Ordinal);
	}

	[Fact]
	public void List_InternalStructures_AreExcludedBeforeFilteringAndPaging()
	{
		string script = StructureLuaScripts.List;
		int internalRead = script.IndexOf("local readable, internal = pcall(function() return s.Internal end)",
			StringComparison.Ordinal);
		int filter = script.IndexOf("if visible and (needle == nil", StringComparison.Ordinal);
		int total = script.IndexOf("total = total + 1", StringComparison.Ordinal);

		Assert.True(internalRead >= 0, "structure_list must read the host's Internal flag.");
		Assert.Contains("local visible = not readable or internal ~= true", script, StringComparison.Ordinal);
		Assert.True(filter > internalRead,
			"structure_list must reject internal structures before applying its name filter.");
		Assert.True(total > filter, "structure_list must exclude internal structures from its totals and pages.");
	}
}
