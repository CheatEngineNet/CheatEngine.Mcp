using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Stages and drives the fixed, CE-local network-isolation probe used only by the admitted symbol-source scenario.</summary>
[SupportedOSPlatform("windows")]
internal static class LiveSymbolSourceNetworkProbe
{
	internal const string ModuleFileName = "cemcpnetworkprobe.dll";
	internal const string ExportName = "luaopen_cemcpnetworkprobe";
	internal const int ExpectedBlockedError = 10013;
	internal const int MaximumResponseBytes = 256;
	private static readonly TimeSpan BuildTimeout = TimeSpan.FromSeconds(75);
	private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(15);

	internal static readonly string DriverTemplate = """
		local sourceProbeModule=assert(package.loadlib(__DLL__,__EXPORT__)(), 'owned source probe module did not register')
		local function sourceProbeRequest()
		  local request=io.open(__REQUEST__,'r')
		  if not request then return end
		  local command=request:read('*a'); request:close(); assert(os.remove(__REQUEST__),'source probe request dequeue failed')
		  local nonce=command:match('^(%x+)\n$')
		  assert(nonce and #nonce==32 and nonce==nonce:lower(), 'invalid fixed source probe request')
		  local success,ipv4,ipv6,pid=pcall(function()
		    return sourceProbeModule.ipv4(),sourceProbeModule.ipv6(),sourceProbeModule.pid()
		  end)
		  local response=assert(io.open(__RESPONSE__..'.tmp','w'))
		  if success then response:write('ok\n'..nonce..'\n'..tostring(pid)..'\n'..tostring(ipv4)..'\n'..tostring(ipv6)..'\n')
		  else response:write('error\n'..nonce..'\n'..tostring(ipv4):gsub('[\r\n]+',' '):sub(1,128)..'\n') end
		  response:close(); assert(os.rename(__RESPONSE__..'.tmp',__RESPONSE__),'source probe response publish failed')
		end
		""".ReplaceLineEndings("\n").TrimEnd('\n');

