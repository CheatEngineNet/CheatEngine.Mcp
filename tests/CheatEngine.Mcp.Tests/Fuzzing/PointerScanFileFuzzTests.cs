using System.Text;

using CheatEngine.Mcp.Tools.Pointer;

namespace CheatEngine.Mcp.Tests.Fuzzing;

/// <summary>Seeded, bounded pointer-scan wire cases with a source-snapshot oracle.</summary>
public sealed class PointerScanFileFuzzTests
{
	private const int Seed = 0x5CA1;
	private const int SnapshotCount = 128;
	private const int MaximumGeneratedPaths = 12;

	[Fact]
	public void SeededSnapshots_RoundTripExactlyAndAlwaysImportAsUnresolved()
	{
		Random random = new(Seed);
		for (int index = 0; index < SnapshotCount; index++)
		{
			TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
			PointerScanSnapshot expected = CreateSnapshot(random, index);
			using MemoryStream content = new();

			PointerScanFile.Save(content, expected, TestContext.Current.CancellationToken);
			Assert.InRange(content.Length, 1, PointerScanFile.MaximumBytes);
			content.Position = 0;
			PointerScanSnapshot actual = PointerScanFile.Load(content, TestContext.Current.CancellationToken);

			Assert.Equal((expected.MapName, expected.Target, expected.Width, expected.Incomplete,
				expected.CoverageIncomplete), (actual.MapName, actual.Target, actual.Width, actual.Incomplete,
				actual.CoverageIncomplete));
			Assert.Equal(expected.Paths.Length, actual.Paths.Length);
			for (int pathIndex = 0; pathIndex < expected.Paths.Length; pathIndex++)
			{
				PointerPath source = expected.Paths[pathIndex];
				PointerPath imported = actual.Paths[pathIndex];
				Assert.Equal((source.BaseAddress, source.Module, source.ModuleOffset),
					(imported.BaseAddress, imported.Module, imported.ModuleOffset));
				Assert.Equal(source.Offsets, imported.Offsets);
				Assert.Equal(PointerVerification.Unresolved, imported.Verification);
			}
		}
	}

	[Fact]
	public void SeededSchemaMutations_RejectBeforeReturningAnySnapshot()
	{
		Random random = new(Seed);
		for (int index = 0; index < SnapshotCount; index++)
		{
			TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
			string content = Mutation(index, random);
			using MemoryStream input = new(Encoding.UTF8.GetBytes(content));

			Exception error = Assert.ThrowsAny<Exception>(() =>
				PointerScanFile.Load(input, TestContext.Current.CancellationToken));
			Assert.True(error is InvalidDataException or System.Text.Json.JsonException,
				$"Seed {Seed}, case {index} returned {error.GetType().Name}: {content}");
			Assert.InRange(input.Position, 0, input.Length);
		}
	}

	private static PointerScanSnapshot CreateSnapshot(Random random, int index)
	{
		int width = random.Next(2) == 0 ? 4 : 8;
		ulong target = width == 4 ? (uint) random.NextInt64(0, (long) uint.MaxValue + 1) : NextUInt64(random);
		bool incomplete = random.Next(2) == 0;
		bool coverageIncomplete = incomplete && random.Next(2) == 0;
		PointerPath[] paths = new PointerPath[random.Next(0, MaximumGeneratedPaths + 1)];
		for (int pathIndex = 0; pathIndex < paths.Length; pathIndex++)
		{
			string? module = random.Next(2) == 0 ? null : $"module-{index}-{pathIndex}.dll";
			ulong address = width == 4 ? (uint) random.NextInt64(0, (long) uint.MaxValue + 1) : NextUInt64(random);
			ulong moduleOffset = module is null ? 0UL : (ulong) random.NextInt64(0, 1_048_577);
			long[] offsets = new long[random.Next(1, 9)];
			for (int offsetIndex = 0; offsetIndex < offsets.Length; offsetIndex++)
			{
				offsets[offsetIndex] = random.NextInt64(-1_048_576, 1_048_577);
			}

			paths[pathIndex] = new PointerPath(address, module, moduleOffset, offsets)
			{
				Verification = PointerVerification.LiveMatch
			};
		}

		return new PointerScanSnapshot($"seed-{Seed}-{index}", target, width, paths, incomplete, coverageIncomplete);
	}

	private static string Mutation(int index, Random random)
	{
		string mapName = $"seed-{Seed}-{index}";
		return (index % 4) switch
		{
			0 => $$"""{"version":2,"mapName":"{{mapName}}","target":0,"width":8,"paths":{},"incomplete":false}""",
			1 => $$"""{"version":2,"mapName":"{{mapName}}","target":"{{random.Next()}}","width":8,"paths":[],"incomplete":false}""",
			2 => $$"""{"version":2,"mapName":"{{mapName}}","target":0,"width":8,"paths":[{"baseAddress":0,"module":null,"moduleOffset":1,"offsets":[0]}],"incomplete":false}""",
			_ => $$"""{"version":2,"mapName":"{{mapName}}","target":0,"width":8,"paths":[{"baseAddress":0,"module":null,"moduleOffset":0,"offsets":[1048577]}],"incomplete":false}"""
		};
	}

	private static ulong NextUInt64(Random random)
	{
		Span<byte> bytes = stackalloc byte[sizeof(ulong)];
		random.NextBytes(bytes);
		return BitConverter.ToUInt64(bytes);
	}
}
