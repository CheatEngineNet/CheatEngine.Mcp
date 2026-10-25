namespace CheatEngine.Mcp.LiveTarget;

internal static class DebuggerExecutionLoopEncoding
{
	internal const int Length = 76;

	internal static byte[] Encode(nint address, nint sleep)
	{
		byte[] code = [0x83, 0x3D, 0, 0, 0, 0, 0, 0x75, 0x42, 0x83, 0x3D, 0, 0, 0, 0, 0,
			0x75, 0x2D, 0xF0, 0xFF, 0x05, 0, 0, 0, 0, 0xF0, 0xFF, 0x05, 0, 0, 0, 0, 0x90,
			0x48, 0x83, 0xEC, 0x28, 0xB9, 0x01, 0, 0, 0, 0x48, 0xB8, 0, 0, 0, 0, 0, 0, 0, 0,
			0xFF, 0xD0, 0x48, 0x83, 0xC4, 0x28, 0xE9, 0xC1, 0xFF, 0xFF, 0xFF,
			0xF0, 0xFF, 0x05, 0, 0, 0, 0, 0xE9, 0xD6, 0xFF, 0xFF, 0xFF, 0xC3];
		Patch(code, 2, checked((long) address + 0x100), checked((long) address + 7));
		Patch(code, 11, checked((long) address + 0x10C), checked((long) address + 16));
		Patch(code, 21, checked((long) address + 0x104), checked((long) address + 25));
		Patch(code, 28, checked((long) address + 0x108), checked((long) address + 32));
		BitConverter.GetBytes((long) sleep).CopyTo(code, 44);
		Patch(code, 66, checked((long) address + 0x110), checked((long) address + 70));
		return code;
	}

	private static void Patch(byte[] code, int offset, long target, long next) =>
		BitConverter.GetBytes(checked((int) (target - next))).CopyTo(code, offset);
}
