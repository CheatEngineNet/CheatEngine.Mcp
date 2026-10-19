using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Owns the fixed private Mono fixture process and validates its atomic manifest.</summary>
[SupportedOSPlatform("windows")]
internal sealed class LiveMonoTarget : IAsyncDisposable
{
	private const string SourceHash = "81F48A0760C9A1646F0F03C16BE1BF7603872932F0F203738579FB00938275F8";
	private const string FixtureHash = "19F2D71A92243740D8D4577EA8CCD46D8CEB647EB6AF0DD9E8BED5C027130A81";
	private const string MonoHash = "70AFA7611A4913DEB03C84F9EE0C1E24CED8EE0558EA8C4D60317637417BF0EC";
	private const string NativeHash = "07A1DEBE6CED8E31FB944046EB6531D41969BAE1F686CF49A13F06877D77D55D";
	private const string MscorlibHash = "C7512C133808ACB42E48B6C1B879AB6F3C988A9E7A83B6A80E75A9FF38B6E5CF";
	private const string ConfigHash = "B6B3E96E9D8EFA31EC6B671F5EA0CBF2D058152BC782B3C53865EBCFBBBCCB1E";
	private const int MaximumInputBytes = 16 * 1024;
	private const int MaximumOutputChars = 4096;
	private readonly Process _process;
	private readonly string _root;
	private readonly Task<string> _stdout;
	private readonly Task<string> _stderr;
	private bool _stopped;

	private LiveMonoTarget(Process process, string root, Manifest manifest, Task<string> stdout, Task<string> stderr)
	{
		_process = process;
		_root = root;
		Facts = manifest;
		_stdout = stdout;
		_stderr = stderr;
	}

	internal Manifest Facts
	{
		get;
	}
	internal int ProcessId => _process.Id;

