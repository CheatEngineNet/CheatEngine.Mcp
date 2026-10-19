using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;

using CheatEngine.Client;
using CheatEngine.Client.Memory;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.SDK.Engine.Values;

using Xunit.Sdk;

namespace CheatEngine.Mcp.Tests.Fuzzing;

/// <summary>Seeded boundary corpus for public value-text parsing. It never contacts a Cheat Engine host.</summary>
public sealed class ValueBoundaryFuzzTests
{
	private const int Seed = 0x51EED;
	private const int CasesPerType = 96;
	private static readonly Address Target = new(0x1000);

	[Fact]
	public void SeededNumericCorpus_EncodesValidValuesWithAnIndependentBitOracle()
	{
		Random random = new(Seed);
		foreach (NumericCase @case in NumericCases)
		{
			for (int index = 0; index < CasesPerType; index++)
			{
				TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
				ulong bits = NextUInt64(random) & Mask(@case.Bits);
				string text = @case.Signed
					? ToSigned(bits, @case.Bits).ToString(CultureInfo.InvariantCulture)
					: bits.ToString(CultureInfo.InvariantCulture);

				byte[] actual = McpValueCodec.Encode(@case.Type, text, 8, "value");
				Assert.Equal(IndependentLittleEndian(bits, @case.Bits / 8), actual);
			}
		}
	}

	[Fact]
	public void SeededInvalidValueCorpus_IsTypedAndRefusedBeforeEveryMemoryCall()
	{
		Random random = new(Seed);
		foreach (NumericCase @case in NumericCases)
		{
			foreach (string value in InvalidValues(@case, random))
			{
				TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
				CallCountingMemory memory = new();
				ICheatEngineClient client = ClientTestDouble.Client((nameof(ICheatEngineClient.Memory), memory.Client));

				CheatEngineToolException error = Assert.Throws<CheatEngineToolException>(() =>
					McpValueCodec.Write(client, Target, @case.Type, value, TestContext.Current.CancellationToken));

				Assert.Equal(ToolErrorKind.InvalidArgument, error.Error.Kind);
				Assert.Equal("value", error.Error.Details!.Value.GetProperty("parameter").GetString());
				Assert.Equal(0, memory.CallCount);
			}
		}
	}

	[Fact]
	public void SeededHexCorpus_AgreesWithTheBclAndPreservesExplicitLimits()
	{
		Random random = new(Seed);
		for (int index = 0; index < CasesPerType; index++)
		{
			TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
			byte[] expected = new byte[random.Next(2, 33)];
			random.NextBytes(expected);
			string text = string.Join(index % 2 == 0 ? "," : " ", expected.Select(static value =>
				"0x" + value.ToString("X2", CultureInfo.InvariantCulture)));

			Assert.Equal(expected, HexParse.Bytes(text, "bytes", 32));
			CheatEngineToolException error = Assert.Throws<CheatEngineToolException>(() =>
				HexParse.Bytes(text, "bytes", expected.Length - 1));
			Assert.Equal(ToolErrorKind.LimitExceeded, error.Error.Kind);
			Assert.Equal("bytes", error.Error.Details!.Value.GetProperty("parameter").GetString());
		}
	}

	private static IEnumerable<string> InvalidValues(NumericCase @case, Random random)
	{
		yield return string.Empty;
		yield return "+";
		yield return "0x";
		yield return "0xG";
		yield return "1_0";
		yield return "12\0";
		yield return (BigInteger.One << @case.Bits).ToString(CultureInfo.InvariantCulture);
		if (@case.Signed)
		{
			yield return (BigInteger.One << (@case.Bits - 1)).ToString(CultureInfo.InvariantCulture);
			yield return (-(BigInteger.One << (@case.Bits - 1)) - 1).ToString(CultureInfo.InvariantCulture);
		}

		for (int index = 0; index < 4; index++)
		{
			yield return new string('9', @case.Bits + 2 + random.Next(1, 9));
		}
	}

	private static ulong NextUInt64(Random random)
	{
		Span<byte> bytes = stackalloc byte[sizeof(ulong)];
		random.NextBytes(bytes);
		return BinaryPrimitives.ReadUInt64LittleEndian(bytes);
	}

	private static ulong Mask(int bits) => bits == 64 ? ulong.MaxValue : (1UL << bits) - 1;

	private static long ToSigned(ulong bits, int width)
	{
		ulong sign = 1UL << (width - 1);
		return (bits & sign) == 0 ? (long) bits : unchecked((long) (bits | ~Mask(width)));
	}

	private static byte[] IndependentLittleEndian(ulong bits, int length)
	{
		byte[] bytes = new byte[length];
		for (int index = 0; index < bytes.Length; index++)
		{
			bytes[index] = (byte) (bits >> (index * 8));
		}

		return bytes;
	}

	private sealed record NumericCase(McpValueType Type, int Bits, bool Signed);

	private static readonly NumericCase[] NumericCases =
	[
		new(McpValueType.Int8, 8, true), new(McpValueType.UInt8, 8, false),
		new(McpValueType.Int16, 16, true), new(McpValueType.UInt16, 16, false),
		new(McpValueType.Int32, 32, true), new(McpValueType.UInt32, 32, false),
		new(McpValueType.Int64, 64, true), new(McpValueType.UInt64, 64, false)
	];

	private sealed class CallCountingMemory
	{
		internal CallCountingMemory()
		{
			Client = ClientTestDouble.Create<IMemoryClient>((method, _) =>
			{
				CallCount++;
				throw new XunitException($"Invalid input reached memory method '{method.Name}'.");
			});
		}

		internal IMemoryClient Client
		{
			get;
		}
		internal int CallCount
		{
			get; private set;
		}
	}
}
