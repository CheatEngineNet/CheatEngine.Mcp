using System.ComponentModel;
using System.Reflection;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Asm;

namespace CheatEngine.Mcp.Tests.Tools.Asm;

/// <summary>
///     <c>asm_generate_api_hook</c> arguments: <c>extension</c> is a label suffix, so it must be a valid part of an
///     Auto Assembler name, and the fixed script receives every argument unchanged.
/// </summary>
public sealed class AsmApiHookTests
{
	private const string Generated = "[ENABLE]\nalloc(originalcall,1024)\n\n[DISABLE]\ndealloc(originalcall)\n";

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Theory]
	[InlineData("a b")]
	[InlineData("x-1")]
	[InlineData("hook)")]
	[InlineData("é")]
	public void GenerateApiHook_ExtensionThatIsNotALabelSuffix_IsRefusedBeforeDispatch(string extension)
	{
		StateTestHarness harness = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new AsmTools(harness.Dispatch, harness.Resources).GenerateApiHook("401000", "401005",
				extension: extension, cancellationToken: Token));

		Assert.Equal((ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal("extension", exception.Error.Details!.Value.GetProperty("parameter").GetString());
		Assert.Equal(0, harness.Dispatches);
	}

	[Fact]
	public void GenerateApiHook_ExtensionOver64Characters_IsRefused()
	{
		StateTestHarness harness = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new AsmTools(harness.Dispatch, harness.Resources).GenerateApiHook("401000", "401005",
				extension: new string('x', AsmTools.MaximumHookExtensionLength + 1), cancellationToken: Token));

		Assert.Equal(ToolErrorKind.InvalidArgument, exception.Error.Kind);
		Assert.Equal(0, harness.Dispatches);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("_hook2")]
	public void GenerateApiHook_ValidExtension_ReachesTheFixedScriptWithEveryArgument(string? extension)
	{
		StateTestHarness harness = new();
		string? source = null;
		harness.Answer<AsmLuaScript>(value =>
		{
			source = value;
			return new AsmLuaScript(Generated);
		});

		AsmGeneratedScript result = new AsmTools(harness.Dispatch, harness.Resources).GenerateApiHook("game.exe+10",
			"myHook", "originalPointer", extension, true, Token);

		Assert.Equal(new AsmGeneratedScript(Generated, string.Empty), result);
		Assert.Contains("[1] = \"game.exe+10\"", source, StringComparison.Ordinal);
		Assert.Contains("[2] = \"myHook\"", source, StringComparison.Ordinal);
		Assert.Contains("[3] = \"originalPointer\"", source, StringComparison.Ordinal);
		Assert.Contains(extension is null ? "[4] = nil" : $"[4] = \"{extension}\"", source, StringComparison.Ordinal);
		Assert.Contains("[5] = true", source, StringComparison.Ordinal);
	}

	[Fact]
	public void GenerateApiHook_ParameterDescriptions_SayWhatCheatEngineDoesWithThem()
	{
		ParameterInfo[] parameters = typeof(AsmTools).GetMethod(nameof(AsmTools.GenerateApiHook))!.GetParameters();

		string extension = Describe(parameters, "extension");
		string newCallAddress = Describe(parameters, "newCallAddress");

		Assert.Contains("suffix Cheat Engine appends to the script's label and allocation names", extension,
			StringComparison.Ordinal);
		Assert.DoesNotContain("architecture", extension, StringComparison.Ordinal);
		Assert.Contains("stores the address of originalcall", newCallAddress, StringComparison.Ordinal);
	}

	private static string Describe(ParameterInfo[] parameters, string name)
	{
		return parameters.Single(parameter => parameter.Name == name).GetCustomAttribute<DescriptionAttribute>()!
			.Description;
	}
}
