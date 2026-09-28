using CheatEngine.Client.Scanning;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tools.Memory;
using CheatEngine.Mcp.Tools.Scan;

namespace CheatEngine.Mcp.Tests.Tools.Scan;

/// <summary>
///     Argument checks and request building of the value-scan tools: every refusal, including those the Client
///     factories would raise as exceptions, is a typed <c>invalid_argument</c> or <c>limit_exceeded</c> before
///     dispatch.
/// </summary>
public sealed class ScanCriteriaTests
{
	[Theory]
	[InlineData("byte", ValueScanValueType.Integer8)]
	[InlineData("Integer8", ValueScanValueType.Integer8)]
	[InlineData("int16", ValueScanValueType.Integer16)]
	[InlineData("INT", ValueScanValueType.Integer32)]
	[InlineData("long", ValueScanValueType.Integer64)]
	[InlineData("float", ValueScanValueType.SingleFloat)]
	[InlineData("doublefloat", ValueScanValueType.DoubleFloat)]
	[InlineData("string", ValueScanValueType.Utf8String)]
	[InlineData(" wstring ", ValueScanValueType.Utf16String)]
	[InlineData("bytearray", ValueScanValueType.ByteArray)]
	public void ParseValueType_AcceptsContractNamesAndAliases(string text, ValueScanValueType expected)
	{
		Assert.Equal(expected, ScanCriteria.ParseValueType(text));
	}

	[Theory]
	[InlineData("pointer")]
	[InlineData("uint32")]
	[InlineData("unsupported")]
	[InlineData(null)]
	public void ParseValueType_RefusesOtherTypes(string? text)
	{
		AssertRefused(() => ScanCriteria.ParseValueType(text), "valueType");
		Assert.False(ScanCriteria.TryParseValueType(text, out _));
	}

	[Theory]
	[InlineData("exact", ValueScanComparison.Exact)]
	[InlineData("Unknown Initial Value", ValueScanComparison.UnknownInitialValue)]
	[InlineData("bigger_than", ValueScanComparison.BiggerThan)]
	[InlineData("smaller", ValueScanComparison.SmallerThan)]
	[InlineData("BETWEEN", ValueScanComparison.Between)]
	public void ParseFirstComparison_NormalizesCaseSeparatorsAndAliases(string text, ValueScanComparison expected)
	{
		Assert.Equal(expected, ScanCriteria.ParseFirstComparison(text));
	}

	[Theory]
	[InlineData("increased_by", ValueScanComparison.IncreasedBy)]
	[InlineData("DecreasedBy", ValueScanComparison.DecreasedBy)]
	[InlineData("unchanged", ValueScanComparison.Unchanged)]
	[InlineData("greaterThan", ValueScanComparison.BiggerThan)]
	public void ParseNextComparison_NormalizesCaseSeparatorsAndAliases(string text, ValueScanComparison expected)
	{
		Assert.Equal(expected, ScanCriteria.ParseNextComparison(text));
	}

	[Fact]
	public void ParseComparison_RefusesComparisonsOfTheOtherScanKind()
	{
		AssertRefused(() => ScanCriteria.ParseFirstComparison("changed"), "comparison");
		AssertRefused(() => ScanCriteria.ParseNextComparison("unknown"), "comparison");
		AssertRefused(() => ScanCriteria.ParseNextComparison(null), "comparison");
	}

	[Theory]
	[InlineData(ValueScanComparison.Exact, null, null, "value")]
	[InlineData(ValueScanComparison.Exact, "1", "2", "upperValue")]
	[InlineData(ValueScanComparison.Between, "1", null, "upperValue")]
	[InlineData(ValueScanComparison.IncreasedBy, null, null, "value")]
	[InlineData(ValueScanComparison.Changed, "1", null, "comparison")]
	[InlineData(ValueScanComparison.UnknownInitialValue, null, "1", "comparison")]
	public void CheckArguments_EnforcesWhichValuesTheComparisonTakes(ValueScanComparison comparison, string? value,
		string? upperValue, string parameter)
	{
		AssertRefused(() => ScanCriteria.CheckArguments(comparison, value, upperValue, null), parameter);
	}

	[Theory]
	[InlineData(-1)]
	[InlineData(16)]
	public void CheckArguments_RefusesFloatDecimalsOutsideZeroToFifteen(int decimals)
	{
		AssertRefused(() => ScanCriteria.CheckArguments(ValueScanComparison.Exact, "1", null, decimals),
			"floatDecimals");
	}

