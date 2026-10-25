using System.Buffers.Binary;

namespace CheatEngine.Mcp.LiveTarget;

public sealed class DebuggerExecutionLoopBytesTests
{
	[Fact]
	public void Loop_UsesPublishedInstructionOffsetsAndRipRelativeCounters()
	{
		const long start = 0x0000_0001_0000_0000;
		const long sleep = 0x0000_7FFB_1234_5678;
		byte[] code = DebuggerExecutionLoopEncoding.Encode(unchecked((nint) start), unchecked((nint) sleep));

		Assert.Equal(76, code.Length);
		Assert.Equal((byte) 0xF0, code[18]);      // capture: lock inc [rip+hits]
		Assert.Equal((byte) 0x90, code[32]);      // run-to/step: published NOP
		Assert.Equal((byte) 0xC3, code[75]);
		Assert.Equal(start + 0x100, start + 7 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(2, 4)));
		Assert.Equal(start + 0x10C, start + 16 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(11, 4)));
		Assert.Equal(start + 0x104, start + 25 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(21, 4)));
		Assert.Equal(start + 0x108, start + 32 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(28, 4)));
		Assert.Equal(start + 0x110, start + 70 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(66, 4)));
		Assert.Equal(sleep, BinaryPrimitives.ReadInt64LittleEndian(code.AsSpan(44, 8)));
		Assert.Equal(75, 9 + unchecked((sbyte) code[8]));
		Assert.Equal(63, 18 + unchecked((sbyte) code[17]));
		Assert.Equal(0, 63 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(59, 4)));
		Assert.Equal(33, 75 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(71, 4)));
	}
}
