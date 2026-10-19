using System.Globalization;
using System.Text;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Renders only the timer shutdown branch so it can be tested without starting CE.</summary>
internal static class LiveExecutionDriverLua
{
	internal static string RenderPreamble() => "local expirySignaled=false";

	internal static string RenderTimerBody(string stopPath, string targetExitPath, string expiryPath,
		int maximumLifetimeMilliseconds, bool targetFirst)
	{
		string stopGate = targetFirst
			? $"local exited=io.open({Lua(targetExitPath)}, 'r'); if not exited then error('target exit was not confirmed before CE shutdown') end; exited:close()"
			: string.Empty;
		string expiry = targetFirst
			? $"if expired and not expirySignaled then local f=assert(io.open({Lua(expiryPath)}, 'w')); f:write('expired'); f:close(); log('execution-expired'); expirySignaled=true end; if expired then return end"
			: "if expired then log('closing'); timer.Enabled=false; " + RenderClose() + "; return end";
		return $$"""
			local stop=io.open({{Lua(stopPath)}}, 'r')
			if stop then stop:close(); {{stopGate}} log('closing'); timer.Enabled=false; {{RenderClose()}}; return end
			local expired=(getTickCount()-started) % 4294967296 > {{maximumLifetimeMilliseconds}}
			{{expiry}}
			""";
	}

	private static string RenderClose() => """
		log('address-list-clear:start')
		local clearOk,clearError=pcall(function()
		  local list=assert(getAddressList(), 'owned address list is missing')
		  assert(math.type(list.Count)=='integer' and list.Count>=0 and list.Count<=256, 'owned address list exceeded its bound')
		  for index=1,256 do
		    if list.Count==0 then break end
		    local before=list.Count
		    assert(list.getMemoryRecord(0), 'owned record is missing').destroy()
		    assert(list.Count<before, 'owned record deletion made no progress')
		  end
		  assert(list.Count==0, 'owned address list was not cleared')
		end)
		log('address-list-clear:end ok='..tostring(clearOk)..' error='..tostring(clearError))
		log('closeCE:start')
		closeCE()
		log('closeCE:return')
		""";

	private static string Lua(string path) => '"' + string.Concat(Encoding.UTF8.GetBytes(path)
		.Select(value => "\\" + value.ToString("D3", CultureInfo.InvariantCulture))) + '"';
}
