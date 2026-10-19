using System.Collections.Immutable;
using System.Globalization;
using System.Text;

using CheatEngine.Client.Inspection;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tools.Memory;
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Engine.Values;

using Xunit.Sdk;

namespace CheatEngine.Mcp.Tests.Tools.Memory;

/// <summary>Deterministic, bounded generated coverage of string and vtable inspection contracts.</summary>
public sealed class MemoryInspectionFuzzTests
{
	private const int Seed = 0x5EED_1A5E;
	private const int CaseCount = 96;

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public void FindStrings_GeneratedValidRuns_MatchIndependentScalarAndByteOracle()
	{
		Random random = new(Seed);
		for (int iteration = 0; iteration < CaseCount; iteration++)
		{
			Token.ThrowIfCancellationRequested();
			int caseSeed = random.Next();
			RunCase($"seed={Seed:X8}, iteration={iteration}, caseSeed={caseSeed:X8}", () =>
				AssertValidStringCase(new Random(caseSeed)));
		}
	}

	[Fact]
	public void FindStrings_ArbitraryBytes_HaveOrderedInBoundsSpansAndConsistentPages()
	{
		Random random = new(Seed ^ 0x4D41_4C46);
		for (int iteration = 0; iteration < CaseCount; iteration++)
		{
			Token.ThrowIfCancellationRequested();
			int caseSeed = random.Next();
			RunCase($"seed={Seed ^ 0x4D41_4C46:X8}, iteration={iteration}, caseSeed={caseSeed:X8}", () =>
				AssertMalformedStringCase(new Random(caseSeed)));
		}
	}

	[Fact]
	public void ReadVtable_GeneratedTables_UseExactPointerWidthRequestsAndPreserveSlotOutcomes()
	{
		Random random = new(Seed ^ 0x5654_4142);
		for (int iteration = 0; iteration < CaseCount; iteration++)
		{
			Token.ThrowIfCancellationRequested();
			int caseSeed = random.Next();
			RunCase($"seed={Seed ^ 0x5654_4142:X8}, iteration={iteration}, caseSeed={caseSeed:X8}", () =>
				AssertVtableCase(new Random(caseSeed), iteration % 9));
		}
	}

	private static void AssertValidStringCase(Random random)
	{
		MemoryStringSearchEncoding encoding = random.Next(2) == 0
			? MemoryStringSearchEncoding.Utf8
			: MemoryStringSearchEncoding.Utf16Le;
		List<GeneratedRun> runs = [];
		List<byte> bytes = [];
		int runCount = random.Next(1, 7);
		for (int index = 0; index < runCount; index++)
		{
			string text = GeneratedText(random, random.Next(1, 9));
			byte[] encoded = Encode(encoding, text);
			runs.Add(new GeneratedRun(bytes.Count, encoded.Length, text, text.EnumerateRunes().Count()));
			bytes.AddRange(encoded);
			if (index != runCount - 1 || random.Next(2) == 0)
			{
				bytes.AddRange(encoding is MemoryStringSearchEncoding.Utf8 ? [0] : [0, 0]);
			}
		}

		int minimum = random.Next(1, 7);
		string? contains = random.Next(2) == 0 ? null : runs[random.Next(runs.Count)].Text[..1].ToUpperInvariant();
		GeneratedRun[] matching = [.. runs.Where(run => run.Scalars >= minimum &&
			(contains is null || run.Text.Contains(contains, StringComparison.OrdinalIgnoreCase)))];
		int offset = random.Next(matching.Length + 3);
		int limit = random.Next(1, 5);
		TargetDouble target = new();
		MemoryBytesReadRequest? request = null;
		target.Memory = (method, arguments) =>
		{
			Assert.Equal(nameof(IMemoryClient.ReadBytes), method.Name);
			request = (MemoryBytesReadRequest) arguments[0]!;
			return bytes.ToImmutableArray();
		};

		MemoryStringFindResult result = new MemoryStringTools(target.Dispatch).FindStrings("4000", bytes.Count, encoding,
			minimum, contains, offset, limit, Token);
		GeneratedRun[] expected = [.. matching.Skip(offset).Take(limit)];
		Assert.Equal((new Address(0x4000), bytes.Count), (request!.Value.Address, request.Value.Length));
		Assert.Equal(matching.Length, result.Total);
		Assert.Equal(offset + expected.Length < matching.Length ? offset + expected.Length : null, result.NextOffset);
		Assert.Equal(result.NextOffset is not null, result.Truncated);
		Assert.Equal(expected.Length, result.Strings.Length);
		for (int index = 0; index < expected.Length; index++)
		{
			GeneratedRun run = expected[index];
			Assert.Equal((0x4000UL + (uint) run.Offset, run.ByteLength, run.Text, false, run.Offset == 0,
				run.Offset + run.ByteLength == bytes.Count),
				(ParseAddress(result.Strings[index].Address), result.Strings[index].ByteLength, result.Strings[index].Text,
					result.Strings[index].PreviewTruncated, result.Strings[index].StartsAtRangeBoundary,
					result.Strings[index].EndsAtRangeBoundary));
		}
	}

