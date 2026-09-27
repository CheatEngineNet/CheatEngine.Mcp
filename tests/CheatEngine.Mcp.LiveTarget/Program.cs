using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace CheatEngine.Mcp.LiveTarget;

internal static class Program
{
	private const int InitialValue = 20260926;
	private static readonly TimeSpan MaximumLifetime = TimeSpan.FromMinutes(10);

	private static int Main(string[] args)
	{
		if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
		{
			return 64;
		}

		string manifestPath = Path.GetFullPath(args[0]);
		string stopMarkerPath = $"{manifestPath}.stop";
		IntPtr allocation = Marshal.AllocHGlobal(64);
		try
		{
			Marshal.WriteInt32(allocation, InitialValue);
			string address = $"0x{unchecked((ulong) allocation.ToInt64()):X}";
			string manifest = JsonSerializer.Serialize(new
			{
				processId = Environment.ProcessId, address, initialValue = InitialValue
			});
			File.WriteAllText(manifestPath + ".tmp", manifest);
			File.Move(manifestPath + ".tmp", manifestPath);

			Stopwatch lifetime = Stopwatch.StartNew();
			while (lifetime.Elapsed < MaximumLifetime && !File.Exists(stopMarkerPath))
			{
				Thread.Sleep(50);
			}

			return 0;
		}
		finally
		{
			Marshal.FreeHGlobal(allocation);
		}
	}
}
