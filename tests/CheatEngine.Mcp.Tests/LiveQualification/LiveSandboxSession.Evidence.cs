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
	public async Task<JsonNode> CompilerProbeAsync(string hostName, string command)
	{
		if (!_options.CompilerQualification || command is not ("status" or "compile" or "armHold"))
		{
			throw new ArgumentOutOfRangeException(nameof(command));
		}

		if (!_hosts.TryGetValue(hostName, out OwnedHost? host) || host.Process is null || host.Stopped)
		{
			throw new InvalidOperationException($"Owned host '{hostName}' is not running.");
		}

		string request = Path.Combine(_layout.RunDirectory, $"compiler-request-{hostName}.txt");
		string response = Path.Combine(_layout.RunDirectory, $"compiler-response-{hostName}.txt");
		RequireInsideRun(request);
		RequireInsideRun(response);
		File.Delete(response);
		await File.WriteAllTextAsync(request + ".tmp", command);
		File.Move(request + ".tmp", request);
		await WaitUntilAsync(() => File.Exists(response), host.Process, TimeSpan.FromSeconds(10));
		return await LiveCompilerProbeResponse.ReadAsync(response, command);
	}

	/// <summary>Runs one fixed private debugger diagnostic command, consuming the previous receipt before publication.</summary>
	public async Task<LiveDebuggerTransitionReceipt> DebuggerTransitionProbeAsync(string hostName, string command,
		int? threadId = null, ulong? rip = null)
	{
		if (!_options.DebuggerTransitionDiagnostic || command is not ("install" or "sample" or "restore"))
		{
			throw new ArgumentOutOfRangeException(nameof(command));
		}
		if (!_hosts.TryGetValue(hostName, out OwnedHost? host) || host.Process is null || host.Stopped)
		{
			throw new InvalidOperationException($"Owned host '{hostName}' is not running.");
		}
		LiveDebuggerExecutionQualification.Manifest manifest = host.Public.DebuggerExecution
			?? throw new InvalidDataException("The private debugger target manifest is missing.");
		if (command == "install" ? threadId != manifest.ThreadId || rip != manifest.Entry : threadId is not null || rip is not null)
		{
			throw new ArgumentException("The private debugger probe accepts only its owned manifest identity.");
		}
		string request = Path.Combine(_layout.RunDirectory, $"debugger-transition-request-{hostName}.txt");
		string response = Path.Combine(_layout.RunDirectory, $"debugger-transition-response-{hostName}.txt");
		RequireInsideRun(request);
		RequireInsideRun(request + ".tmp");
		RequireInsideRun(response);
		if (File.Exists(request))
		{
			throw new InvalidOperationException("The prior private debugger command has not been consumed.");
		}
		string nonce = Interlocked.Increment(ref _debuggerTransitionProbeSequence).ToString(CultureInfo.InvariantCulture);
		string body = command == "install"
			? $"{nonce}\n{command}\n{threadId!.Value.ToString(CultureInfo.InvariantCulture)}\n{rip!.Value.ToString("X", CultureInfo.InvariantCulture)}\n"
			: $"{nonce}\n{command}\n";
		File.Delete(response);
		await File.WriteAllTextAsync(request + ".tmp", body);
		File.Move(request + ".tmp", request);
		await WaitUntilAsync(() => File.Exists(response), host.Process, TimeSpan.FromSeconds(10));
		await using FileStream stream = new(response, FileMode.Open, FileAccess.Read, FileShare.Read);
		if (stream.Length > 2048)
		{
			throw new InvalidDataException("The debugger diagnostic receipt exceeded its bound.");
		}
		using StreamReader reader = new(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: false);
		List<string> lines = [];
		while (await reader.ReadLineAsync() is { } line)
		{
			if (lines.Count >= 28 || line.Length > 256)
			{
				throw new InvalidDataException("The debugger diagnostic receipt exceeded its line bounds.");
			}
			lines.Add(line);
		}
		Record("debugger_transition_diagnostic_receipt", new
		{
			hostName,
			nonce,
			command,
			lines
		});
		return LiveDebuggerTransitionReceipt.Parse([.. lines], nonce, command);
	}

	/// <summary>Reads the fixed diagnostic bytes from a still-owned debugger transition target.</summary>
	internal LiveDebuggerTransitionMemory.Snapshot ReadDebuggerTransitionMemory(string hostName)
	{
		if (!_options.DebuggerTransitionDiagnostic)
		{
			throw new InvalidOperationException("The debugger transition memory diagnostic is not admitted.");
		}
		if (!_hosts.TryGetValue(hostName, out OwnedHost? host) || host.Target is null || host.Stopped)
		{
			throw new InvalidOperationException($"Owned target '{hostName}' is not running.");
		}

		LiveDebuggerExecutionQualification.Manifest manifest = host.Public.DebuggerExecution
			?? throw new InvalidDataException("The private debugger target manifest is missing.");
		host.Target.Refresh();
		if (host.Target.HasExited || host.Target.Id != host.Public.TargetProcessId || manifest.ProcessId != host.Target.Id)
		{
			throw new InvalidOperationException("The debugger transition diagnostic target identity no longer matches the owned manifest.");
		}

		return LiveDebuggerTransitionMemory.Read(host.Target, manifest);
	}

	/// <summary>Restarts only the disposable target for one still-running private CE host and publishes a fresh manifest.</summary>

	public void Record(string name, object evidence)
	{
		_report[name] = evidence;
		File.AppendAllText(_layout.ReceiptsPath,
			JsonSerializer.Serialize(new
			{
				name,
				evidence
			}) + Environment.NewLine);
	}

	public void MarkPassed()
	{
		_passed = true;
	}

	internal async Task<string[]> ScanUiAsync(string name, string action)
	{
		if (action is not ("prepare" or "snapshot" or "manual" or "hide"))
		{
			throw new ArgumentOutOfRangeException(nameof(action));
		}

		OwnedHost host = _hosts[name];
		string response = Path.Combine(_layout.RunDirectory, $"scan-response-{name}.txt");
		File.Delete(response);
		await File.WriteAllTextAsync(Path.Combine(_layout.RunDirectory, $"scan-request-{name}.txt"), action);
		await WaitUntilAsync(() => File.Exists(response), host.Process!, TimeSpan.FromSeconds(10));
		string[] lines = await File.ReadAllLinesAsync(response);
		return lines.Length != 8 || lines[0] != "ok"
			? throw new InvalidOperationException($"Native scan UI probe failed: {string.Join("; ", lines)}")
			: lines;
	}
}
