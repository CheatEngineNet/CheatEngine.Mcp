using System.Diagnostics;
using System.Globalization;
using System.Reflection.PortableExecutable;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Tests.LiveQualification.Infrastructure;

namespace CheatEngine.Mcp.Tests.LiveQualification;

internal sealed partial class LiveSandboxSession
{
	private void RequireInsideRun(string path)
	{
		if (!LiveQualificationOptIn.IsSameOrBelow(path, _layout.RunDirectory)
			|| Path.GetFullPath(path)
				.Equals(Path.GetFullPath(_layout.RunDirectory), StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("A sandbox file operation escaped the current run directory.");
		}
	}

	private static async Task WaitUntilAsync(Func<bool> ready, Process process, TimeSpan timeout)
	{
		Stopwatch elapsed = Stopwatch.StartNew();
		while (!ready())
		{
			if (process.HasExited || elapsed.Elapsed > timeout)
			{
				throw new TimeoutException("The disposable target did not become ready.");
			}

			await Task.Delay(50);
		}
	}

	private static void RequireNoCheatEngine()
	{
		foreach (Process process in Process.GetProcesses())
		{
			using (process)
			{
				if (process.ProcessName.StartsWith("cheatengine-", StringComparison.OrdinalIgnoreCase)
					|| process.ProcessName.Equals("cheatengine", StringComparison.OrdinalIgnoreCase)
					|| process.ProcessName.Equals("Cheat Engine", StringComparison.OrdinalIgnoreCase)
					|| process.ProcessName.StartsWith("gtutorial", StringComparison.OrdinalIgnoreCase))
				{
					throw new InvalidOperationException(
						$"Close {process.ProcessName} (PID {process.Id}) before live tests; it will not be stopped automatically.");
				}
			}
		}
	}

	internal static string FindRepository()
	{
		for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
			 directory is not null;
			 directory = directory.Parent)
		{
			if (File.Exists(Path.Combine(directory.FullName, "CheatEngine.Mcp.slnx")))
			{
				return directory.FullName;
			}
		}

		throw new DirectoryNotFoundException("Run live tests from the CheatEngine.Mcp checkout.");
	}

	private string HostInstallationDirectory(string name)
	{
		return Path.Combine(_layout.RunDirectory, $"ce-{name}");
	}

	private LiveCompilerRoots CompilerRoots(string name)
	{
		if (!_options.CompilerQualification)
		{
			throw new InvalidOperationException("Compiler roots were not admitted for this live session.");
		}

		string root = Path.Combine(_layout.RunDirectory, "compiler", name);
		string temp = _options.TempTopology == CompilerTempTopology.Shared
			? Path.Combine(_layout.RunDirectory, "compiler", "shared", "temp")
			: Path.Combine(root, "temp");
		return new LiveCompilerRoots(temp, Path.Combine(root, "references"),
			Path.Combine(root, "output"));
	}

	private void CreateCompilerRoots(string name)
	{
		LiveCompilerRoots roots = CompilerRoots(name);
		foreach (string path in new[] { roots.Temp, roots.References, roots.Output })
		{
			RequireInsideRun(path);
			Directory.CreateDirectory(path);
		}
	}
}
