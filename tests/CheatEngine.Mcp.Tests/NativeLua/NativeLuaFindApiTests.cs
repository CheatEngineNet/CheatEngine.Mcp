using CheatEngine.Mcp.Core.Lua;
using CheatEngine.Mcp.Tools.Lua;

namespace CheatEngine.Mcp.Tests.NativeLua;

public sealed unsafe partial class NativeLuaToolRuntimeTests
{
	[Fact]
	public void LuaFindApi_OverlongStreamingLine_ReturnsTheClosedPartialPage()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
		             local reads = 0
		             getCheatEngineDir = function() return 'C:\\CheatEngine' end
		             io.open = function()
		             	return {
		             		read = function(_, count)
		             			assert(count == 4096)
		             			reads = reads + 1
		             			return reads <= 3 and string.rep('x', count) or nil
		             		end,
		             		close = function() closes = (closes or 0) + 1 return true end
		             	}
		             end
		             """);

		LuaJsonResult<LuaApiSearchResult> copied = ReadJson(
			LuaToolRuntime.BuildSource(LuaScripts.FindApi, 100, ["needle", 20, 0]),
			LuaJsonContext.Default.LuaApiSearchResult);

		Assert.False(copied.IsError, copied.Error?.Message);
		LuaApiSearchResult page = copied.Value!;
		Assert.Equal((0, true), (page.Total, page.Truncated));
		Assert.Null(page.NextOffset);
		Assert.Empty(page.Lines);
		Assert.Equal(1L, ReadGlobal("closes"));
	}

	[Fact]
	public void LuaFindApi_SourceBound_ReturnsTheClosedPartialPage()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
		             local reads = 0
		             getCheatEngineDir = function() return 'C:\\CheatEngine' end
		             io.open = function(path, mode)
		             	assert(path == 'C:\\CheatEngine\\celua.txt')
		             	assert(mode == 'rb')
		             	return {
		             		read = function()
		             			reads = reads + 1
		             			return reads <= 129 and string.rep('needle\n', 512) or nil
		             		end,
		             		close = function() closes = (closes or 0) + 1 return true end
		             	}
		             end
		             """);

		LuaJsonResult<LuaApiSearchResult> copied = ReadJson(
			LuaToolRuntime.BuildSource(LuaScripts.FindApi, 100, ["needle", 100, 0]),
			LuaJsonContext.Default.LuaApiSearchResult);

		Assert.False(copied.IsError, copied.Error?.Message);
		LuaApiSearchResult page = copied.Value!;
		Assert.Equal(65536, page.Total);
		Assert.True(page.Truncated);
		Assert.Null(page.NextOffset);
		Assert.Equal(100, page.Lines.Length);
		Assert.Equal((1, 100), (page.Lines[0].Number, page.Lines[^1].Number));
		Assert.Equal(1L, ReadGlobal("closes"));
	}

	[Fact]
	public void LuaFindApi_ReadFailure_ClosesTheFileAndReportsTheCompletedRead()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
		             getCheatEngineDir = function() return 'C:\\CheatEngine' end
		             io.open = function()
		             	return {
		             		read = function() return nil, 'simulated I/O error' end,
		             		close = function() closes = (closes or 0) + 1 return true end
		             	}
		             end
		             """);

		LuaJsonResult<LuaApiSearchResult> copied = ReadJson(
			LuaToolRuntime.BuildSource(LuaScripts.FindApi, 100, ["needle", 20, 0]),
			LuaJsonContext.Default.LuaApiSearchResult);

		Assert.True(copied.IsError);
		Assert.Equal(("host_refused", "completed"), (copied.Error!.Kind, copied.Error.HostEffect));
		Assert.Equal(1L, ReadGlobal("closes"));
	}

	[Fact]
	public void LuaFindApi_CloseFailure_DoesNotReportACompletedPage()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
		             getCheatEngineDir = function() return 'C:\\CheatEngine' end
		             io.open = function()
		             	return {
		             		read = function() return nil end,
		             		close = function() closes = (closes or 0) + 1 return nil, 'simulated close failure' end
		             	}
		             end
		             """);

		LuaJsonResult<LuaApiSearchResult> copied = ReadJson(
			LuaToolRuntime.BuildSource(LuaScripts.FindApi, 100, ["needle", 20, 0]),
			LuaJsonContext.Default.LuaApiSearchResult);

		Assert.True(copied.IsError);
		Assert.Equal(("partial_effect", "cleanup_unconfirmed"), (copied.Error!.Kind, copied.Error.HostEffect));
		Assert.Equal(1L, ReadGlobal("closes"));
	}
}
