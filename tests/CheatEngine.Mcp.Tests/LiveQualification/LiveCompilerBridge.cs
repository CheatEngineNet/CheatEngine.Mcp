using System.Security.Cryptography;
using System.Text;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Reviewed fixed autorun bridge for native compiler qualification; it accepts no caller source or paths.</summary>
internal static class LiveCompilerBridge
{
	internal static readonly string Template = """
		local compilerReceipts = 0
		local holdArmed = false
		local originalCompileCS = compileCS
		if type(originalCompileCS) == 'function' then
		  compileCS = function(...)
		    compilerReceipts = compilerReceipts + 1
		    local results = table.pack(originalCompileCS(...))
		    if holdArmed and type(results[1]) == 'string' and #results[1] > 0 then
		      holdArmed = false
		      local ready = assert(io.open(__HOLD_READY__..'.tmp', 'w'))
		      ready:write(results[1]); ready:close()
		      assert(os.rename(__HOLD_READY__..'.tmp', __HOLD_READY__), 'compiler hold receipt publish failed')
		      local started = getTickCount()
		      local release = io.open(__HOLD_RELEASE__, 'r')
		      while not release do
		        if getTickCount() - started > 10000 then
		          return nil, 'qualification compiler hold timed out'
		        end
		        sleep(25)
		        release = io.open(__HOLD_RELEASE__, 'r')
		      end
		      release:close()
		    end
		    return table.unpack(results, 1, results.n)
		  end
		end
		local function compilerProbe()
		  local request=io.open(__REQUEST__,'r')
		  if not request then return end
		  local command=request:read('*a'); request:close(); assert(os.remove(__REQUEST__), 'compiler request dequeue failed')
		  if command~='status' and command~='compile' and command~='armHold' then return end
		  local available=type(compileCS)=='function'
		  local value=''
		  if command=='armHold' then
		    holdArmed=available
		  elseif command=='compile' and available then
		    local called,filename,message=pcall(compileCS,__SOURCE__,{},nil)
		    if called and type(filename)=='string' and #filename>0 then value=filename
		    else available=false; value=tostring(called and message or filename) end
		  end
		  local response=assert(io.open(__RESPONSE__..'.tmp','w'))
		  response:write('ok\n'..tostring(available)..'\n'..tostring(compilerReceipts)..'\n'..value); response:close()
		  os.rename(__RESPONSE__..'.tmp',__RESPONSE__)
		end
		""".ReplaceLineEndings("\n").TrimEnd('\n');
	internal const string TemplateSha256 = "770C3777E8E265C6C5D6E14E87CF0F94917505BB514720A0CE412B98CD2C55F5";

	internal static void RequireReviewedHash()
	{
		string actual = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Template)));
		if (!string.Equals(actual, TemplateSha256, StringComparison.Ordinal))
		{
			throw new InvalidOperationException("The fixed compiler bridge template differs from its reviewed hash.");
		}
	}

	internal static string Render(string requestPath, string responsePath, string source, Func<string, string> quote) =>
		Render(requestPath, responsePath, responsePath + ".hold-ready", responsePath + ".hold-release", source, quote);

	/// <summary>Renders only reviewed paths and source supplied by the private live-test driver.</summary>
	internal static string Render(string requestPath, string responsePath, string holdReadyPath, string holdReleasePath,
		string source, Func<string, string> quote)
	{
		ArgumentNullException.ThrowIfNull(quote);
		return Template.Replace("__REQUEST__", quote(requestPath), StringComparison.Ordinal)
			.Replace("__RESPONSE__", quote(responsePath), StringComparison.Ordinal)
			.Replace("__HOLD_READY__", quote(holdReadyPath), StringComparison.Ordinal)
			.Replace("__HOLD_RELEASE__", quote(holdReleasePath), StringComparison.Ordinal)
			.Replace("__SOURCE__", quote(source), StringComparison.Ordinal);
	}
}
