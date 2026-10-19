using System.Diagnostics;
using System.Globalization;
using System.Reflection.PortableExecutable;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Reversible evidence for creating one owned .NET Framework child through Cheat Engine.</summary>
[SupportedOSPlatform("windows")]
internal static class LiveProcessCreateQualification
{
	private static readonly TimeSpan ManifestTimeout = TimeSpan.FromSeconds(10);
	private const int MaximumManifestBytes = 4096;
	private const int MaximumFixtureBytes = 1024 * 1024;

	internal static async Task VerifyAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient instance,
		string repositoryRoot)
	{
		ArgumentNullException.ThrowIfNull(sandbox);
		ArgumentNullException.ThrowIfNull(instance);
		ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
		LiveFrameworkFixture fixture = RequireFixture(repositoryRoot, sandbox.TargetArchitecture);
		string directory = Path.Combine(sandbox.RunDirectory, "roadmap-qualification", "process-create",
			Guid.NewGuid().ToString("N"));
		string executable = OwnedPath(directory, "CheatEngine.Mcp.LiveFrameworkTarget.exe");
		string manifest = OwnedPath(directory, "child.json");
		string stop = manifest + ".stop";
		int originalProcessId = sandbox.HostA.TargetProcessId;
		bool creationAttempted = false;
		bool targetRestored = false;
		bool childExited = false;
		OwnedChild? child = null;
		Exception? failure = null;
		List<Exception> cleanup = [];
		Directory.CreateDirectory(directory);
		try
		{
			await VerifyGatesAsync(instance);
			await AssertCurrentAsync(instance, originalProcessId);
			CopyFixture(fixture, executable);

			creationAttempted = true;
			JsonNode created = await CallAsync(instance, CheatEngineToolNames.ProcessCreate,
				new Dictionary<string, object?>
				{
					["path"] = executable,
					["parameters"] = QuoteWindowsArgument(manifest),
					["debug"] = false,
					["breakOnEntryPoint"] = false
				});
			int childProcessId = created["processId"]!.GetValue<int>();
			Assert.True(childProcessId > 0);
			Assert.False(created["monoAutoAttach"]!.GetValue<bool>());
			child = await ReadOwnedChildAsync(manifest, executable, fixture, childProcessId,
				TestContext.Current.CancellationToken);
			await AssertCurrentAsync(instance, childProcessId);
			Receipt(sandbox, CheatEngineToolNames.ProcessCreate, new JsonObject
			{
				["result"] = created.DeepClone(),
				["manifest"] = child.Manifest!.DeepClone(),
				["executablePath"] = executable,
				["executableSha256"] = fixture.Sha256
			});
		}
		catch (Exception exception) { failure = exception; throw; }
		finally
		{
			if (creationAttempted && child is null)
			{
				using CancellationTokenSource discoveryTimeout = new(ManifestTimeout);
				LiveMcpInstanceClient discoveryInstance = instance.WithCancellation(discoveryTimeout.Token);
				await CleanupAsync(cleanup, async () =>
				{
					child = await RecoverOwnedChildAsync(discoveryInstance, manifest, executable, fixture,
						discoveryTimeout.Token);
				});
			}
			if (child is not null)
			{
				await CleanupAsync(cleanup, async () =>
				{
					await StopOwnedChildAsync(child, stop);
					childExited = true;
				});
			}
			if (creationAttempted)
			{
				using CancellationTokenSource restoreTimeout = new(ManifestTimeout);
				LiveMcpInstanceClient cleanupInstance = instance.WithCancellation(restoreTimeout.Token);
				await CleanupAsync(cleanup, async () =>
				{
					JsonNode attached = await CallAsync(cleanupInstance, CheatEngineToolNames.ProcessAttach,
						new Dictionary<string, object?>
						{
							["process"] = originalProcessId.ToString(CultureInfo.InvariantCulture)
						});
					Assert.Equal(originalProcessId, attached["processId"]!.GetValue<int>());
					await AssertCurrentAsync(cleanupInstance, originalProcessId);
					targetRestored = true;
				});
			}
			if (!creationAttempted || childExited)
			{
				await CleanupAsync(cleanup, () => DeleteOwnedDirectoryAsync(directory, sandbox.RunDirectory));
			}
			else
			{
				cleanup.Add(new InvalidOperationException(
					"The process-create directory was retained because the attempted child was not proven stopped."));
			}
			if (cleanup.Count == 0 && (!creationAttempted || targetRestored) && !Directory.Exists(directory))
			{
				sandbox.Record("live_process_create_cleanup", new
				{
					creationAttempted,
					childVerified = child is not null,
					targetRestored,
					directoryDeleted = true
				});
			}
			LiveLifecycleQualification.ThrowWithCleanup(failure, cleanup);
		}
	}

	private static LiveFrameworkFixture RequireFixture(string repositoryRoot, string architecture)
	{
		string repository = Path.GetFullPath(repositoryRoot);
		string root = Path.Combine(repository, "artifacts", "live-framework-target", architecture);
		string executable = Path.Combine(root, "CheatEngine.Mcp.LiveFrameworkTarget.exe");
		string identity = Path.Combine(root, "fixture-identity.json");
		string source = Path.Combine(repository, "eng", "LiveFrameworkTarget", "Program.cs");
		if (!File.Exists(executable) || !File.Exists(identity) || !File.Exists(source))
		{
			throw new FileNotFoundException("The reviewed Framework process-create fixture is unavailable.", executable);
		}

		LiveCompilerCandidate.RequireNoReparseAncestors(executable);
		LiveCompilerCandidate.RequireNoReparseAncestors(identity);
		LiveCompilerCandidate.RequireNoReparseAncestors(source);
		if (new FileInfo(identity).Length is <= 0 or > MaximumManifestBytes ||
			new FileInfo(executable).Length is <= 0 or > MaximumFixtureBytes)
		{
			throw new InvalidDataException("The reviewed Framework fixture or identity exceeds its size bound.");
		}

		JsonNode document = JsonNode.Parse(File.ReadAllText(identity))
			?? throw new InvalidDataException("The reviewed Framework fixture identity is empty.");
		string identityArchitecture = Text(document, "architecture");
		string expectedHash = Text(document, "targetSha256");
		string expectedSourceHash = Text(document, "sourceSha256");
		string sourceHash = HashFile(source);
		string executableHash = HashFile(executable);
		if (!string.Equals(identityArchitecture, architecture, StringComparison.Ordinal) ||
			!string.Equals(expectedHash, executableHash, StringComparison.OrdinalIgnoreCase) ||
			!string.Equals(expectedSourceHash, sourceHash, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("The reviewed Framework fixture identity does not match its selected bytes.");
		}

		using FileStream image = File.OpenRead(executable);
		using PEReader pe = new(image);
		if (pe.PEHeaders.CorHeader is not { } cor ||
			(architecture == "x86" && (pe.PEHeaders.CoffHeader.Machine != Machine.I386 || (cor.Flags & CorFlags.Requires32Bit) == 0)) ||
			(architecture == "x64" && pe.PEHeaders.CoffHeader.Machine != Machine.Amd64))
		{
			throw new InvalidDataException("The Framework fixture PE architecture does not match its identity.");
		}

		return new LiveFrameworkFixture(executable, executableHash, architecture);
	}

	private static void CopyFixture(LiveFrameworkFixture fixture, string destination)
	{
		LiveCompilerCandidate.RequireNoReparseAncestors(destination);
		File.Copy(fixture.ExecutablePath, destination, false);
		Assert.Equal(fixture.Sha256, HashFile(destination));
	}

	private static async Task<OwnedChild> ReadOwnedChildAsync(string manifest, string executable,
		LiveFrameworkFixture fixture, int? expectedProcessId, CancellationToken cancellationToken)
	{
		Stopwatch elapsed = Stopwatch.StartNew();
		while (!File.Exists(manifest))
		{
			if (elapsed.Elapsed >= ManifestTimeout)
			{
				throw new TimeoutException("The Framework child did not publish its private manifest.");
			}

			await Task.Delay(50, cancellationToken);
		}

		LiveCompilerCandidate.RequireNoReparseAncestors(manifest);
		if (new FileInfo(manifest).Length is <= 0 or > MaximumManifestBytes)
		{
			throw new InvalidDataException("The Framework child manifest exceeds its size bound.");
		}

		JsonNode document = JsonNode.Parse(await File.ReadAllTextAsync(manifest, cancellationToken))
			?? throw new InvalidDataException("The Framework child manifest is empty.");
		int processId = document["processId"]!.GetValue<int>();
		Assert.True(processId > 0);
		if (expectedProcessId is { } expected)
		{
			Assert.Equal(expected, processId);
		}

		Assert.Equal(fixture.Architecture == "x86" ? 32 : 64, document["pointerWidth"]!.GetValue<int>());
		Assert.Equal("CheatEngine.Mcp.LiveFrameworkTarget", Text(document, "assembly"));
		Assert.Equal(fixture.Sha256, Text(document, "sha256"), ignoreCase: true);
		Assert.True(Version.TryParse(Text(document, "runtime"), out Version? runtime) && runtime.Major == 4);
		Process process = OpenOwnedProcess(processId, executable, fixture);
		return new OwnedChild(process, document.DeepClone());
	}

	private static async Task<OwnedChild> RecoverOwnedChildAsync(LiveMcpInstanceClient instance, string manifest,
		string executable, LiveFrameworkFixture fixture, CancellationToken cancellationToken)
	{
		JsonNode current = await CallAsync(instance, CheatEngineToolNames.ProcessGetCurrent);
		if (current["isOpen"]!.GetValue<bool>())
		{
			int selectedProcessId = current["processId"]!.GetValue<int>();
			try
			{
				return new OwnedChild(OpenOwnedProcess(selectedProcessId, executable, fixture), null);
			}
			catch (Exception)
			{
				// The selected process is not the copied fixture. The manifest is the remaining ownership proof.
			}
		}

		return await ReadOwnedChildAsync(manifest, executable, fixture, expectedProcessId: null, cancellationToken);
	}

	private static Process OpenOwnedProcess(int processId, string executable, LiveFrameworkFixture fixture)
	{
		Process process = Process.GetProcessById(processId);
		process.Refresh();
		if (process.HasExited)
		{
			process.Dispose();
			throw new InvalidOperationException("The owned Framework child exited before cleanup.");
		}

		string imagePath;
		try
		{
			using ProcessModule main = process.MainModule
				?? throw new InvalidOperationException("The owned Framework child has no executable module.");
			imagePath = main.FileName;
		}
		catch
		{
			process.Dispose();
			throw;
		}
		if (!string.Equals(Path.GetFullPath(executable), Path.GetFullPath(imagePath), StringComparison.OrdinalIgnoreCase) ||
			!string.Equals(fixture.Sha256, HashFile(imagePath), StringComparison.OrdinalIgnoreCase))
		{
			process.Dispose();
			throw new InvalidOperationException("The manifest process does not match the copied Framework fixture.");
		}

		return process;
	}

	private static async Task StopOwnedChildAsync(OwnedChild child, string stop)
	{
		child.Process.Refresh();
		if (child.Process.HasExited)
		{
			throw new InvalidOperationException("The owned Framework child exited before its graceful stop.");
		}

		LiveCompilerCandidate.RequireNoReparseAncestors(stop);
		File.WriteAllText(stop, "stop");
		await child.Process.WaitForExitAsync().WaitAsync(ManifestTimeout);
		if (child.Process.ExitCode != 0)
		{
			throw new InvalidOperationException("The owned Framework child returned a nonzero exit code.");
		}

		child.Process.Dispose();
	}

	private static async Task VerifyGatesAsync(LiveMcpInstanceClient instance)
	{
		JsonNode runtime = await CallAsync(instance, CheatEngineToolNames.RuntimeGetInfo);
		JsonNode gates = runtime["gates"] ?? throw new InvalidDataException("runtime_get_info omitted gates.");
		Assert.False(gates["unsafeLua"]!.GetValue<bool>());
		Assert.False(gates["autoAssembler"]!.GetValue<bool>());
		Assert.False(gates["targetCodeExecution"]!.GetValue<bool>());
		Assert.False(gates["kernelAccess"]!.GetValue<bool>());
	}

	private static async Task AssertCurrentAsync(LiveMcpInstanceClient instance, int expectedProcessId)
	{
		JsonNode current = await CallAsync(instance, CheatEngineToolNames.ProcessGetCurrent);
		Assert.True(current["isOpen"]!.GetValue<bool>());
		Assert.Equal(expectedProcessId, current["processId"]!.GetValue<int>());
	}

	private static string OwnedPath(string root, string leaf)
	{
		string path = Path.GetFullPath(Path.Combine(root, leaf));
		if (!LiveQualificationOptIn.IsSameOrBelow(path, root))
		{
			throw new InvalidOperationException("The Framework process-create path escaped its owned root.");
		}

		return path;
	}

	private static Task DeleteOwnedDirectoryAsync(string directory, string runDirectory)
	{
		string fullDirectory = Path.GetFullPath(directory);
		if (!LiveQualificationOptIn.IsSameOrBelow(fullDirectory, runDirectory))
		{
			throw new InvalidOperationException("The process-create cleanup directory escaped the live run.");
		}

		LiveCompilerCandidate.RequireNoReparseAncestors(fullDirectory);
		if (Directory.Exists(directory))
		{
			Directory.Delete(directory, true);
		}

		Assert.False(Directory.Exists(directory));
		return Task.CompletedTask;
	}

	private static async Task CleanupAsync(List<Exception> cleanup, Func<Task> action)
	{
		try
		{
			await action();
		}
		catch (Exception exception)
		{
			cleanup.Add(exception);
		}
	}

	private static string HashFile(string path)
	{
		using FileStream stream = File.OpenRead(path);
		return Convert.ToHexString(SHA256.HashData(stream));
	}

	private static string QuoteWindowsArgument(string value)
	{
		ArgumentException.ThrowIfNullOrEmpty(value);
		StringBuilder quoted = new();
		quoted.Append('"');
		int backslashes = 0;
		foreach (char character in value)
		{
			if (character == '\\')
			{
				backslashes++;
				continue;
			}

			if (character == '"')
			{
				quoted.Append('\\', (backslashes * 2) + 1);
				quoted.Append(character);
				backslashes = 0;
				continue;
			}

			quoted.Append('\\', backslashes);
			quoted.Append(character);
			backslashes = 0;
		}

		quoted.Append('\\', backslashes * 2);
		quoted.Append('"');
		return quoted.ToString();
	}

	private static string Text(JsonNode node, string property) => node[property]!.GetValue<string>();
	private static void Receipt(LiveSandboxSession sandbox, string tool, JsonNode result) =>
		sandbox.Record("all_tools_" + tool, result.DeepClone());
	private static async Task<JsonNode> CallAsync(LiveMcpInstanceClient instance, string tool,
		IReadOnlyDictionary<string, object?>? arguments = null) => await instance.CallToolAsync(tool, arguments)
		?? throw new InvalidDataException($"Tool '{tool}' returned no payload.");

	private sealed record OwnedChild(Process Process, JsonNode? Manifest);
}