	internal static string RenderDriverLua(string dllPath, string requestPath, string responsePath, Func<string, string> quote)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(dllPath);
		ArgumentNullException.ThrowIfNull(quote);
		return DriverTemplate.Replace("__DLL__", quote(dllPath), StringComparison.Ordinal)
			.Replace("__EXPORT__", quote(ExportName), StringComparison.Ordinal)
			.Replace("__REQUEST__", quote(requestPath), StringComparison.Ordinal)
			.Replace("__RESPONSE__", quote(responsePath), StringComparison.Ordinal);
	}

	internal static string LoadedTimerBody(bool symbolSourcesQualification) => symbolSourcesQualification
		? "pluginControl(); scanProbe(); compilerProbe(); sourceProbeRequest(); return"
		: "pluginControl(); scanProbe(); compilerProbe(); return";

	/// <summary>Builds the reviewed fixture into the owned run root, validates its manifest, and returns its private-copy lease.</summary>
	internal static async Task<LiveSymbolSourceNetworkProbeStage> StageAsync(string repositoryRoot, string ownedRunRoot,
		CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
		ArgumentException.ThrowIfNullOrWhiteSpace(ownedRunRoot);
		string repository = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryRoot));
		string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(ownedRunRoot));
		if (!Directory.Exists(root) || !Path.IsPathFullyQualified(root))
		{
			throw new InvalidDataException("The source-probe run root must be an existing absolute directory.");
		}
		LiveCompilerCandidate.RequireNoReparseAncestors(root);
		string script = Path.Combine(repository, "eng", "Build-LiveNetworkProbe.ps1");
		string source = Path.Combine(repository, "eng", "LiveNetworkProbe", "CemcpNetworkProbe.cpp");
		if (!File.Exists(script) || !File.Exists(source))
		{
			throw new FileNotFoundException("The reviewed source-probe build inputs are missing.");
		}
		LiveCompilerCandidate.RequireNoReparseAncestors(script);
		LiveCompilerCandidate.RequireNoReparseAncestors(source);
		string output = Path.Combine(root, "symbol-source-network-probe");
		LiveSymbolSourceFixture.RequireInsideRun(output, root);
		Directory.CreateDirectory(output);
		LiveCompilerCandidate.RequireNoReparseAncestors(output);
		ProcessStartInfo start = new("pwsh")
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardError = true
		};
		start.ArgumentList.Add("-NoProfile");
		start.ArgumentList.Add("-File");
		start.ArgumentList.Add(script);
		start.ArgumentList.Add("-OutputDirectory");
		start.ArgumentList.Add(output);
		using Process process = Process.Start(start) ?? throw new InvalidOperationException("The owned source-probe build did not start.");
		Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
		try
		{
			await process.WaitForExitAsync(cancellationToken).WaitAsync(BuildTimeout, cancellationToken);
		}
		catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
		{
			if (!process.HasExited)
			{
				process.Kill(true);
				await process.WaitForExitAsync(CancellationToken.None);
			}
			throw;
		}
		string stderr = await error;
		if (process.ExitCode != 0)
		{
			throw new InvalidOperationException("The owned source-probe build failed: " + TrimDiagnostic(stderr));
		}
		string[] manifests = Directory.EnumerateFiles(output, "CemcpNetworkProbe-*.manifest.json", SearchOption.TopDirectoryOnly).ToArray();
		if (manifests.Length != 1)
		{
			throw new InvalidDataException("The owned source-probe build did not produce exactly one manifest.");
		}
		return ValidateManifest(manifests[0], source, root);
	}

	internal static LiveSymbolSourceNetworkProbeStage ValidateManifest(string manifestPath, string expectedSourcePath, string ownedRunRoot)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
		ArgumentException.ThrowIfNullOrWhiteSpace(expectedSourcePath);
		string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(ownedRunRoot));
		string manifest = Path.GetFullPath(manifestPath);
		string expectedSource = Path.GetFullPath(expectedSourcePath);
		LiveSymbolSourceFixture.RequireInsideRun(manifest, root);
		LiveCompilerCandidate.RequireNoReparseAncestors(manifest);
		if (!File.Exists(expectedSource))
		{
			throw new FileNotFoundException("The reviewed source-probe source is missing.", expectedSource);
		}
		using FileStream stream = new(manifest, FileMode.Open, FileAccess.Read, FileShare.Read);
		if (stream.Length is <= 0 or > 16_384)
		{
			throw new InvalidDataException("The source-probe build manifest exceeds its byte bound.");
		}
		using JsonDocument document = JsonDocument.Parse(stream);
		JsonElement rootElement = document.RootElement;
		if (rootElement.ValueKind != JsonValueKind.Object || rootElement.GetProperty("fixture").GetString() != "CemcpNetworkProbe" ||
			rootElement.GetProperty("architecture").GetString() != "x64" || rootElement.GetProperty("destinationPort").GetInt32() != 9 ||
			rootElement.GetProperty("expectedBlockedError").GetInt32() != ExpectedBlockedError ||
			rootElement.GetProperty("maximumPendingConnectSeconds").GetInt32() != 1)
		{
			throw new InvalidDataException("The source-probe build manifest has unexpected fixed facts.");
		}
		string source = ReadPath(rootElement.GetProperty("source"), "path");
		string dll = ReadPath(rootElement.GetProperty("dll"), "path");
		if (!string.Equals(Path.GetFullPath(source), expectedSource, StringComparison.OrdinalIgnoreCase) ||
			!string.Equals(Hash(source), ReadSha256(rootElement.GetProperty("source")), StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("The source-probe source path or hash differs from the reviewed build manifest.");
		}
		LiveSymbolSourceFixture.RequireInsideRun(dll, root);
		LiveCompilerCandidate.RequireNoReparseAncestors(dll);
		long length = rootElement.GetProperty("dll").GetProperty("length").GetInt64();
		if (length <= 0 || !File.Exists(dll) || new FileInfo(dll).Length != length ||
			!string.Equals(Hash(dll), ReadSha256(rootElement.GetProperty("dll")), StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("The source-probe DLL bytes differ from its build manifest.");
		}
		string[] exports = rootElement.GetProperty("exports").EnumerateArray().Select(value => value.GetString() ?? string.Empty).ToArray();
		string[] functions = rootElement.GetProperty("functions").EnumerateArray().Select(value => value.GetString() ?? string.Empty).ToArray();
		if (!exports.SequenceEqual([ExportName], StringComparer.Ordinal) || !functions.SequenceEqual(["ipv4", "ipv6", "pid"], StringComparer.Ordinal))
		{
			throw new InvalidDataException("The source-probe build manifest does not declare the fixed Lua module contract.");
		}
		return new LiveSymbolSourceNetworkProbeStage(root, dll, Hash(dll), Hash(source), manifest);
	}

	internal static async Task<LiveSymbolSourceNetworkProbeReceipt> RequestAsync(string ownedRunRoot, LiveSandboxHost host,
		string stage, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(host);
		if (host.ProcessId <= 0 || !IsStage(stage))
		{
			throw new InvalidDataException("The source-probe host identity or evidence stage is invalid.");
		}
		string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(ownedRunRoot));
		string request = Path.Combine(root, $"symbol-source-probe-request-{host.Name}.txt");
		string response = Path.Combine(root, $"symbol-source-probe-response-{host.Name}.txt");
		LiveSymbolSourceFixture.RequireInsideRun(request, root);
		LiveSymbolSourceFixture.RequireInsideRun(response, root);
		LiveCompilerCandidate.RequireNoReparseAncestors(request);
		LiveCompilerCandidate.RequireNoReparseAncestors(response);
		if (File.Exists(response))
		{
			File.Delete(response);
		}
		string nonce = Guid.NewGuid().ToString("N");
		string temporary = request + ".tmp";
		await File.WriteAllTextAsync(temporary, nonce + "\n", new UTF8Encoding(false), cancellationToken);
		File.Move(temporary, request, false);
		Process process = Process.GetProcessById(host.ProcessId);
		using (process)
		{
			await WaitForResponseAsync(response, process, cancellationToken);
		}
		LiveSymbolSourceNetworkProbeReceipt receipt = await ReadReceiptAsync(response, nonce, host.ProcessId, stage, cancellationToken);
		return receipt;
	}

	internal static async Task<LiveSymbolSourceNetworkProbeReceipt> ReadReceiptAsync(string path, string nonce, int expectedProcessId,
		string stage, CancellationToken cancellationToken)
	{
		if (!IsNonce(nonce) || expectedProcessId <= 0 || !IsStage(stage))
		{
			throw new InvalidDataException("The source-probe receipt expectation is invalid.");
		}
		LiveCompilerCandidate.RequireNoReparseAncestors(path);
		await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
		if (stream.Length is <= 0 or > MaximumResponseBytes)
		{
			throw new InvalidDataException("The source-probe response exceeds its byte bound.");
		}
		using StreamReader reader = new(stream, new UTF8Encoding(false, true), false);
		string[] lines = (await reader.ReadToEndAsync(cancellationToken)).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
		if (lines.Length != 6 || lines[0] != "ok" || lines[1] != nonce || !int.TryParse(lines[2], NumberStyles.None,
			CultureInfo.InvariantCulture, out int processId) || processId != expectedProcessId ||
			!int.TryParse(lines[3], NumberStyles.None, CultureInfo.InvariantCulture, out int ipv4) ||
			!int.TryParse(lines[4], NumberStyles.None, CultureInfo.InvariantCulture, out int ipv6) || lines[5].Length != 0 ||
			ipv4 != ExpectedBlockedError || ipv6 != ExpectedBlockedError)
		{
			throw new InvalidDataException("The fixed source-probe response did not prove the expected CE-local blocked connections.");
		}
		return new LiveSymbolSourceNetworkProbeReceipt(stage, nonce, processId, ipv4, ipv6, DateTimeOffset.UtcNow);
	}

	private static async Task WaitForResponseAsync(string response, Process process, CancellationToken cancellationToken)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		while (!File.Exists(response))
		{
			if (process.HasExited)
			{
				throw new InvalidOperationException("The owned CE process exited before the source-probe response.");
			}
			if (stopwatch.Elapsed >= ResponseTimeout)
			{
				throw new TimeoutException("The fixed source-probe response did not arrive before its deadline.");
			}
			await Task.Delay(50, cancellationToken);
		}
	}

	private static string ReadPath(JsonElement parent, string name)
	{
		string? value = parent.GetProperty(name).GetString();
		if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value))
		{
			throw new InvalidDataException("The source-probe manifest has an invalid absolute path.");
		}
		return value;
	}

	private static string ReadSha256(JsonElement parent)
	{
		string? value = parent.GetProperty("sha256").GetString();
		if (value is null || value.Length != 64 || !value.All(Uri.IsHexDigit))
		{
			throw new InvalidDataException("The source-probe manifest has an invalid SHA-256 digest.");
		}
		return value;
	}

	private static string Hash(string path)
	{
		using FileStream input = File.OpenRead(path);
		return Convert.ToHexString(SHA256.HashData(input));
	}

	private static bool IsNonce(string value) => value.Length == 32 && value.All(character => character is (>= '0' and <= '9') or (>= 'a' and <= 'f'));
	private static bool IsStage(string value) => value.Length is > 0 and <= 64 && value.All(character => char.IsAsciiLetterOrDigit(character) || character == '-');
	private static string TrimDiagnostic(string value) => value.ReplaceLineEndings(" ").Trim()[..Math.Min(value.Trim().Length, 512)];
}

