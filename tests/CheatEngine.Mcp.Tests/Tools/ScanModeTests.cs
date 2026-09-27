using CheatEngine.Client;
using CheatEngine.Client.Results;
using CheatEngine.Client.Scanning;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools;

using Xunit.Sdk;

namespace CheatEngine.Mcp.Tests.Tools;

public sealed class ScanModeTests
{
	[Fact]
	public void MemoryScan_UnknownInitialNumericValue_RoutesTypedUnknownRequest()
	{
		ValueScanFirstRequest? captured = null;
		ScanTool tool = CreateTool(first => captured = first, _ =>
		{
		});

		object result = tool.MemoryScan("unknown", comparison: "unknown");

		ToolResultAssert.IsSuccess(result);
		Assert.NotNull(captured);
		Assert.Equal(ValueScanComparison.UnknownInitialValue, captured.Value.Comparison);
		Assert.Equal(ValueScanValueType.Integer32, captured.Value.ValueType);
		Assert.Null(captured.Value.Value);
	}

	[Theory]
	[InlineData("between", "10", "20", (int) ValueScanComparison.Between)]
	[InlineData("greater", "10", null, (int) ValueScanComparison.BiggerThan)]
	[InlineData("less", "10", null, (int) ValueScanComparison.SmallerThan)]
	public void MemoryScan_ValueComparison_RoutesTypedFirstRequest(string comparison, string value, string? upperValue,
		int expectedComparison)
	{
		ValueScanFirstRequest? captured = null;
		ScanTool tool = CreateTool(first => captured = first, _ =>
		{
		});

		object result = tool.MemoryScan("first", "int32", value, comparison, upperValue);

		ToolResultAssert.IsSuccess(result);
		Assert.NotNull(captured);
		Assert.Equal((ValueScanComparison) expectedComparison, captured.Value.Comparison);
		Assert.Equal(value, captured.Value.Value!.Value.Text);
		Assert.Equal(upperValue, captured.Value.UpperValue?.Text);
	}

	[Fact]
	public void MemoryScan_InvalidRange_DoesNotAllocateSession()
	{
		int creations = 0;
		IValueScanner scanner = ClientTestDouble.Create<IValueScanner>((method, _) =>
		{
			if (method.Name == nameof(IValueScanner.CreateSession))
			{
				creations++;
				throw new XunitException("Invalid scan requests must be refused before allocation.");
			}

			throw new NotSupportedException(method.Name);
		});
		ScanTool tool = new(ClientTestDouble.Client((nameof(ICheatEngineClient.ValueScans), scanner)));

		object result = tool.MemoryScan("invalid", "int32", "10", "between");

		ToolResultAssert.IsFailure(result, "upperValue is required for this comparison.");
		Assert.Equal(0, creations);
	}

	[Theory]
	[InlineData("increased", (int) ValueScanComparison.Increased)]
	[InlineData("decreased", (int) ValueScanComparison.Decreased)]
	[InlineData("changed", (int) ValueScanComparison.Changed)]
	[InlineData("unchanged", (int) ValueScanComparison.Unchanged)]
	public void NextMemoryScan_PreviousValueComparison_RoutesTypedNextRequest(string comparison, int expectedComparison)
	{
		ValueScanNextRequest? captured = null;
		ScanTool tool = CreateTool(_ =>
		{
		}, next => captured = next);
		ToolResultAssert.IsSuccess(tool.MemoryScan("next", "int32", "100"));

		object result = tool.NextMemoryScan("next", comparison: comparison);

		ToolResultAssert.IsSuccess(result);
		Assert.NotNull(captured);
		Assert.Equal((ValueScanComparison) expectedComparison, captured.Value.Comparison);
		Assert.Null(captured.Value.Value);
		Assert.Null(captured.Value.UpperValue);
	}

	[Theory]
	[InlineData("between", "10", "20", (int) ValueScanComparison.Between)]
	[InlineData("greater", "10", null, (int) ValueScanComparison.BiggerThan)]
	[InlineData("less", "10", null, (int) ValueScanComparison.SmallerThan)]
	[InlineData("increasedBy", "5", null, (int) ValueScanComparison.IncreasedBy)]
	[InlineData("decreasedBy", "5", null, (int) ValueScanComparison.DecreasedBy)]
	public void NextMemoryScan_ValueComparison_RoutesTypedNextRequest(string comparison, string value,
		string? upperValue,
		int expectedComparison)
	{
		ValueScanNextRequest? captured = null;
		ScanTool tool = CreateTool(_ =>
		{
		}, next => captured = next);
		ToolResultAssert.IsSuccess(tool.MemoryScan("next", "int32", "100"));

		object result = tool.NextMemoryScan("next", value, comparison, upperValue);

		ToolResultAssert.IsSuccess(result);
		Assert.NotNull(captured);
		Assert.Equal((ValueScanComparison) expectedComparison, captured.Value.Comparison);
		Assert.Equal(value, captured.Value.Value!.Value.Text);
		Assert.Equal(upperValue, captured.Value.UpperValue?.Text);
	}

	private static ScanTool CreateTool(Action<ValueScanFirstRequest> first, Action<ValueScanNextRequest> next)
	{
		bool firstComplete = false;
		IValueScanSession session = ClientTestDouble.Create<IValueScanSession>((method, arguments) =>
		{
			switch (method.Name)
			{
				case "get_State":
					return firstComplete ? ValueScanSessionState.ResultsReady : ValueScanSessionState.Created;
				case "FirstScan":
					first((ValueScanFirstRequest) arguments![0]!);
					firstComplete = true;
					return null;
				case "NextScan":
					next((ValueScanNextRequest) arguments![0]!);
					return null;
				case "GetResultCount":
					return 0UL;
				case "Release":
					return new LeaseReleaseOutcome(LeaseReleaseKind.Released, CheatEngineHostEffect.Completed);
				default:
					throw new NotSupportedException(method.Name);
			}
		});
		IValueScanner scanner = ClientTestDouble.Create<IValueScanner>((method, _) =>
			method.Name == nameof(IValueScanner.CreateSession)
				? session
				: throw new NotSupportedException(method.Name));
		return new ScanTool(ClientTestDouble.Client((nameof(ICheatEngineClient.ValueScans), scanner)));
	}
}
