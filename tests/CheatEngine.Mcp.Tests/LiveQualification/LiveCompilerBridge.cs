using System.Security.Cryptography;
using System.Text;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Reviewed fixed autorun bridge for native compiler qualification; it accepts no caller source or paths.</summary>
internal static class LiveCompilerBridge
{
	internal static readonly string Template = """
		local compilerReceipts = 0
		local originalCompileCS = compileCS
		if type(originalCompileCS) == 'function' then
		  compileCS = function(...)
		    compilerReceipts = compilerReceipts + 1
		    return originalCompileCS(...)
		  end
		end
		local function compilerProbe()
		  local request=io.open(__REQUEST__,'r')
		  if not request then return end
		  local command=request:read('*a'); request:close(); os.remove(__REQUEST__)
		  if command~='status' and command~='compile' then return end
		  local available=type(compileCS)=='function'
		  local value=''
		  if command=='compile' and available then
		    local called,filename,message=pcall(compileCS,__SOURCE__,{},nil)
		    if called and type(filename)=='string' and #filename>0 then value=filename
		    else available=false; value=tostring(called and message or filename) end
		  end
		  local response=assert(io.open(__RESPONSE__..'.tmp','w'))
		  response:write('ok\n'..tostring(available)..'\n'..tostring(compilerReceipts)..'\n'..value); response:close()
		  os.rename(__RESPONSE__..'.tmp',__RESPONSE__)
		end
		""".ReplaceLineEndings("\n").TrimEnd('\n');
	internal const string TemplateSha256 = "A5B0280CE702D720CD496E5700CE30AD92FFD362EB825A60265A02B6682DA7F2";

	internal static void RequireReviewedHash()
	{
		string actual = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Template)));
		if (!string.Equals(actual, TemplateSha256, StringComparison.Ordinal))
		{
			throw new InvalidOperationException("The fixed compiler bridge template differs from its reviewed hash.");
		}
	}

	internal static string Render(string requestPath, string responsePath, string source, Func<string, string> quote)
	{
		ArgumentNullException.ThrowIfNull(quote);
		return Template.Replace("__REQUEST__", quote(requestPath), StringComparison.Ordinal)
			.Replace("__RESPONSE__", quote(responsePath), StringComparison.Ordinal)
			.Replace("__SOURCE__", quote(source), StringComparison.Ordinal);
	}
}