	internal static async Task<LiveMonoTarget> StartAsync(LiveSandboxSession sandbox, string repositoryRoot,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(sandbox);
		string repository = Path.GetFullPath(repositoryRoot);
		string runtime = Path.Combine(repository, "artifacts", "fixtures", "mono-6.12.0.206-x64", "extracted", "runtime");
		string mono = Path.Combine(runtime, "bin", "mono.exe");
		string config = Path.Combine(runtime, "etc", "mono", "config");
		string assembly = Path.Combine(repository, "artifacts", "live-mono-target", "CheatEngine.Mcp.LiveMonoTarget.exe");
		string identity = Path.Combine(Path.GetDirectoryName(assembly)!, "fixture-identity.json");
		string native = Path.Combine(runtime, "bin", "mono-2.0-sgen.dll");
		string mscorlib = Path.Combine(runtime, "lib", "mono", "4.5", "mscorlib.dll");
		foreach (string path in new[] { repository, runtime, mono, config, native, mscorlib, assembly, identity })
		{
			LiveCompilerCandidate.RequireNoReparseAncestors(path);
		}

		if (sandbox.TargetArchitecture != "x64" || !File.Exists(mono) || !File.Exists(config) || !File.Exists(native) || !File.Exists(mscorlib) || !File.Exists(assembly) || !File.Exists(identity))
		{
			throw new FileNotFoundException("The reviewed x64 Mono fixture or its private runtime is unavailable.", assembly);
		}
		JsonNode identityNode = ReadBoundedJson(identity);
		string source = identityNode["sourcePath"]?.GetValue<string>() ?? string.Empty;
		if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
		{
			throw new InvalidDataException("The Mono fixture identity has no source path.");
		}

		LiveCompilerCandidate.RequireNoReparseAncestors(source);
		string assemblyHash = Hash(assembly);
		if (!string.Equals(assemblyHash, FixtureHash, StringComparison.OrdinalIgnoreCase) || !string.Equals(Hash(mono), MonoHash, StringComparison.OrdinalIgnoreCase) || !string.Equals(Hash(native), NativeHash, StringComparison.OrdinalIgnoreCase) || !string.Equals(Hash(mscorlib), MscorlibHash, StringComparison.OrdinalIgnoreCase) || !string.Equals(Hash(config), ConfigHash, StringComparison.OrdinalIgnoreCase) ||
			!string.Equals(identityNode["targetSha256"]?.GetValue<string>(), FixtureHash, StringComparison.OrdinalIgnoreCase) || identityNode["architecture"]?.GetValue<string>() != "x64" ||
			!string.Equals(Path.GetFullPath(identityNode["targetPath"]?.GetValue<string>() ?? string.Empty), assembly, StringComparison.OrdinalIgnoreCase) ||
			!string.Equals(Path.GetFullPath(identityNode["monoPath"]?.GetValue<string>() ?? string.Empty), mono, StringComparison.OrdinalIgnoreCase) ||
			!string.Equals(identityNode["monoSha256"]?.GetValue<string>(), MonoHash, StringComparison.OrdinalIgnoreCase) ||
			!IdentityPathAndHash(identityNode, "monoNativeLibrary", native, NativeHash) || !IdentityPathAndHash(identityNode, "runtimeConfig", config, ConfigHash) || !IdentityPathAndHash(identityNode, "mscorlib", mscorlib, MscorlibHash) ||
			!string.Equals(Hash(source), SourceHash, StringComparison.OrdinalIgnoreCase) || !string.Equals(identityNode["sourceSha256"]?.GetValue<string>(), SourceHash, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("The reviewed Mono fixture bytes do not match the pinned identity.");
		}

		string root = Path.Combine(sandbox.RunDirectory, "mono-qualification", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
		string nonce = Guid.NewGuid().ToString("N");
		string manifestPath = Path.Combine(root, $"mono-owned-{nonce}.manifest.json");
		string failurePath = Path.Combine(root, $"mono-owned-{nonce}.failure.txt");
		ProcessStartInfo start = new(mono)
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			WorkingDirectory = root,
			RedirectStandardOutput = true,
			RedirectStandardError = true
		};
		start.ArgumentList.Add("--config");
		start.ArgumentList.Add(config);
		start.ArgumentList.Add(assembly);
		start.ArgumentList.Add("--root");
		start.ArgumentList.Add(root);
		start.ArgumentList.Add("--nonce");
		start.ArgumentList.Add(nonce);
		start.Environment["MONO_PATH"] = Path.Combine(runtime, "lib", "mono", "4.5");
		start.Environment["MONO_CFG_DIR"] = Path.Combine(runtime, "etc");
		foreach (string key in start.Environment.Keys.Where(static key => key.StartsWith("MONO_", StringComparison.OrdinalIgnoreCase) || key.StartsWith("DOTNET_", StringComparison.OrdinalIgnoreCase) || key.StartsWith("COMPLUS_", StringComparison.OrdinalIgnoreCase) || key.StartsWith("COR_", StringComparison.OrdinalIgnoreCase) || key.StartsWith("CHEATENGINE_", StringComparison.OrdinalIgnoreCase) || key.StartsWith("MCP_", StringComparison.OrdinalIgnoreCase)).ToArray())
		{
			start.Environment.Remove(key);
		}

		start.Environment["MONO_PATH"] = Path.Combine(runtime, "lib", "mono", "4.5");
		start.Environment["MONO_CFG_DIR"] = Path.Combine(runtime, "etc");
		Process process = Process.Start(start) ?? throw new InvalidOperationException("The owned Mono fixture did not start.");
		Task<string> stdout = ReadBoundedAsync(process.StandardOutput);
		Task<string> stderr = ReadBoundedAsync(process.StandardError);
		try
		{
			JsonNode node = await WaitForManifestAsync(manifestPath, failurePath, process, cancellationToken);
			Manifest facts = Manifest.Parse(node, nonce, process.Id, root, assembly, assemblyHash);
			process.Refresh();
			if (process.HasExited || !string.Equals(Path.GetFullPath(process.MainModule?.FileName ?? string.Empty), Path.GetFullPath(mono), StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidDataException("The owned Mono fixture process identity changed before qualification.");
			}
			return new LiveMonoTarget(process, root, facts, stdout, stderr);
		}
		catch (Exception startup)
		{
			try
			{
				await TerminateAndProveAsync(process);
				process.Dispose();
				await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(2), CancellationToken.None);
			}
			catch (Exception cleanup)
			{
				try
				{
					await WaitForNaturalExpiryAsync(process);
					process.Dispose();
				}
				catch (Exception expiry) { throw new AggregateException(startup, cleanup, expiry); }
				throw new AggregateException(startup, cleanup);
			}
			throw;
		}
	}

	public async ValueTask DisposeAsync()
	{
		if (_stopped)
		{
			return;
		}

		List<Exception> failures = [];
		try
		{
			if (!string.Equals(Path.GetFullPath(Facts.StopMarker), Path.Combine(_root, "mono-owned-" + Facts.Nonce + ".stop"), StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidDataException("The Mono stop marker is not owned by this fixture.");
			}

			_process.Refresh();
			if (_process.HasExited)
			{
				throw new InvalidOperationException("The owned Mono fixture exited before graceful marker cleanup.");
			}

			File.WriteAllText(Facts.StopMarker, "stop");
			await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
			if (_process.ExitCode != 0)
			{
				throw new InvalidOperationException($"Owned Mono fixture exited {_process.ExitCode}.");
			}

			_stopped = true;
			await ObserveOutputAsync();
		}
		catch (Exception exception)
		{
			failures.Add(exception);
			if (!_stopped)
			{
				try
				{
					await TerminateAndProveAsync(_process);
					_stopped = true;
					await ObserveOutputAsync();
				}
				catch (Exception kill) { failures.Add(kill); }
			}
		}
		finally
		{
			if (_stopped)
			{
				_process.Dispose();
			}
		}
		if (failures.Count != 0)
		{
			throw new AggregateException("The owned Mono fixture did not stop cleanly.", failures);
		}
	}

	private static async Task TerminateAndProveAsync(Process process)
	{
		process.Refresh();
		if (!process.HasExited)
		{
			process.Kill(true);
		}

		await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
		process.Refresh();
		if (!process.HasExited)
		{
			throw new TimeoutException("The owned Mono fixture did not exit after forced termination.");
		}
	}
	private static async Task WaitForNaturalExpiryAsync(Process process)
	{
		await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(125));
		process.Refresh();
		if (!process.HasExited)
		{
			throw new TimeoutException("The owned Mono fixture did not reach its bounded natural expiry.");
		}
	}
	private static async Task<string> ReadBoundedAsync(StreamReader reader)
	{
		char[] buffer = new char[512];
		StringBuilder output = new();
		while (true)
		{
			int read = await reader.ReadAsync(buffer);
			if (read == 0)
			{
				return output.ToString();
			}

			if (output.Length + read > MaximumOutputChars)
			{
				throw new InvalidDataException("The owned Mono fixture output exceeded its bound.");
			}

			output.Append(buffer, 0, read);
		}
	}
	private async Task ObserveOutputAsync()
	{
		string[] output = await Task.WhenAll(_stdout, _stderr).WaitAsync(TimeSpan.FromSeconds(2));
		if (output.Any(static text => text.Length != 0))
		{
			throw new InvalidDataException("The owned Mono fixture wrote unexpected output.");
		}
	}
	private static JsonNode ReadBoundedJson(string path)
	{
		if (new FileInfo(path).Length is <= 0 or > MaximumInputBytes)
		{
			throw new InvalidDataException("Mono fixture input exceeds its bound.");
		}

		return JsonNode.Parse(File.ReadAllText(path)) ?? throw new InvalidDataException("Mono fixture input is empty.");
	}

	private static async Task<JsonNode> WaitForManifestAsync(string manifest, string failure, Process process, CancellationToken token)
	{
		Stopwatch deadline = Stopwatch.StartNew();
		while (!File.Exists(manifest))
		{
			process.Refresh();
			if (process.HasExited)
			{
				throw new InvalidOperationException($"The owned Mono fixture exited {process.ExitCode} before publishing its manifest.");
			}

			if (File.Exists(failure))
			{
				if (new FileInfo(failure).Length > MaximumInputBytes)
				{
					throw new InvalidDataException("The owned Mono fixture failure exceeds its bound.");
				}

				throw new InvalidDataException("The owned Mono fixture reported startup failure: " + await File.ReadAllTextAsync(failure, token));
			}
			if (deadline.Elapsed >= TimeSpan.FromSeconds(15))
			{
				throw new TimeoutException("The owned Mono fixture did not publish its manifest.");
			}

			await Task.Delay(50, token);
		}
		if (new FileInfo(manifest).Length is <= 0 or > MaximumInputBytes)
		{
			throw new InvalidDataException("The Mono manifest exceeds its bound.");
		}

		return JsonNode.Parse(await File.ReadAllTextAsync(manifest, token)) ?? throw new InvalidDataException("The Mono manifest is empty.");
	}

	private static string Hash(string path)
	{
		using FileStream file = File.OpenRead(path);
		return Convert.ToHexString(SHA256.HashData(file));
	}
	private static bool IdentityPathAndHash(JsonNode identity, string prefix, string path, string hash) =>
		string.Equals(Path.GetFullPath(identity[prefix + "Path"]?.GetValue<string>() ?? string.Empty), path, StringComparison.OrdinalIgnoreCase) &&
		string.Equals(identity[prefix + "Sha256"]?.GetValue<string>(), hash, StringComparison.OrdinalIgnoreCase);

	internal sealed record Manifest(string Nonce, int ProcessId, string AssemblyPath, string AssemblyHash, string Namespace,
		string ClassName, string ObjectPointer, string StopMarker, int Magic, long Counter, double Speed, int StaticMagic,
		int ExpectedBumpArgument, long ExpectedCounterAfterInvoke)
	{
		internal static Manifest Parse(JsonNode node, string nonce, int processId, string root, string assembly, string assemblyHash)
		{
			string Text(string name) => node[name]?.GetValue<string>() ?? throw new InvalidDataException($"Mono manifest misses {name}.");
			int Number(string name) => node[name]?.GetValue<int>() ?? throw new InvalidDataException($"Mono manifest misses {name}.");
			long Int64(string name) => node[name]?.GetValue<long>() ?? throw new InvalidDataException($"Mono manifest misses {name}.");
			JsonNode fields = node["fields"] ?? throw new InvalidDataException("Mono manifest misses fields.");
			string pointer = Text("objectPointer");
			uint token = node["gcHandleToken"]?.GetValue<uint>() ?? 0;
			string expectedStop = Path.Combine(root, "mono-owned-" + nonce + ".stop");
			if (Text("nonce") != nonce || Number("pid") != processId || Text("architecture") != "x64" || Text("monoRuntimeMarker") != "Mono.Runtime" || Text("namespace") != "CheatEngine.Mcp.MonoFixture" || Text("class") != "MonoPrimitiveProbe" ||
				!string.Equals(Path.GetFullPath(Text("ownedRoot")), Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase) ||
				!string.Equals(Path.GetFullPath(Text("stopMarker")), expectedStop, StringComparison.OrdinalIgnoreCase) ||
				!string.Equals(Path.GetFullPath(Text("assemblyFullPath")), Path.GetFullPath(assembly), StringComparison.OrdinalIgnoreCase) ||
				!string.Equals(Text("assemblySha256"), assemblyHash, StringComparison.OrdinalIgnoreCase) ||
				!ulong.TryParse(pointer, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong address) || address == 0 ||
				Number("maximumLifetimeMilliseconds") != 120000 || token == 0 ||
				fields[nameof(Magic)]?.GetValue<int>() != 314159 || fields[nameof(Counter)]?.GetValue<long>() != 7000L || fields[nameof(Speed)]?.GetValue<double>() != 1.25d || Number("staticMagic") != 424242 || Number("staticAdd42WarmupResult") != 42 || Number("expectedInstanceBumpArgument") != 7 || Int64("expectedCounterAfterInvoke") != 7007L)
			{
				throw new InvalidDataException("The owned Mono manifest identity is invalid.");
			}
			return new(nonce, processId, Path.GetFullPath(assembly), assemblyHash, Text("namespace"), Text("class"), pointer,
				Text("stopMarker"), fields[nameof(Magic)]!.GetValue<int>(), fields[nameof(Counter)]!.GetValue<long>(), fields[nameof(Speed)]!.GetValue<double>(),
				Number("staticMagic"), Number("expectedInstanceBumpArgument"), Int64("expectedCounterAfterInvoke"));
		}
	}
}