	[Fact]
	public void CheckArguments_RefusesValuesLongerThanTheLimit()
	{
		string text = new('1', ScanCriteria.MaximumValueCharacters + 1);

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			ScanCriteria.CheckArguments(ValueScanComparison.Exact, text, null, null));

		Assert.Equal(ToolErrorKind.LimitExceeded, exception.Error.Kind);
		Assert.StartsWith("value:", exception.Error.Message, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("string", "between", "a", "b", "comparison")]
	[InlineData("wstring", "greater", "a", null, "comparison")]
	[InlineData("bytes", "unknown", null, null, "comparison")]
	[InlineData("string", "exact", "", null, "value")]
	[InlineData("wstring", "exact", "", null, "value")]
	[InlineData("bytes", "exact", " , ", null, "value")]
	[InlineData("bytes", "exact", "48 ?? 05", null, "value")]
	[InlineData("float", "exact", "NaN", null, "value")]
	[InlineData("float", "exact", "1e39", null, "value")]
	[InlineData("double", "between", "0", "Infinity", "upperValue")]
	[InlineData("int32", "exact", "0x10", null, "value")]
	[InlineData("int32", "exact", "1.5", null, "value")]
	[InlineData("byte", "exact", "256", null, "value")]
	[InlineData("int16", "less", "-32769", null, "value")]
	public void First_RefusesWhatTheClientFactoriesWouldThrowFor(string type, string comparison, string? value,
		string? upperValue, string parameter)
	{
		AssertRefused(() => ScanCriteria.First(ScanCriteria.ParseValueType(type),
			ScanCriteria.ParseFirstComparison(comparison), value, upperValue, null), parameter);
	}

	[Theory]
	[InlineData("string", "increased", null)]
	[InlineData("bytes", "changed", null)]
	[InlineData("wstring", "increasedBy", "1")]
	[InlineData("string", "less", "a")]
	public void Next_RefusesEveryComparisonButExactForTextAndBytes(string type, string comparison, string? value)
	{
		AssertRefused(() => ScanCriteria.Next(ScanCriteria.ParseValueType(type),
			ScanCriteria.ParseNextComparison(comparison), value, null, null), "comparison");
	}

	[Theory]
	[InlineData("int32")]
	[InlineData("string")]
	[InlineData("bytes")]
	public void FloatDecimals_AreRefusedForTypesThatAreNotFloatingPoint(string type)
	{
		ValueScanValueType parsed = ScanCriteria.ParseValueType(type);
		string value = type == "bytes" ? "48" : "7";

		AssertRefused(() => ScanCriteria.First(parsed, ValueScanComparison.Exact, value, null, 3), "floatDecimals");
		AssertRefused(() => ScanCriteria.Next(parsed, ValueScanComparison.Exact, value, null, 3), "floatDecimals");
	}

	[Theory]
	[InlineData("float", "25.25", null, "25.250000")]
	[InlineData("double", "25.25", null, "25.250000000000")]
	[InlineData("float", "25.256", 2, "25.26")]
	[InlineData("double", "57", 0, "57")]
	[InlineData("int32", " -25 ", null, "-25")]
	[InlineData("bytes", "0x48,8B 05", null, "48 8B 05")]
	[InlineData("bytes", "488B05", null, "48 8B 05")]
	[InlineData("string", "fox", null, "fox")]
	public void First_WritesTheValueTextCheatEngineCompares(string type, string value, int? decimals,
		string expected)
	{
		ValueScanFirstRequest request = ScanCriteria.First(ScanCriteria.ParseValueType(type),
			ValueScanComparison.Exact, value, null, decimals);

		Assert.Equal(expected, request.Value?.Text);
	}

	[Fact]
	public void Next_BuildsTypedRequestsForNumericComparisons()
	{
		ValueScanNextRequest between = ScanCriteria.Next(ValueScanValueType.SingleFloat,
			ValueScanComparison.Between, "56.5", "57.5", 1);
		ValueScanNextRequest decreased = ScanCriteria.Next(ValueScanValueType.Integer32,
			ValueScanComparison.Decreased, null, null, null);

		Assert.Equal((ValueScanComparison.Between, "56.5", "57.5"),
			(between.Comparison, between.Value?.Text, between.UpperValue?.Text));
		Assert.Equal(ValueScanComparison.Decreased, decreased.Comparison);
		Assert.Null(decreased.Value);
	}

