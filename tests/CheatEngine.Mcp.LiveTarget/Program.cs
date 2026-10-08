using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace CheatEngine.Mcp.LiveTarget;

internal static class Program
{
	private const int InitialValue = 20260926;
	private static readonly TimeSpan MaximumLifetime = TimeSpan.FromMinutes(10);
	private static readonly TimeSpan SoakMaximumLifetime = TimeSpan.FromMinutes(135);
	private static readonly string[] PointerOffsets = ["-10", "20"];

	private static int Main(string[] args)
	{
		if (args.Length is 2 or 3 && string.Equals(args[0], "--probe-plugin", StringComparison.Ordinal))
		{
			return PluginBundleProbe.Run(args[1], args.Length == 3 ? args[2] : null);
		}

		bool soak = args.Length == 2 && string.Equals(args[0], "--soak", StringComparison.Ordinal);
		if ((!soak && args.Length != 1) || (soak && string.IsNullOrWhiteSpace(args[1]))
			|| (!soak && string.IsNullOrWhiteSpace(args[0])))
		{
			return 64;
		}

		string manifestPath = Path.GetFullPath(soak ? args[1] : args[0]);
		string stopMarkerPath = $"{manifestPath}.stop";
		IntPtr direct = IntPtr.Zero;
		IntPtr root = IntPtr.Zero;
		IntPtr middle = IntPtr.Zero;
		IntPtr final = IntPtr.Zero;
		IntPtr zeroRoot = IntPtr.Zero;
		try
		{
			direct = Marshal.AllocHGlobal(64);
			root = Marshal.AllocHGlobal(64);
			middle = Marshal.AllocHGlobal(64);
			final = Marshal.AllocHGlobal(64);
			zeroRoot = Marshal.AllocHGlobal(IntPtr.Size);
			// A fixed data-only decode range for bounded code jobs; it is never executed.
			Marshal.Copy(Enumerable.Repeat((byte) 0x90, 64).ToArray(), 0, direct, 64);
			Marshal.WriteInt32(direct, InitialValue);
			Marshal.WriteInt32(final, InitialValue);
			// dereference(root) - 0x10 == middle; dereference(middle) + 0x20 == final.
			Marshal.WriteIntPtr(root, IntPtr.Add(middle, 0x10));
			Marshal.WriteIntPtr(middle, IntPtr.Subtract(final, 0x20));
			Marshal.WriteIntPtr(zeroRoot, IntPtr.Zero);
			string address = Address(direct);
			string manifest = JsonSerializer.Serialize(new
			{
				processId = Environment.ProcessId,
				pointerWidth = IntPtr.Size * 8,
				address,
				initialValue = InitialValue,
				pointerRootAddress = Address(root),
				pointerTargetAddress = Address(final),
				zeroPointerRootAddress = Address(zeroRoot),
				unreadableRootAddress = "0x1",
				pointerOffsets = PointerOffsets
			});
			File.WriteAllText(manifestPath + ".tmp", manifest);
			File.Move(manifestPath + ".tmp", manifestPath);

			Stopwatch lifetime = Stopwatch.StartNew();
			TimeSpan maximumLifetime = soak ? SoakMaximumLifetime : MaximumLifetime;
			while (lifetime.Elapsed < maximumLifetime && !File.Exists(stopMarkerPath))
			{
				Thread.Sleep(50);
			}

			return 0;
		}
		finally
		{
			Free(zeroRoot);
			Free(final);
			Free(middle);
			Free(root);
			Free(direct);
		}
	}

	private static string Address(IntPtr value)
	{
		ulong bits = IntPtr.Size == 4 ? unchecked((uint) value.ToInt32()) : unchecked((ulong) value.ToInt64());
		return $"0x{bits:X}";
	}

	private static void Free(IntPtr allocation)
	{
		if (allocation != IntPtr.Zero)
		{
			Marshal.FreeHGlobal(allocation);
		}
	}
}
