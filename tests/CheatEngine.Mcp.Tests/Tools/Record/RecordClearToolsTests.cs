using System.Text.Json;

using CheatEngine.Mcp.Tests.Tools.Memory;
using CheatEngine.Mcp.Tools.Record;

namespace CheatEngine.Mcp.Tests.Tools.Record;

/// <summary>The complete address-list clear: its fixed Lua walk counts child records before the single clear operation.</summary>
public sealed class RecordClearToolsTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public void Clear_NestedRecords_UsesTheBoundedWalkAndReturnsTheCompleteCount()
	{
		TargetDouble target = new();
		string? source = null;
		target.LuaResult = script =>
		{
			source = script;
			return new RecordClearResult(5);
		};

		RecordClearResult result = new RecordClearTools(target.Dispatch).Clear(Token);

		Assert.Equal(new RecordClearResult(5), result);
		Assert.Contains("for child = 0, record.Count - 1 do", source, StringComparison.Ordinal);
		Assert.Contains("enqueue(record.Child[child])", source, StringComparison.Ordinal);
		// Cheat Engine indexes nested records in the list too, so each record is queued once by its id.
		Assert.Contains("if record ~= nil and not queued[record.ID] then", source, StringComparison.Ordinal);
		Assert.Contains("return {deleted = last}", source, StringComparison.Ordinal);
		Assert.Equal(1, target.LuaCalls);
	}

	[Fact]
	public void ClearResult_UsesTheGeneratedJsonSchema()
	{
		string json = JsonSerializer.Serialize(new RecordClearResult(5), RecordJsonContext.Default.RecordClearResult);

		Assert.Equal("{\"deleted\":5}", json);
	}
}