/// <summary>One verified fixed-DLL lease; it copies only to the named private CE installation before it starts.</summary>
[SupportedOSPlatform("windows")]
internal sealed class LiveSymbolSourceNetworkProbeStage
{
	internal LiveSymbolSourceNetworkProbeStage(string ownedRunRoot, string dllPath, string dllSha256, string sourceSha256, string manifestPath)
	{
		OwnedRunRoot = ownedRunRoot;
		DllPath = dllPath;
		DllSha256 = dllSha256;
		SourceSha256 = sourceSha256;
		ManifestPath = manifestPath;
	}

	internal string OwnedRunRoot
	{
		get;
	}
	internal string DllPath
	{
		get;
	}
	internal string DllSha256
	{
		get;
	}
	internal string SourceSha256
	{
		get;
	}
	internal string ManifestPath
	{
		get;
	}

	internal string CopyToPrivateHost(string privateHostDirectory)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(privateHostDirectory);
		string hostDirectory = Path.GetFullPath(privateHostDirectory);
		LiveSymbolSourceFixture.RequireInsideRun(hostDirectory, OwnedRunRoot);
		LiveCompilerCandidate.RequireNoReparseAncestors(hostDirectory);
		string destination = Path.Combine(hostDirectory, LiveSymbolSourceNetworkProbe.ModuleFileName);
		LiveSymbolSourceFixture.RequireInsideRun(destination, OwnedRunRoot);
		if (File.Exists(destination))
		{
			throw new InvalidDataException("The private CE source-probe DLL already exists.");
		}
		File.Copy(DllPath, destination, false);
		if (!string.Equals(Hash(destination), DllSha256, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("The private CE source-probe DLL copy did not preserve its verified bytes.");
		}
		return destination;
	}

