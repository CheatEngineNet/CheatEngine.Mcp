using System.Buffers;
using System.Text;

using CheatEngine.Mcp.Tests.Support;

namespace CheatEngine.Mcp.Tests.Core;

public sealed class LuaJsonWriterTests
{
	[Fact]
	public void Deserialize_CamelCaseJson_FillsTheSourceGeneratedRecord()
	{
		byte[] json = Encoding.UTF8.GetBytes(
			"""{"ok":true,"returnValues":[1,"two",null]}""");

		LuaExecuteOutcome outcome = LuaJsonWriter.Deserialize(json, TestJsonContext.Default.LuaExecuteOutcome);

		Assert.True(outcome.Ok);
		Assert.Null(outcome.Phase);
		Assert.Equal(["1", "\"two\"", "null"], outcome.ReturnValues!.Select(value => value.GetRawText()));
	}

	[Fact]
	public void Deserialize_NullOrMismatchedJson_IsAContractViolation()
	{
		foreach (string json in new[] { "null", "{\"ok\":\"yes\"}", "{}", "[1]", "{\"ok\":true" })
		{
			LuaJsonException exception = Assert.Throws<LuaJsonException>(() =>
				LuaJsonWriter.Deserialize(Encoding.UTF8.GetBytes(json), TestJsonContext.Default.LuaExecuteOutcome));
			Assert.Equal(LuaJsonViolation.Contract, exception.Violation);
			Assert.Contains(nameof(LuaExecuteOutcome), exception.Message, StringComparison.Ordinal);
		}
	}

	[Fact]
	public void BufferPool_AfterReturn_ReusesAClearedBufferUnlessItGrewPastOneMebibyte()
	{
		LuaJsonBufferPool pool = new();
		ArrayBufferWriter<byte> first = pool.Rent();
		first.Write("{}"u8);
		pool.Return(first);

		ArrayBufferWriter<byte> second = pool.Rent();
		Assert.Same(first, second);
		Assert.Equal(0, second.WrittenCount);
		Assert.NotSame(second, pool.Rent());

		second.GetSpan(LuaJsonBufferPool.RetainedBufferBytes + 1);
		second.Advance(LuaJsonBufferPool.RetainedBufferBytes + 1);
		pool.Return(second);
		Assert.NotSame(second, pool.Rent());
	}
}
