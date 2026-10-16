using CheatEngine.Client;
using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Inspection;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Exec;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Mcp.Tests.Tools.Exec;

/// <summary>Execution inputs preserve native numeric types and fail before any target effect.</summary>
public sealed class ExecSupportTests
{
	[Fact]
	public void Arguments_KnownNumericBoundaries_PreserveExactValuesAndNativeTypes()
	{
		(int[] types, object?[] values) = ExecSupport.Arguments(
		[
			new(ExecArgumentType.Integral, "-9223372036854775808"),
			new(ExecArgumentType.Integral, "18446744073709551615"),
			new(ExecArgumentType.Integral, " 0X8000000000000000 "),
			new(ExecArgumentType.Integral, "FFFFFFFFFFFFFFFF"),
			new(ExecArgumentType.SinglePrecision, "1.25"),
			new(ExecArgumentType.DoublePrecision, "-2.5e10")
		]);

		Assert.Equal([0, 0, 0, 0, 1, 2], types);
		Assert.Equal(long.MinValue, Assert.IsType<long>(values[0]));
		Assert.Equal(ulong.MaxValue, Assert.IsType<ulong>(values[1]));
		Assert.Equal(0x8000000000000000UL, Assert.IsType<ulong>(values[2]));
		Assert.Equal(ulong.MaxValue, Assert.IsType<ulong>(values[3]));
		Assert.Equal(1.25f, Assert.IsType<float>(values[4]));
		Assert.Equal(-25_000_000_000d, Assert.IsType<double>(values[5]));
	}

	[Fact]
	public void Arguments_EmptyList_ProducesEmptyParallelNativeArrays()
	{
		(int[] types, object?[] values) = ExecSupport.Arguments([]);

		Assert.Empty(types);
		Assert.Empty(values);
	}

	[Fact]
	public void Arguments_NullListOrEntry_ReportsTheOriginalArgumentIndex()
	{
		AssertInvalid(() => ExecSupport.Arguments(null), "arguments:");
		AssertInvalid(() => ExecSupport.Arguments([new(ExecArgumentType.Integral, "1"), null!]), "arguments[1]:");
	}

	[Theory]
	[InlineData("missing")]
	[InlineData("too_long")]
	public void Arguments_MissingOrOversizedText_ReportsIndexedValue(string caseId)
	{
		string text = caseId == "missing" ? null! : new string(' ', ExecSupport.MaximumArgumentText) + "1";

		AssertInvalid(() => ExecSupport.Arguments([new(ExecArgumentType.Integral, text)]), "arguments[0].value:");
	}

	[Fact]
	public void Arguments_ExactMaximumTextLength_AcceptsAndPreservesTheParsedValue()
	{
		string text = new string(' ', ExecSupport.MaximumArgumentText - 1) + "1";

		(int[] types, object?[] values) = ExecSupport.Arguments([new(ExecArgumentType.Integral, text)]);

		Assert.Equal([0], types);
		Assert.Equal(1L, Assert.IsType<long>(Assert.Single(values)));
	}

	[Theory]
	[InlineData("empty")]
	[InlineData("prefix_only")]
	[InlineData("non_hex")]
	[InlineData("decimal_overflow")]
	[InlineData("hex_overflow")]
	public void Arguments_InvalidIntegerPartition_RefusesWithoutNarrowing(string caseId)
	{
		string value = caseId switch
		{
			"empty" => string.Empty,
			"prefix_only" => "0x",
			"non_hex" => "7FFG",
			"decimal_overflow" => "18446744073709551616",
			_ => "0x10000000000000000"
		};

		AssertInvalid(() => ExecSupport.Arguments([new(ExecArgumentType.Integral, value)]), "arguments[0].value:");
	}