	private static string Hash(string path)
	{
		using FileStream input = File.OpenRead(path);
		return Convert.ToHexString(SHA256.HashData(input));
	}
}

internal sealed record LiveSymbolSourceNetworkProbeReceipt(string Stage, string Nonce, int ProcessId, int Ipv4Error,
	int Ipv6Error, DateTimeOffset ObservedAt);

internal sealed partial class LiveSandboxSession
{
	/// <summary>Requests one already-loaded fixed probe from an owned CE process after admitted WFP activation.</summary>
	[SupportedOSPlatform("windows")]
	internal async Task<LiveSymbolSourceNetworkProbeReceipt> VerifySymbolSourceNetworkAsync(LiveSandboxHost host, string stage,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(host);
		if (!_options.SymbolSourcesQualification || SymbolSourceContainment.ActivationReceipt is null ||
			!_hosts.TryGetValue(host.Name, out OwnedHost? owned) || owned.Stopped || owned.Process is null ||
			owned.Public.ProcessId != host.ProcessId)
		{
			throw new InvalidOperationException("The source-probe bridge is available only to a running admitted symbol-source host after WFP activation.");
		}
		LiveSymbolSourceNetworkProbeReceipt receipt = await LiveSymbolSourceNetworkProbe.RequestAsync(_layout.RunDirectory, host,
			stage, cancellationToken);
		Record($"symbol_source_network_probe_{stage}_{host.Name}", receipt);
		return receipt;
	}
}
