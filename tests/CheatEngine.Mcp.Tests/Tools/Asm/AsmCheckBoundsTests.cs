using System.ComponentModel;
using System.Reflection;
using System.Text;

using CheatEngine.Client.Assembly;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Asm;

namespace CheatEngine.Mcp.Tests.Tools.Asm;

/// <summary>
///     The size bounds of <c>asm_check</c>: its DISABLE check passes the script to fixed Lua, whose string arguments
///     are bounded by their UTF-8 size, so a larger script is refused before Cheat Engine checks the ENABLE section.
/// </summary>
public sealed class AsmCheckBoundsTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public void Check_ScriptOverTheUtf8Bound_RefusesBeforeTheEnableCheck()
	{
		StateTestHarness harness = new();
		int checks = 0;
		AsmTools tools = new(harness.Dispatch, harness.Resources, CountingAutoAssembler(() => checks++));
		// About 400000 characters, well under the character bound, but about 1200000 bytes as UTF-8.
		string script = "[ENABLE]\n//" + new string('中', 400_000) + "\n[DISABLE]\n";
		Assert.True(script.Length < AsmTools.MaximumScriptLength);

		CheatEngineToolException exception =
			Assert.Throws<CheatEngineToolException>(() => tools.Check(script, cancellationToken: Token));

		Assert.Equal((ToolErrorKind.LimitExceeded, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal("script", exception.Error.Details!.Value.GetProperty("parameter").GetString());
		Assert.Equal(0, checks);
		Assert.Equal(0, harness.Dispatches);
		Assert.Empty(harness.LuaCalls);
	}

	[Fact]
	public void Check_ScriptAtTheUtf8Bound_ReachesTheEnableCheck()
	{
		StateTestHarness harness = new();
		int checks = 0;
		AsmTools tools = new(harness.Dispatch, harness.Resources, CountingAutoAssembler(() => checks++));
		const string head = "[ENABLE]\n//";
		const string tail = "\n[DISABLE]\n";
		string script = head + new string('x', AsmTools.MaximumCheckedScriptBytes - head.Length - tail.Length) + tail;
		Assert.Equal(AsmTools.MaximumCheckedScriptBytes, Encoding.UTF8.GetByteCount(script));

		AsmCheckResult result = tools.Check(script, cancellationToken: Token);

		Assert.Equal((false, AsmScriptSection.Enable), (result.Accepted, result.FailedSection));
		Assert.Equal(1, checks);
	}

	[Fact]
	public void Check_ScriptParameter_StatesTheUtf8Bound()
	{
		string description = typeof(AsmTools).GetMethod(nameof(AsmTools.Check))!.GetParameters()
			.Single(static parameter => parameter.Name == "script")
			.GetCustomAttribute<DescriptionAttribute>()!.Description;

		Assert.Contains("1048576 characters and 1048576 UTF-8 bytes", description, StringComparison.Ordinal);
	}

	/// <summary>An Auto Assembler Client that rejects every ENABLE section and counts the checks.</summary>
	private static IAutoAssemblerClient CountingAutoAssembler(Action checkedOnce)
	{
		return ClientTestDouble.Create<IAutoAssemblerClient>((method, _) =>
		{
			if (method.Name != nameof(IAutoAssemblerClient.Check))
			{
				throw new NotSupportedException(method.Name);
			}

			checkedOnce();
			return new AutoAssemblerCheckResult(false, "Error in line 2", false);
		});
	}
}
