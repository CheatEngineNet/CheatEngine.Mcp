using System.Collections.Immutable;
using System.Text;

using CheatEngine.Client.Memory;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tools.Memory;
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Mcp.Tests.Tools.Memory;

/// <summary>Direct adapter coverage for the bounded, single-range printable-string search.</summary>
public sealed class MemoryStringToolsTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public void FindStrings_Utf8MultibyteRuns_ReadsTheExactRangeOnceAndReturnsExactAddresses()
	{
		TargetDouble target = new();
		byte[] bytes = [.. Encoding.UTF8.GetBytes("cat\0hé😊\0z")];
		MemoryBytesReadRequest? request = null;
		target.Memory = (method, arguments) =>
		{
			Assert.Equal(nameof(IMemoryClient.ReadBytes), method.Name);
			request = (MemoryBytesReadRequest) arguments[0]!;
			return bytes.ToImmutableArray();
		};

		MemoryStringFindResult result = new MemoryStringTools(target.Dispatch).FindStrings("1000", bytes.Length,
			minLength: 2, cancellationToken: Token);

		Assert.Equal((new Address(0x1000), bytes.Length), (request!.Value.Address, request.Value.Length));
		Assert.Single(target.CallsTo("Memory"));
		Assert.Equal(("1000", bytes.Length, MemoryStringSearchEncoding.Utf8, 2, null, false),
			(result.Address, result.Size, result.Encoding, result.Total, result.NextOffset, result.Truncated));
		Assert.Equal(
		[
			new MemoryStringMatch("1000", 3, "cat", false, true, false),
			new MemoryStringMatch("1004", 7, "hé😊", false, false, false)
		], result.Strings);
	}

	[Fact]
	public void FindStrings_Utf16LeSurrogatePairAndOddTrailingByte_PreservesThePairAndDelimitsTheTail()
	{
		TargetDouble target = new();
		byte[] bytes = [.. Encoding.Unicode.GetBytes("ok😀"), 0, 0, 0x41];
		Serve(target, bytes);

		MemoryStringFindResult result = new MemoryStringTools(target.Dispatch).FindStrings("2000", bytes.Length,
			MemoryStringSearchEncoding.Utf16Le, 3, cancellationToken: Token);

		MemoryStringMatch match = Assert.Single(result.Strings);
		Assert.Equal(("2000", 8, "ok😀", false, true, false),
			(match.Address, match.ByteLength, match.Text, match.PreviewTruncated, match.StartsAtRangeBoundary,
				match.EndsAtRangeBoundary));
	}

	[Fact]
	public void FindStrings_InvalidUtf8AndControlCharacters_DelimitWithoutInventingReplacementText()
	{
		TargetDouble target = new();
		byte[] bytes = [.. Encoding.UTF8.GetBytes("good"), 0xC3, 0x01, .. Encoding.UTF8.GetBytes("next")];
		Serve(target, bytes);

		MemoryStringFindResult result = new MemoryStringTools(target.Dispatch).FindStrings("3000", bytes.Length,
			minLength: 4, cancellationToken: Token);

		Assert.Equal(["good", "next"], result.Strings.Select(static match => match.Text));
		Assert.Equal(["3000", "3006"], result.Strings.Select(static match => match.Address));
		Assert.DoesNotContain(result.Strings, static match => match.Text.Contains('\uFFFD'));
	}

	[Fact]
	public void FindStrings_UnpairedUtf16Surrogates_DelimitWithoutLosingTheFollowingRun()
	{
		TargetDouble target = new();
		byte[] bytes = [.. Encoding.Unicode.GetBytes("good"), 0x00, 0xD8,
			.. Encoding.Unicode.GetBytes("next"), 0x00, 0xDC, .. Encoding.Unicode.GetBytes("last")];
		Serve(target, bytes);

		MemoryStringFindResult result = new MemoryStringTools(target.Dispatch).FindStrings("1000", bytes.Length,
			MemoryStringSearchEncoding.Utf16Le, cancellationToken: Token);

		Assert.Equal(["good", "next", "last"], result.Strings.Select(static match => match.Text));
		Assert.Equal(["1000", "100A", "1014"], result.Strings.Select(static match => match.Address));
	}

	[Fact]
	public void FindStrings_UnicodeFormatAndSeparators_DelimitPrintableRuns()
	{
		TargetDouble target = new();
		byte[] bytes = Encoding.UTF8.GetBytes("good\u200Bnext\u2028last");
		Serve(target, bytes);

		MemoryStringFindResult result = new MemoryStringTools(target.Dispatch).FindStrings("3800", bytes.Length,
			minLength: 4, cancellationToken: Token);

		Assert.Equal(["good", "next", "last"], result.Strings.Select(static match => match.Text));
	}

	[Fact]
	public void FindStrings_SpaceSeparators_RemainWithinANaturalLanguageRun()
	{
		TargetDouble target = new();
		byte[] bytes = Encoding.UTF8.GetBytes("hello world\u00A0again");
		Serve(target, bytes);

		MemoryStringMatch match = Assert.Single(new MemoryStringTools(target.Dispatch).FindStrings("3900", bytes.Length,
			minLength: 4, cancellationToken: Token).Strings);

		Assert.Equal("hello world\u00A0again", match.Text);
	}

	[Fact]
	public void FindStrings_MinimumLengthCountsUnicodeScalarsAndFilterUsesTheFullRunBeyondItsPreview()
	{
		TargetDouble target = new();
		string longRun = new string('a', MemoryStringTools.MaximumPreviewScalars - 1) + "😀Needle";
		byte[] bytes = [.. Encoding.UTF8.GetBytes("😀\0" + longRun)];
		Serve(target, bytes);

		MemoryStringFindResult result = new MemoryStringTools(target.Dispatch).FindStrings("4000", bytes.Length,
			minLength: 2, contains: "needle", cancellationToken: Token);

		MemoryStringMatch match = Assert.Single(result.Strings);
		Assert.Equal(Encoding.UTF8.GetByteCount(longRun), match.ByteLength);
		Assert.Equal(MemoryStringTools.MaximumPreviewScalars, match.Text.EnumerateRunes().Count());
		Assert.EndsWith("😀", match.Text, StringComparison.Ordinal);
		Assert.True(match.PreviewTruncated);
		Assert.DoesNotContain("Needle", match.Text, StringComparison.Ordinal);
	}

	[Fact]
	public void FindStrings_PagingReportsTotalNextOffsetAndRangeEdgeEvidence()
	{
		TargetDouble target = new();
		byte[] bytes = Encoding.UTF8.GetBytes("one\0two\0three");
		Serve(target, bytes);

		MemoryStringFindResult result = new MemoryStringTools(target.Dispatch).FindStrings("5000", bytes.Length,
			minLength: 3, offset: 1, limit: 1, cancellationToken: Token);

		Assert.Equal((3, 2, true), (result.Total, result.NextOffset, result.Truncated));
		Assert.Equal(new MemoryStringMatch("5004", 3, "two", false, false, false), Assert.Single(result.Strings));
	}

	[Fact]
	public void FindStrings_ThirdPageAndOffsetPastEnd_AreReachableWithoutRetainingEarlierMatches()
	{
		TargetDouble target = new();
		byte[] bytes = new byte[513 * 2];
		for (int index = 0; index < bytes.Length; index += 2)
		{
			bytes[index] = (byte) 'a';
		}

		Serve(target, bytes);
		MemoryStringFindResult third = new MemoryStringTools(target.Dispatch).FindStrings("6000", bytes.Length,
			minLength: 1, offset: 512, limit: 1, cancellationToken: Token);
		MemoryStringFindResult pastEnd = new MemoryStringTools(target.Dispatch).FindStrings("6000", bytes.Length,
			minLength: 1, offset: 700, limit: 1, cancellationToken: Token);

		Assert.Equal((513, null, false), (third.Total, third.NextOffset, third.Truncated));
		Assert.Equal(new MemoryStringMatch("6400", 1, "a", false, false, false), Assert.Single(third.Strings));
		Assert.Equal((513, null, false), (pastEnd.Total, pastEnd.NextOffset, pastEnd.Truncated));
		Assert.Empty(pastEnd.Strings);
	}

	[Theory]
	[InlineData(4, 0xFFFFFFFEUL)]
	[InlineData(8, 0xFFFFFFFFFFFFFFFEUL)]
	public void FindStrings_RangePastTargetAddressSpace_RefusesBeforeReading(int pointerBytes, ulong address)
	{
		TargetDouble target = new()
		{
			Bitness = pointerBytes == 4 ? PointerSize.Bit32 : PointerSize.Bit64
		};
		target.Symbols["edge"] = address;

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new MemoryStringTools(target.Dispatch).FindStrings("edge", 3, cancellationToken: Token));

		Assert.Equal((ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Empty(target.CallsTo("Memory"));
	}

	[Theory]
	[InlineData(0, 1, 64, 4, ToolErrorKind.InvalidArgument)]
	[InlineData(MemoryStringTools.MaximumBytes + 1, 0, 64, 4, ToolErrorKind.LimitExceeded)]
	[InlineData(16, -1, 64, 4, ToolErrorKind.InvalidArgument)]
	[InlineData(16, 0, 257, 4, ToolErrorKind.LimitExceeded)]
	[InlineData(16, 0, 64, 0, ToolErrorKind.InvalidArgument)]
	public void FindStrings_InvalidBounds_RefuseBeforeDispatch(int size, int offset, int limit, int minLength,
		ToolErrorKind kind)
	{
		TargetDouble target = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new MemoryStringTools(target.Dispatch).FindStrings("1000", size, minLength: minLength, offset: offset,
				limit: limit, cancellationToken: Token));

		Assert.Equal((kind, ToolHostEffect.NotStarted), (exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal(0, target.Dispatcher.Calls);
	}

	[Fact]
	public void FindStrings_InvalidEncodingAndUnreadableRange_DoNotReturnAPartialClaim()
	{
		TargetDouble invalid = new();
		CheatEngineToolException invalidEncoding = Assert.Throws<CheatEngineToolException>(() =>
			new MemoryStringTools(invalid.Dispatch).FindStrings("1000", 1, (MemoryStringSearchEncoding) 99,
				cancellationToken: Token));
		Assert.Equal((ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted),
			(invalidEncoding.Error.Kind, invalidEncoding.Error.HostEffect));

		TargetDouble unreadable = new()
		{
			Memory = (_, _) => throw new CheatEngineFailure(CheatEngineFailureKind.MemoryReadFailed, "Memory.ReadBytes",
				"The requested range is unreadable.", hostEffect: CheatEngineHostEffect.Completed).ToException()
		};

		CheatEngineToolException readFailure = Assert.Throws<CheatEngineToolException>(() =>
			new MemoryStringTools(unreadable.Dispatch).FindStrings("1000", 16, cancellationToken: Token));
		Assert.Equal(ToolErrorKind.MemoryReadFailed, readFailure.Error.Kind);
		Assert.Empty(unreadable.CallsTo("Lua"));
	}

	[Fact]
	public void FindStrings_SuccessfulReadWithWrongByteCount_DiscardsTheResult()
	{
		TargetDouble target = new()
		{
			Memory = (_, _) => ImmutableArray.Create<byte>(0x61, 0x62, 0x63)
		};

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new MemoryStringTools(target.Dispatch).FindStrings("1000", 4, cancellationToken: Token));

		Assert.Equal(ToolErrorKind.InvalidState, exception.Error.Kind);
	}

	[Fact]
	public void FindStrings_TargetChangedAfterResolution_DiscardsTheRangeBeforeReading()
	{
		TargetDouble target = new();
		target.Epochs.Enqueue(1);
		target.Epochs.Enqueue(2);

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new MemoryStringTools(target.Dispatch).FindStrings("1000", 4, cancellationToken: Token));

		Assert.Equal(ToolErrorKind.TargetChanged, exception.Error.Kind);
		Assert.Empty(target.CallsTo("Memory"));
	}

	[Theory]
	[InlineData(MemoryStringSearchEncoding.Utf8)]
	[InlineData(MemoryStringSearchEncoding.Utf16Le)]
	public void FindStrings_MinimumLength_CountsScalarsInsteadOfBytesOrUtf16CodeUnits(MemoryStringSearchEncoding encoding)
	{
		TargetDouble target = new();
		byte[] bytes = (encoding == MemoryStringSearchEncoding.Utf8 ? Encoding.UTF8 : Encoding.Unicode)
			.GetBytes("😀\0ab");
		Serve(target, bytes);

		MemoryStringFindResult result = new MemoryStringTools(target.Dispatch).FindStrings("1000", bytes.Length,
			encoding, minLength: 2, cancellationToken: Token);

		Assert.Equal(1, result.Total);
		Assert.Equal("ab", Assert.Single(result.Strings).Text);
	}

	[Fact]
	public void FindStrings_TargetChangedDuringRead_DiscardsTheBytes()
	{
		TargetDouble target = new();
		foreach (long epoch in new long[] { 1, 1, 1, 1, 2 })
		{
			target.Epochs.Enqueue(epoch);
		}
		Serve(target, Encoding.UTF8.GetBytes("hello"));

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new MemoryStringTools(target.Dispatch).FindStrings("1000", 5, cancellationToken: Token));

		Assert.Equal(ToolErrorKind.TargetChanged, exception.Error.Kind);
		Assert.Single(target.CallsTo("Memory"));
	}

	[Fact]
	public void FindStrings_CancelledAfterRead_DoesNotReturnDecodedResults()
	{
		using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
		TargetDouble target = new()
		{
			Memory = (_, _) =>
			{
				cancellation.Cancel();
				return Encoding.UTF8.GetBytes("hello").ToImmutableArray();
			}
		};

		Assert.ThrowsAny<OperationCanceledException>(() =>
			new MemoryStringTools(target.Dispatch).FindStrings("1000", 5, cancellationToken: cancellation.Token));
		Assert.Single(target.CallsTo("Memory"));
	}

	[Fact]
	public void FindStrings_MaximumRange_ReturnsBoundedPreviewAndFullByteLength()
	{
		TargetDouble target = new();
		byte[] bytes = new byte[MemoryStringTools.MaximumBytes];
		Array.Fill(bytes, (byte) 'a');
		Serve(target, bytes);

		MemoryStringFindResult result = new MemoryStringTools(target.Dispatch).FindStrings("1000", bytes.Length,
			cancellationToken: Token);

		Assert.Equal(1, result.Total);
		MemoryStringMatch match = Assert.Single(result.Strings);
		Assert.Equal(1_048_576, match.ByteLength);
		Assert.Equal(new string('a', 256), match.Text);
		Assert.True(match.PreviewTruncated);
		Assert.True(match.StartsAtRangeBoundary);
		Assert.True(match.EndsAtRangeBoundary);
		Assert.Single(target.CallsTo("Memory"));
	}

	private static void Serve(TargetDouble target, byte[] bytes)
	{
		target.Memory = (method, arguments) =>
		{
			Assert.Equal(nameof(IMemoryClient.ReadBytes), method.Name);
			Assert.Equal(bytes.Length, ((MemoryBytesReadRequest) arguments[0]!).Length);
			return bytes.ToImmutableArray();
		};
	}
}