	[Theory]
	[InlineData(ExecArgumentType.SinglePrecision, "NaN")]
	[InlineData(ExecArgumentType.SinglePrecision, "Infinity")]
	[InlineData(ExecArgumentType.SinglePrecision, "1e1000")]
	[InlineData(ExecArgumentType.DoublePrecision, "NaN")]
	[InlineData(ExecArgumentType.DoublePrecision, "-Infinity")]
	[InlineData(ExecArgumentType.DoublePrecision, "1e1000")]
	public void Arguments_NonFiniteFloatingPoint_RefusesBeforeInvocation(ExecArgumentType type, string value)
	{
		AssertInvalid(() => ExecSupport.Arguments([new(type, value)]), "arguments[0].value:");
	}

	[Theory]
	[InlineData(1)]
	[InlineData(10_000)]
	public void Timeout_ExactSupportedEndpoints_AreAccepted(int milliseconds)
	{
		Assert.Null(Xunit.Record.Exception(() => ExecSupport.Timeout(milliseconds)));
	}

	[Theory]
	[InlineData(0)]
	[InlineData(10_001)]
	public void Timeout_AdjacentUnsupportedValues_RefuseBeforeInvocation(int milliseconds)
	{
		AssertInvalid(() => ExecSupport.Timeout(milliseconds), "timeoutMilliseconds:");
	}

	[Theory]
	[InlineData(CheatEngineFailureKind.NotFound, false, ToolErrorKind.NotFound)]
	[InlineData(CheatEngineFailureKind.Cancelled, false, ToolErrorKind.Cancelled)]
	[InlineData(CheatEngineFailureKind.Cancelled, true, ToolErrorKind.Stopping)]
	[InlineData(CheatEngineFailureKind.InvalidState, true, ToolErrorKind.Stopping)]
	[InlineData(CheatEngineFailureKind.LuaError, false, ToolErrorKind.HostRefused)]
	public void Resolve_FailedLookup_PreservesFailureAndActivationStoppingSemantics(
		CheatEngineFailureKind failureKind, bool stopping, ToolErrorKind expected)
	{
		using CancellationTokenSource lifetime = new();
		if (stopping)
		{
			lifetime.Cancel();
		}

		int lookups = 0;
		IInspectionClient inspection = ClientTestDouble.Create<IInspectionClient>((method, arguments) =>
		{
			Assert.Equal(nameof(IInspectionClient.TryResolveAddress), method.Name);
			Assert.Equal("missing", Assert.IsType<SymbolExpression>(arguments![0]).Value);
			arguments[2] = default(Address);
			arguments[3] = new CheatEngineFailure(failureKind, "Inspection.ResolveAddress", "known lookup refusal",
				hostEffect: CheatEngineHostEffect.NotStarted);
			lookups++;
			return false;
		});
		ICheatEngineDispatcher dispatcher = ClientTestDouble.Create<ICheatEngineDispatcher>(static (_, _) =>
			throw new InvalidOperationException("Address failure must not invoke target code."));
		ICheatEngineClient client = ClientTestDouble.Client(dispatcher, lifetime.Token,
			(nameof(ICheatEngineClient.Inspection), inspection));

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			ExecSupport.Resolve(client, "missing", "address", TestContext.Current.CancellationToken));

		Assert.Equal(1, lookups);
		Assert.Equal((expected, ToolHostEffect.NotStarted), (exception.Error.Kind, exception.Error.HostEffect));
		if (failureKind == CheatEngineFailureKind.NotFound)
		{
			Assert.Equal("address does not resolve to an address.", exception.Error.Message);
			Assert.Equal("Check the expression with symbol_resolve.", exception.Error.Hint);
		}
		else
		{
			Assert.Equal("Inspection.ResolveAddress", exception.Error.Operation);
			Assert.Equal("known lookup refusal", exception.Error.Message);
		}
	}

	private static void AssertInvalid(Action action, string parameterPrefix)
	{
		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(action);
		Assert.Equal((ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted, false),
			(exception.Error.Kind, exception.Error.HostEffect, exception.Error.Retryable));
		Assert.StartsWith(parameterPrefix, exception.Error.Message, StringComparison.Ordinal);
	}
}
