using System.Buffers;

namespace CheatEngine.Mcp.Core.Lua;

/// <summary>Reuses one JSON buffer across the Lua result copies of its owner.</summary>
/// <remarks>
///     Hold one instance per activation-scoped owner; product assemblies keep no static state. A rented buffer is empty;
///     a returned buffer is cleared, and it is dropped instead of cached once it grew beyond 1 MiB, so that one large
///     result does not stay in memory for the whole activation. Renting while the buffer is out yields a new one.
/// </remarks>
internal sealed class LuaJsonBufferPool
{
	internal const int RetainedBufferBytes = 1024 * 1024;
	private const int InitialBufferBytes = 4096;

	private ArrayBufferWriter<byte>? _cached;

	/// <summary>Takes the cached buffer, or a new one.</summary>
	/// <returns>An empty buffer to give back with <see cref="Return" />.</returns>
	internal ArrayBufferWriter<byte> Rent()
	{
		return Interlocked.Exchange(ref _cached, null) ?? new ArrayBufferWriter<byte>(InitialBufferBytes);
	}

	/// <summary>Clears a rented buffer and caches it unless it grew beyond <see cref="RetainedBufferBytes" />.</summary>
	/// <param name="buffer">The buffer from <see cref="Rent" />.</param>
	internal void Return(ArrayBufferWriter<byte> buffer)
	{
		ArgumentNullException.ThrowIfNull(buffer);
		if (buffer.Capacity > RetainedBufferBytes)
		{
			return;
		}

		buffer.Clear();
		Interlocked.Exchange(ref _cached, buffer);
	}
}
