using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;

internal static class Program
{
	private static int Main(string[] args)
	{
		if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0])) return 64;
		if (!Path.IsPathRooted(args[0])) return 64;
		string manifest = Path.GetFullPath(args[0]);
		if (!string.Equals(manifest, args[0], StringComparison.OrdinalIgnoreCase)) return 64;
		File.WriteAllText(manifest + ".tmp", "{\"processId\":" + Process.GetCurrentProcess().Id +
			",\"pointerWidth\":" + (IntPtr.Size * 8) + ",\"runtime\":\"" + Environment.Version +
			"\",\"assembly\":\"" + Assembly.GetExecutingAssembly().GetName().Name + "\",\"sha256\":\"" +
			Hash(Assembly.GetExecutingAssembly().Location) + "\"}");
		File.Move(manifest + ".tmp", manifest);
		string stop = manifest + ".stop";
		Stopwatch lifetime = Stopwatch.StartNew();
		while (!File.Exists(stop) && lifetime.Elapsed < TimeSpan.FromMinutes(10)) System.Threading.Thread.Sleep(100);
		return 0;
	}

	private static string Hash(string path)
	{
		using (SHA256 sha = SHA256.Create())
		using (FileStream stream = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
	}
}
