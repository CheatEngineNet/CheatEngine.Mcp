namespace CheatEngine.Mcp.Tests.Hosting;

public sealed class GatewayOptionsTests
{
	private static readonly string Directory = Path.Combine(Path.GetTempPath(), "CheatEngine.Mcp.OptionsTests");

	[Fact]
	public void Resolve_NoArgumentOrVariable_UsesTheDefaults()
	{
		GatewayOptions options = GatewayOptions.Resolve([], static _ => null);

		Assert.Equal(TimeSpan.FromSeconds(45), options.CallTimeout);
		Assert.Equal(InstanceRegistry.DefaultDirectory, options.InstanceDirectory);
	}

	[Fact]
	public void Resolve_EnvironmentVariable_SetsTheCallTimeout()
	{
		GatewayOptions options = GatewayOptions.Resolve([], Environment(("MCP_GATEWAY_CALL_TIMEOUT_SECONDS", "120")));

		Assert.Equal(TimeSpan.FromSeconds(120), options.CallTimeout);
	}

	[Fact]
	public void Resolve_ArgumentAndVariable_ArgumentWins()
	{
		GatewayOptions options = GatewayOptions.Resolve(
			["--call-timeout-seconds", "5", "--instance-directory", Directory],
			Environment(("MCP_GATEWAY_CALL_TIMEOUT_SECONDS", "3600"), ("MCP_INSTANCE_DIRECTORY", @"C:\elsewhere")));

		Assert.Equal(TimeSpan.FromSeconds(5), options.CallTimeout);
		Assert.Equal(Directory, options.InstanceDirectory);
	}

	[Theory]
	[InlineData("3601")]
	[InlineData("4")]
	[InlineData("0")]
	[InlineData("-10")]
	[InlineData("abc")]
	[InlineData("45.5")]
	[InlineData(" 45")]
	[InlineData("1e2")]
	[InlineData("")]
	public void Resolve_OutOfRangeOrMalformedArgument_FailsAtStartup(string value)
	{
		ArgumentException failure = Assert.Throws<ArgumentException>(() =>
			GatewayOptions.Resolve(["--call-timeout-seconds", value], static _ => null));

		Assert.Contains("--call-timeout-seconds must be a whole number of seconds from 5 to 3600", failure.Message,
			StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("3601")]
	[InlineData("4")]
	[InlineData("ten")]
	public void Resolve_InvalidEnvironmentVariable_FailsAtStartup(string value)
	{
		ArgumentException failure = Assert.Throws<ArgumentException>(() =>
			GatewayOptions.Resolve([], Environment(("MCP_GATEWAY_CALL_TIMEOUT_SECONDS", value))));

		Assert.Contains("MCP_GATEWAY_CALL_TIMEOUT_SECONDS must be a whole number of seconds", failure.Message,
			StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("--call-timeout-seconds")]
	[InlineData("--instance-directory")]
	public void Resolve_ArgumentWithoutValue_FailsAtStartup(string argument)
	{
		Assert.Throws<ArgumentException>(() => GatewayOptions.Resolve([argument], static _ => null));
	}

	[Fact]
	public void Resolve_UnknownArgumentOrRelativeDirectory_FailsAtStartup()
	{
		Assert.Throws<ArgumentException>(() => GatewayOptions.Resolve(["--timeout", "45"], static _ => null));
		Assert.Throws<ArgumentException>(() =>
			GatewayOptions.Resolve(["--instance-directory", "relative"], static _ => null));
		Assert.Throws<ArgumentException>(() =>
			GatewayOptions.Resolve([], Environment(("MCP_INSTANCE_DIRECTORY", "relative"))));
	}

	[Theory]
	[InlineData("")]
	[InlineData(" ")]
	public void Resolve_EmptyInstanceDirectoryOverride_FailsAtStartup(string directory)
	{
		Assert.Throws<ArgumentException>(() =>
			GatewayOptions.Resolve(["--instance-directory", directory], static _ => null));
		Assert.Throws<ArgumentException>(() =>
			GatewayOptions.Resolve([], Environment(("MCP_INSTANCE_DIRECTORY", directory))));
	}

	private static Func<string, string?> Environment(params (string Name, string Value)[] variables)
	{
		return name => variables.FirstOrDefault(variable => variable.Name == name).Value;
	}
}