	[Fact]
	public void OverflowOfASignedType_HintsAtTheSignedEquivalent()
	{
		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			ScanCriteria.First(ValueScanValueType.Integer32, ValueScanComparison.Exact, "4294967295", null, null));

		Assert.Contains("-1", exception.Error.Hint, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("400000", null, "endAddress")]
	[InlineData(null, "500000", "startAddress")]
	[InlineData(" ", "500000", "startAddress")]
	public void NamedScanOptions_RequireACompleteRange(string? start, string? end, string parameter)
	{
		AssertRefused(() => NamedScanOptions.Create(start, end, ProtectionRequirement.Any,
			ProtectionRequirement.Any, ProtectionRequirement.Any, null, null), parameter);
	}

	[Theory]
	[InlineData(4, "0", "lastDigits")]
	[InlineData(0, null, "alignment")]
	[InlineData(null, "XYZ", "lastDigits")]
	[InlineData(null, "12345678901234567", "lastDigits")]
	public void NamedScanOptions_RefuseInvalidAlignmentRules(int? alignment, string? lastDigits, string parameter)
	{
		AssertRefused(() => NamedScanOptions.Create(null, null, ProtectionRequirement.Any,
			ProtectionRequirement.Any, ProtectionRequirement.Any, alignment, lastDigits), parameter);
	}

	[Fact]
	public void NamedScanOptions_AlignmentAboveTheLimitIsALimit()
	{
		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			NamedScanOptions.Create(null, null, ProtectionRequirement.Any, ProtectionRequirement.Any,
				ProtectionRequirement.Any, NamedScanOptions.MaximumAlignment + 1, null));

		Assert.Equal(ToolErrorKind.LimitExceeded, exception.Error.Kind);
	}

	[Fact]
	public void NamedScanOptions_MapProtectionInCheatEngineOrderAndAnyToUnspecified()
	{
		NamedScanOptions options = NamedScanOptions.Create(null, null, ProtectionRequirement.Required,
			ProtectionRequirement.Excluded, ProtectionRequirement.Any, null, "a0");

		Assert.Equal(
			(ScanProtectionRequirement.Excluded, ScanProtectionRequirement.Unspecified,
				ScanProtectionRequirement.Required),
			(options.Protection.Executable, options.Protection.CopyOnWrite, options.Protection.Writable));
		Assert.Equal((ScanAlignmentMode.LastDigits, "A0"), (options.Alignment.Mode, options.Alignment.Digits));
	}

	[Theory]
	[InlineData("startAddress")]
	[InlineData("writable")]
	[InlineData("executable")]
	[InlineData("copyOnWrite")]
	[InlineData("alignment")]
	[InlineData("lastDigits")]
	public void NamedScanOptions_AreRefusedForTheMainScanner(string parameter)
	{
		NamedScanOptions options = NamedScanOptions.Create(
			parameter == "startAddress" ? "400000" : null, parameter == "startAddress" ? "500000" : null,
			parameter == "writable" ? ProtectionRequirement.Required : ProtectionRequirement.Any,
			parameter == "executable" ? ProtectionRequirement.Excluded : ProtectionRequirement.Any,
			parameter == "copyOnWrite" ? ProtectionRequirement.Excluded : ProtectionRequirement.Any,
			parameter == "alignment" ? 4 : null, parameter == "lastDigits" ? "8" : null);

		CheatEngineToolException exception = AssertRefused(options.RequireNone, parameter);

		Assert.NotNull(exception.Error.Hint);
		NamedScanOptions.Create(null, null, ProtectionRequirement.Any, ProtectionRequirement.Any,
			ProtectionRequirement.Any, null, null).RequireNone();
	}

	private static CheatEngineToolException AssertRefused(Action action, string parameter)
	{
		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(action);
		Assert.Equal((ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted, false),
			(exception.Error.Kind, exception.Error.HostEffect, exception.Error.Retryable));
		Assert.StartsWith(parameter + ":", exception.Error.Message, StringComparison.Ordinal);
		return exception;
	}

	private static CheatEngineToolException AssertRefused<T>(Func<T> action, string parameter)
	{
		return AssertRefused(() => { _ = action(); }, parameter);
	}
}
