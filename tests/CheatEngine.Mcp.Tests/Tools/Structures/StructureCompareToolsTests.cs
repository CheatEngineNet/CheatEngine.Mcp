using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Tests.Tools.Structures;

/// <summary>Raw structure comparisons validate that each requested byte belongs to a complete comparison cell.</summary>
public sealed class StructureCompareToolsTests
{
	[Fact]
	public void Compare_RawSizeWithTrailingBytes_IsRefusedWithoutDispatch()
	{
		StructureToolHarness harness = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			harness.Comparisons.Compare(["1000"], ["2000"], size: 5, granularity: 4,
				cancellationToken: TestContext.Current.CancellationToken));

		Assert.Equal((ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal("size", exception.Error.Details!.Value.GetProperty("parameter").GetString());
		Assert.Equal(0, harness.Dispatcher.Calls);
	}
}
