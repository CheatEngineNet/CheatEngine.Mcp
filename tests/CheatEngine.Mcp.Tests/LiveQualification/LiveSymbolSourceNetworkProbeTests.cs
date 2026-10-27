using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;

namespace CheatEngine.Mcp.Tests.LiveQualification;

[SupportedOSPlatform("windows")]
public sealed class LiveSymbolSourceNetworkProbeTests
{
	[Fact]
	public void RenderDriverLua_FixedModuleContract_RendersOnlyOwnedValues()
	{
		string lua = LiveSymbolSourceNetworkProbe.RenderDriverLua(@"C:\owned\ce-A\cemcpnetworkprobe.dll",
			@"C:\owned\request.txt", @"C:\owned\response.txt", value => "[" + value + "]");

		Assert.Contains("package.loadlib([C:\\owned\\ce-A\\cemcpnetworkprobe.dll],[luaopen_cemcpnetworkprobe])", lua,
			StringComparison.Ordinal);
		Assert.Contains("sourceProbeModule.ipv4(),sourceProbeModule.ipv6(),sourceProbeModule.pid()", lua, StringComparison.Ordinal);
		Assert.Contains("os.rename", lua, StringComparison.Ordinal);
		Assert.DoesNotContain("__", lua, StringComparison.Ordinal);
	}

	[Fact]
	public void LoadedTimerBody_SymbolSourcesSelection_AddsNoProbeToTheDefaultDriver()
	{
		Assert.Equal("pluginControl(); scanProbe(); compilerProbe(); return", LiveSymbolSourceNetworkProbe.LoadedTimerBody(false));
		Assert.Equal("pluginControl(); scanProbe(); compilerProbe(); sourceProbeRequest(); return",
			LiveSymbolSourceNetworkProbe.LoadedTimerBody(true));
	}

	[Fact]
	public async Task ReadReceiptAsync_ExactNoncePidAndBlockedErrors_Accepts()
	{
		using Scratch scratch = new();
		string path = Path.Combine(scratch.Root, "response.txt");
		const string nonce = "0123456789abcdef0123456789abcdef";
		await File.WriteAllTextAsync(path, "ok\n" + nonce + "\n42\n10013\n10013\n", TestContext.Current.CancellationToken);

		LiveSymbolSourceNetworkProbeReceipt receipt = await LiveSymbolSourceNetworkProbe.ReadReceiptAsync(path, nonce, 42, "before-enable",
			TestContext.Current.CancellationToken);

		Assert.Equal("before-enable", receipt.Stage);
		Assert.Equal(nonce, receipt.Nonce);
		Assert.Equal(42, receipt.ProcessId);
		Assert.Equal(LiveSymbolSourceNetworkProbe.ExpectedBlockedError, receipt.Ipv4Error);
		Assert.Equal(LiveSymbolSourceNetworkProbe.ExpectedBlockedError, receipt.Ipv6Error);
	}

	[Theory]
	[InlineData("ffffffffffffffffffffffffffffffff", 42, 10013, 10013)]
	[InlineData("0123456789abcdef0123456789abcdef", 43, 10013, 10013)]
	[InlineData("0123456789abcdef0123456789abcdef", 42, 0, 10013)]
	[InlineData("0123456789abcdef0123456789abcdef", 42, 10013, 10060)]
	public async Task ReadReceiptAsync_WrongNoncePidOrBlockedError_Refuses(string receivedNonce, int pid, int ipv4, int ipv6)
	{
		using Scratch scratch = new();
		string path = Path.Combine(scratch.Root, "response.txt");
		await File.WriteAllTextAsync(path, $"ok\n{receivedNonce}\n{pid}\n{ipv4}\n{ipv6}\n", TestContext.Current.CancellationToken);

		await Assert.ThrowsAsync<InvalidDataException>(() => LiveSymbolSourceNetworkProbe.ReadReceiptAsync(path,
			"0123456789abcdef0123456789abcdef", 42, "after-enable", TestContext.Current.CancellationToken));
	}

	[Fact]
	public void ValidateManifest_HashOrModuleContractChanged_Refuses()
	{
		using Scratch scratch = new();
		string source = scratch.Write("CemcpNetworkProbe.cpp", "reviewed source");
		string dll = scratch.Write("build/probe.dll", "reviewed dll");
		string manifest = scratch.WriteManifest(source, dll, sourceHash: new string('0', 64));

		InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
			LiveSymbolSourceNetworkProbe.ValidateManifest(manifest, source, scratch.Root));

		Assert.Contains("source path or hash", exception.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void ValidateManifest_ValidBytes_CopiesOnlyToPrivateHostAndPreservesDigest()
	{
		using Scratch scratch = new();
		string source = scratch.Write("CemcpNetworkProbe.cpp", "reviewed source");
		string dll = scratch.Write("build/probe.dll", "reviewed dll");
		string manifest = scratch.WriteManifest(source, dll);

		LiveSymbolSourceNetworkProbeStage stage = LiveSymbolSourceNetworkProbe.ValidateManifest(manifest, source, scratch.Root);
		string privateHost = Path.Combine(scratch.Root, "ce-A");
		Directory.CreateDirectory(privateHost);
		string copied = stage.CopyToPrivateHost(privateHost);

		Assert.Equal(Path.Combine(privateHost, LiveSymbolSourceNetworkProbe.ModuleFileName), copied, ignoreCase: true);
		Assert.Equal(Hash(dll), Hash(copied));
		Assert.Throws<InvalidDataException>(() => stage.CopyToPrivateHost(privateHost));
	}

	private static string Hash(string path)
	{
		using FileStream input = File.OpenRead(path);
		return Convert.ToHexString(SHA256.HashData(input));
	}

	private sealed class Scratch : IDisposable
	{
		internal string Root { get; } = Path.Combine(Path.GetTempPath(), "cemcp-source-probe-" + Guid.NewGuid().ToString("N"));

		internal Scratch() => Directory.CreateDirectory(Root);

		internal string Write(string relativePath, string contents)
		{
			string path = Path.Combine(Root, relativePath);
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllText(path, contents);
			return path;
		}

		internal string WriteManifest(string source, string dll, string? sourceHash = null)
		{
			string path = Path.Combine(Root, "build", "probe.manifest.json");
			object manifest = new
			{
				fixture = "CemcpNetworkProbe",
				architecture = "x64",
				source = new
				{
					path = source,
					sha256 = sourceHash ?? Hash(source)
				},
				dll = new
				{
					path = dll,
					sha256 = Hash(dll),
					length = new FileInfo(dll).Length
				},
				exports = new[] { LiveSymbolSourceNetworkProbe.ExportName },
				functions = new[] { "ipv4", "ipv6", "pid" },
				destinations = new[] { "192.0.2.1", "2001:db8::1" },
				destinationPort = 9,
				expectedBlockedError = LiveSymbolSourceNetworkProbe.ExpectedBlockedError,
				maximumPendingConnectSeconds = 1
			};
			File.WriteAllText(path, JsonSerializer.Serialize(manifest));
			return path;
		}

		public void Dispose()
		{
			if (Directory.Exists(Root))
			{
				Directory.Delete(Root, true);
			}
		}
	}
}