	private static void AssertMalformedStringCase(Random random)
	{
		byte[] bytes = new byte[random.Next(1, 193)];
		random.NextBytes(bytes);
		MemoryStringSearchEncoding encoding = random.Next(2) == 0
			? MemoryStringSearchEncoding.Utf8
			: MemoryStringSearchEncoding.Utf16Le;
		TargetDouble target = new()
		{
			Memory = (_, _) => bytes.ToImmutableArray()
		};
		MemoryStringTools tools = new(target.Dispatch);
		MemoryStringFindResult complete = tools.FindStrings("8000", bytes.Length, encoding, minLength: 1, limit: 256,
			cancellationToken: Token);
		Assert.Equal(complete.Strings.Length, complete.Total);
		Assert.Null(complete.NextOffset);
		Assert.False(complete.Truncated);
		AssertSpans(complete.Strings, 0x8000, bytes.Length);

		int offset = random.Next(complete.Total + 3);
		int limit = random.Next(1, 9);
		MemoryStringFindResult page = tools.FindStrings("8000", bytes.Length, encoding, minLength: 1, offset: offset,
			limit: limit, cancellationToken: Token);
		MemoryStringMatch[] expected = [.. complete.Strings.Skip(offset).Take(limit)];
		Assert.Equal(expected, page.Strings);
		Assert.Equal(complete.Total, page.Total);
		Assert.Equal(offset + expected.Length < complete.Total ? offset + expected.Length : null, page.NextOffset);
		Assert.Equal(page.NextOffset is not null, page.Truncated);
		AssertSpans(page.Strings, 0x8000, bytes.Length);
	}

