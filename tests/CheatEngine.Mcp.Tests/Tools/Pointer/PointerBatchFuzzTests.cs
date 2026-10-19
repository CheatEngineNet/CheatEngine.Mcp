using System.Globalization;

using CheatEngine.Mcp.Tools.Pointer;
using CheatEngine.SDK.Engine.Runtime;

using Xunit.Sdk;

namespace CheatEngine.Mcp.Tests.Tools.Pointer;

/// <summary>Deterministic pointer-chain graph cases with an arithmetic oracle independent of the tool.</summary>
public sealed class PointerBatchFuzzTests
{
	private const int Seed = unchecked((int) 0xB47C_1055);
	private const int CaseCount = 48;

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task ReadChains_GeneratedSignedGraphs_PreserveHopsFaultPrefixesAndCompactResults()
	{
		Random random = new(Seed);
		for (int iteration = 0; iteration < CaseCount; iteration++)
		{
			Token.ThrowIfCancellationRequested();
			int caseSeed = random.Next();
			foreach (PointerSize size in new[] { PointerSize.Bit32, PointerSize.Bit64 })
			{
				string context = $"seed={Seed:X8}, iteration={iteration}, caseSeed={caseSeed:X8}, pointerBytes={size.Bytes}";
				try
				{
					await AssertCaseAsync(new Random(caseSeed), size);
				}
				catch (Exception exception) when (exception is not OperationCanceledException)
				{
					throw new XunitException($"{context}: {exception.Message}");
				}
			}
		}
	}

	private static async Task AssertCaseAsync(Random random, PointerSize size)
	{
		int hops = random.Next(1, 6);
		int? failureAt = random.Next(3) == 0 ? random.Next(hops) : null;
		long[] offsets = new long[hops];
		for (int index = 0; index < offsets.Length; index++)
		{
			offsets[index] = random.Next(-0x20, 0x21);
		}

		await using PointerFixture fixture = new(pointerSize: size);
		ulong root = PointerFixture.ModuleBase + 0x300UL + ((ulong) random.Next(0, 128) * 8);
		string rootExpression = FormatAddress(root);
		List<PointerHop> expectedHops = [];
		ulong current = root;
		if (failureAt == 0)
		{
			current = 0x60000;
			rootExpression = FormatAddress(current);
		}
		else
		{
			for (int index = 0; index < hops; index++)
			{
				if (failureAt == index)
				{
					break;
				}

				ulong next = failureAt == index + 1
					? 0x60000
					: 0x22000UL + ((ulong) index * 0x100) + ((ulong) random.Next(0, 16) * 0x10);
				ulong pointer = PointerFor(next, offsets[index]);
				fixture.PutPointer(current, pointer);
				expectedHops.Add(new PointerHop(FormatAddress(current), FormatAddress(pointer), FormatOffset(offsets[index]),
					FormatAddress(next)));
				current = next;
			}
		}

		PointerChainCandidate[] candidates = [new("generated", rootExpression,
			[.. offsets.Select(FormatOffset)])];
		PointerBatchTools tools = (PointerBatchTools) fixture.Targets.Get(typeof(PointerBatchTools));
		int beforeCompact = fixture.PrimitiveReads;
		PointerChainBatchResult compact = tools.ReadChains(candidates, cancellationToken: Token);
		int compactReads = fixture.PrimitiveReads - beforeCompact;
		int beforeDetailed = fixture.PrimitiveReads;
		PointerChainBatchResult detailed = tools.ReadChains(candidates, includeHops: true, cancellationToken: Token);
		int detailedReads = fixture.PrimitiveReads - beforeDetailed;
		int expectedReads = failureAt is null ? hops : failureAt.Value + 1;
		Assert.Equal(expectedReads, compactReads);
		Assert.Equal(expectedReads, detailedReads);
		PointerChainCandidateResult result = Assert.Single(detailed.Candidates);
		Assert.Equal(Assert.Single(compact.Candidates), result with
		{
			Hops = null
		});
		Assert.Null(Assert.Single(compact.Candidates).Hops);
		Assert.Equal(expectedHops, result.Hops);

		if (failureAt is { } failed)
		{
			Assert.Equal((PointerChainResolutionStatus.Unreadable, failed, "60000"),
				(result.ChainStatus, result.FailedHop, result.FailedReadAt));
			Assert.Equal(expectedHops.Count, result.Hops!.Count);
			return;
		}

		Assert.Equal(PointerChainResolutionStatus.Resolved, result.ChainStatus);
		Assert.Equal(FormatAddress(current), result.Address);
		Assert.Equal(hops, result.Hops!.Count);
	}

	private static ulong PointerFor(ulong next, long offset)
	{
		return offset >= 0 ? next - (ulong) offset : next + Magnitude(offset);
	}

	private static ulong Magnitude(long value) => unchecked((ulong) -(value + 1)) + 1;

	private static string FormatAddress(ulong value) => value.ToString("X", CultureInfo.InvariantCulture);

	private static string FormatOffset(long value) => value < 0
		? "-" + Magnitude(value).ToString("X", CultureInfo.InvariantCulture)
		: value.ToString("X", CultureInfo.InvariantCulture);
}
