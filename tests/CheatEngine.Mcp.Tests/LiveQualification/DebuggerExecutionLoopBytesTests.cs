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

		Assert.Equal(55, code.Length);
		Assert.Equal((byte) 0xF0, code[9]);       // capture: lock inc [rip+hits]
		Assert.Equal((byte) 0x90, code[23]);      // run-to/step: published NOP
		Assert.Equal((byte) 0xC3, code[54]);
		Assert.Equal(start + 0x100, start + 7 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(2, 4)));
		Assert.Equal(start + 0x104, start + 16 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(12, 4)));
		Assert.Equal(start + 0x108, start + 23 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(19, 4)));
		Assert.Equal(sleep, BinaryPrimitives.ReadInt64LittleEndian(code.AsSpan(35, 8)));
	}
}
