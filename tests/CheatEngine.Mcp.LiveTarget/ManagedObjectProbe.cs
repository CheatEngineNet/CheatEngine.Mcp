using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CheatEngine.Mcp.LiveTarget;

/// <summary>A pinned, owned managed object whose reference is published directly, never reconstructed from a layout offset.</summary>
[StructLayout(LayoutKind.Sequential)]
internal sealed class ManagedObjectProbe
{
	public int Marker;
	public int Mirror;
	public int Toggle;

	internal ManagedObjectProbe(int marker, int mirror, int toggle)
	{
		Marker = marker;
		Mirror = mirror;
		Toggle = toggle;
	}

	internal static nint AddressOf(ManagedObjectProbe value)
	{
		ArgumentNullException.ThrowIfNull(value);
		// This reads the GC object reference itself. The caller owns a pinned GCHandle for its complete lifetime.
		return Unsafe.As<ManagedObjectProbe, nint>(ref value);
	}
}