	private static void AssertVtableCase(Random random, int count)
	{
		int pointerBytes = random.Next(2) == 0 ? 4 : 8;
		bool direct = random.Next(2) == 0;
		int start = random.Next(0, 12);
		ulong table = pointerBytes == 4 ? 0x4000UL : 0x1_0000_4000UL;
		ulong requested = direct ? table : table - 0x100;
		TargetDouble target = new()
		{
			Bitness = pointerBytes == 4 ? PointerSize.Bit32 : PointerSize.Bit64
		};
		List<MemoryBytesReadRequest> requests = [];
		HashSet<int> failed = [.. Enumerable.Range(start, count).Where(_ => random.Next(5) == 0)];
		ulong[] values = [.. Enumerable.Range(0, start + count).Select(index => index % 3 == 0 ? 0UL :
			(pointerBytes == 4 ? 0x5000UL + (uint) index : 0x1_0000_5000UL + (uint) index))];
		target.Memory = (method, arguments) =>
		{
			Assert.Equal(nameof(IMemoryClient.ReadBytesDetailed), method.Name);
			MemoryBytesReadRequest request = (MemoryBytesReadRequest) arguments[0]!;
			requests.Add(request);
			if (!direct && request.Address.ToUInt64() == requested)
			{
				return Outcome(table, pointerBytes);
			}

			int index = checked((int) ((request.Address.ToUInt64() - table) / (uint) pointerBytes));
			return failed.Contains(index)
				? new MemoryBytesReadOutcome(pointerBytes, [], new CheatEngineFailure(CheatEngineFailureKind.MemoryReadFailed,
					"Memory.ReadBytesDetailed", "generated unreadable slot", hostEffect: CheatEngineHostEffect.Completed))
				: Outcome(values[index], pointerBytes);
		};
		target.Inspection = (method, arguments) =>
		{
			Assert.Equal(nameof(IInspectionClient.TryResolveName), method.Name);
			arguments[1] = null;
			arguments[2] = new CheatEngineFailure(CheatEngineFailureKind.NotFound, "Inspection.TryResolveName",
				"generated no symbol", hostEffect: CheatEngineHostEffect.Completed);
			return false;
		};

		if (count == 0)
		{
			CheatEngineToolException error = Assert.Throws<CheatEngineToolException>(() =>
				new MemoryVtableTools(target.Dispatch).ReadVtable(FormatAddress(requested), direct, start, count, Token));
			Assert.Equal(ToolErrorKind.InvalidArgument, error.Error.Kind);
			Assert.Empty(requests);
			return;
		}

		MemoryVtableResult result = new MemoryVtableTools(target.Dispatch).ReadVtable(FormatAddress(requested), direct,
			start, count, Token);
		ulong[] expectedReads = direct
			? [.. Enumerable.Range(start, count).Select(index => table + ((ulong) index * (uint) pointerBytes))]
			: [requested, .. Enumerable.Range(start, count).Select(index => table + ((ulong) index * (uint) pointerBytes))];
		Assert.Equal(expectedReads, requests.Select(static request => request.Address.ToUInt64()));
		Assert.All(requests, request => Assert.Equal(pointerBytes, request.Length));
		Assert.Equal((FormatAddress(requested), FormatAddress(table), pointerBytes, start),
			(result.Address, result.VtableAddress, result.PointerBytes, result.StartIndex));
		for (int offset = 0; offset < count; offset++)
		{
			MemoryVtableSlot slot = result.Slots[offset];
			int index = start + offset;
			Assert.Equal((index, FormatAddress(table + ((ulong) index * (uint) pointerBytes))), (slot.Index, slot.SlotAddress));
			if (failed.Contains(index))
			{
				Assert.Equal(ToolErrorKind.MemoryReadFailed, slot.Error!.Kind);
				Assert.Null(slot.FunctionAddress);
			}
			else
			{
				Assert.Equal(FormatAddress(values[index]), slot.FunctionAddress);
				Assert.Null(slot.Error);
			}
		}
	}

	private static void AssertSpans(IReadOnlyList<MemoryStringMatch> strings, ulong start, int size)
	{
		ulong previousEnd = start;
		foreach (MemoryStringMatch match in strings)
		{
			ulong address = ParseAddress(match.Address);
			Assert.True(match.ByteLength > 0 && address >= previousEnd && address >= start &&
				address - start <= (ulong) size && (ulong) match.ByteLength <= (ulong) size - (address - start),
				$"Invalid span {match.Address}+{match.ByteLength} in {start:X}+{size:X}.");
			previousEnd = address + (uint) match.ByteLength;
		}
	}

	private static string GeneratedText(Random random, int scalars)
	{
		string[] alphabet = ["A", "b", "é", "Ω", "😀", " "];
		StringBuilder text = new();
		for (int index = 0; index < scalars; index++)
		{
			text.Append(alphabet[random.Next(alphabet.Length)]);
		}

		return text.ToString();
	}

	private static byte[] Encode(MemoryStringSearchEncoding encoding, string text) =>
		(encoding is MemoryStringSearchEncoding.Utf8 ? Encoding.UTF8 : Encoding.Unicode).GetBytes(text);

	private static MemoryBytesReadOutcome Outcome(ulong value, int width)
	{
		byte[] bytes = width == 4 ? BitConverter.GetBytes((uint) value) : BitConverter.GetBytes(value);
		return new MemoryBytesReadOutcome(width, [.. bytes], null);
	}

	private static ulong ParseAddress(string text) => ulong.Parse(text, NumberStyles.AllowHexSpecifier,
		CultureInfo.InvariantCulture);

	private static string FormatAddress(ulong value) => value.ToString("X", CultureInfo.InvariantCulture);

	private static void RunCase(string context, Action body)
	{
		try
		{
			body();
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			throw new XunitException($"{context}: {exception.Message}");
		}
	}

	private sealed record GeneratedRun(int Offset, int ByteLength, string Text, int Scalars);
}
