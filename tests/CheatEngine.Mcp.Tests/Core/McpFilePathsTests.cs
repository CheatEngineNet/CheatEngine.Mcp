using System.Diagnostics;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Files;
using CheatEngine.Mcp.Tests.Support;

using Microsoft.Extensions.Options;

namespace CheatEngine.Mcp.Tests.Core;

public sealed class McpFilePathsTests : IDisposable
{
	private const string Tool = "test_tool";
	private readonly Scratch _scratch = new();

	public void Dispose()
	{
		_scratch.Dispose();
	}

	[Fact]
	public void RequireRead_AbsoluteLocalPath_ReturnsTheNormalizedFullPath()
	{
		McpFilePaths paths = Create();
		string folder = _scratch.CreateFolder("reads");
		string raw = folder.Replace('\\', '/') + "/sub/../dump.bin";

		string full = paths.RequireRead(raw, Tool);

		Assert.Equal(Path.Combine(folder, "dump.bin"), full);
	}

	[Theory]
	[InlineData("", "is required")]
	[InlineData("   ", "is required")]
	[InlineData("relative\\dump.bin", "absolute path")]
	[InlineData("C:dump.bin", "absolute path")]
	[InlineData("\\rooted\\dump.bin", "absolute path")]
	[InlineData("\\\\server\\share\\dump.bin", "UNC or device")]
	[InlineData("//server/share/dump.bin", "UNC or device")]
	[InlineData("\\/server\\share\\dump.bin", "UNC or device")]
	[InlineData("\\\\?\\C:\\Windows\\win.ini", "wildcard")]
	[InlineData("\\\\?\\UNC\\server\\share\\dump.bin", "wildcard")]
	[InlineData("\\\\.\\PhysicalDrive0", "UNC or device")]
	[InlineData("\\\\.\\C:\\Windows\\win.ini", "UNC or device")]
	[InlineData("C:\\folder\\dump.bin:stream", "alternate data stream")]
	[InlineData("C:\\folder\\dump.bin::$DATA", "alternate data stream")]
	[InlineData("C:\\folder\\CON", "Windows device")]
	[InlineData("C:\\folder\\nul.txt", "Windows device")]
	[InlineData("C:\\folder\\com1.log", "Windows device")]
	[InlineData("C:\\folder\\COM1 .log", "Windows device")]
	[InlineData("C:\\folder\\LPT9", "Windows device")]
	[InlineData("C:\\folder\\lpt\u00B9.bin", "Windows device")]
	[InlineData("C:\\folder\\CONOUT$", "Windows device")]
	[InlineData("C:\\AUX\\dump.bin", "Windows device")]
	[InlineData("C:\\folder\\dump*.bin", "wildcard")]
	[InlineData("C:\\folder\\dump?.bin", "wildcard")]
	[InlineData("C:\\folder\\a|b.bin", "wildcard")]
	[InlineData("C:\\folder\\a<b.bin", "wildcard")]
	[InlineData("C:\\folder\\a\"b.bin", "wildcard")]
	[InlineData("C:\\folder\\dump\u0001.bin", "wildcard")]
	[InlineData("C:\\folder\\dump.bin.", "space or a period")]
	[InlineData("C:\\folder \\dump.bin", "space or a period")]
	[InlineData("C:\\folder.\\dump.bin", "space or a period")]
	public void RequireRead_RefusedForm_IsInvalidArgumentWithoutEcho(string path, string reason)
	{
		McpFilePaths paths = Create();

		ToolError error = Refused(() => paths.RequireRead(path, Tool));

		Assert.Contains(reason, error.Message, StringComparison.Ordinal);
		if (path.Trim().Length > 0)
		{
			Assert.DoesNotContain(path, error.Message, StringComparison.Ordinal);
		}

		Assert.NotNull(error.Hint);
	}

	[Fact]
	public void RequireRead_OverlongPath_IsRefused()
	{
		McpFilePaths paths = Create();

		string path = "C:\\" + new string('a', McpPathRules.MaximumLength);

		ToolError error = Refused(() => paths.RequireRead(path, Tool));

		Assert.Contains("longer than", error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void RequireRead_DriveWithoutFixedDisk_IsRefused()
	{
		HashSet<char> used = [.. DriveInfo.GetDrives().Select(static drive => char.ToUpperInvariant(drive.Name[0]))];
		char free = "ZYXWVUTSRQPONMLKJIHGFED".First(letter => !used.Contains(letter));
		McpFilePaths paths = Create();

		ToolError error = Refused(() => paths.RequireRead($"{free}:\\dump.bin", Tool));

		Assert.Contains("local fixed drive", error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void RequireRead_CustomParameter_IsNamedInMessageAndDetails()
	{
		McpFilePaths paths = Create();

		ToolError error = Refused(() => paths.RequireRead("relative.bin", Tool, "moduleFile"));

		Assert.StartsWith("moduleFile ", error.Message, StringComparison.Ordinal);
		Assert.Equal("moduleFile", error.Details!.Value.GetProperty("parameter").GetString());
	}

	[Theory]
	[InlineData("registry")]
	[InlineData("registry\\instance.json")]
	[InlineData("REGISTRY\\Instance.JSON")]
	[InlineData("registry\\nested\\deeper\\file.bin")]
	[InlineData("registry\\")]
	[InlineData("elsewhere\\..\\registry\\instance.json")]
	[InlineData("data")]
	[InlineData("data\\appsettings.json")]
	[InlineData("data\\CheatEngine.Mcp.1234.log")]
	[InlineData("Data\\logs\\..\\appsettings.json")]
	public void RequireReadAndWrite_ProtectedDirectories_AreAlwaysRefused(string relative)
	{
		// Even a write root that covers both protected directories does not open them.
		McpFilePaths paths = Create(_scratch.Root);
		string path = _scratch.Root + "\\" + relative;

		ToolError read = Refused(() => paths.RequireRead(path, Tool));
		ToolError write = Refused(() => paths.RequireWrite(path, Tool));

		Assert.Contains("protected MCP directory", read.Message, StringComparison.Ordinal);
		Assert.Contains("protected MCP directory", write.Message, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("registry2\\instance.json")]
	[InlineData("registry.old\\instance.json")]
	[InlineData("database\\appsettings.json")]
	[InlineData("data-backup\\appsettings.json")]
	public void RequireRead_SiblingSharingAPrefix_IsNotProtected(string relative)
	{
		McpFilePaths paths = Create();
		string path = Path.Combine(_scratch.Root, relative);

		Assert.Equal(path, paths.RequireRead(path, Tool));
	}

	[Fact]
	public void RequireRead_JunctionOnThePath_IsRefused()
	{
		string target = _scratch.CreateFolder("target");
		File.WriteAllText(Path.Combine(target, "dump.bin"), "x");
		string link = _scratch.CreateJunction("link", target);
		McpFilePaths paths = Create(_scratch.Root);

		ToolError read = Refused(() => paths.RequireRead(Path.Combine(link, "dump.bin"), Tool));
		ToolError write = Refused(() => paths.RequireWrite(Path.Combine(link, "new.bin"), Tool));
		ToolError self = Refused(() => paths.RequireRead(link, Tool));

		Assert.Contains("reparse point", read.Message, StringComparison.Ordinal);
		Assert.Contains("reparse point", write.Message, StringComparison.Ordinal);
		Assert.Contains("reparse point", self.Message, StringComparison.Ordinal);
		Assert.Equal(Path.Combine(target, "dump.bin"), paths.RequireRead(Path.Combine(target, "dump.bin"), Tool));
	}

	[Fact]
	public void RequireRead_DataDirectoryConfiguredThroughAJunction_IsRefusedUnderItsTargetToo()
	{
		string real = _scratch.CreateFolder("real-data");
		string link = _scratch.CreateJunction("linked-data", real);
		McpFilePaths paths = new(new McpFileOptions(), _scratch.CreateFolder("registry"), link);

		ToolError error = Refused(() => paths.RequireRead(Path.Combine(real, "appsettings.json"), Tool));

		Assert.Contains("protected MCP directory", error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void RequireRead_ShortNameAlias_IsExpandedBeforeTheProtectedDirectoryCheck()
	{
		string folder = _scratch.CreateFolder("LongFolderNameForAlias");
		File.WriteAllText(Path.Combine(folder, "dump.bin"), "x");
		string registry = _scratch.CreateFolder("InstanceRegistry");
		File.WriteAllText(Path.Combine(registry, "instance.json"), "{}");
		McpFilePaths paths = new(new McpFileOptions(), registry, Path.Combine(_scratch.Root, "data"));
		string folderAlias = Path.Combine(_scratch.Root, "LONGFO~1", "dump.bin");
		string registryAlias = Path.Combine(_scratch.Root, "INSTAN~1", "instance.json");

		if (File.Exists(folderAlias) && File.Exists(registryAlias))
		{
			// Normalization expands an existing 8.3 alias, so the protected directory keeps a single name.
			Assert.Equal(Path.Combine(folder, "dump.bin"), paths.RequireRead(folderAlias, Tool));
			ToolError error = Refused(() => paths.RequireRead(registryAlias, Tool));
			Assert.Contains("protected MCP directory", error.Message, StringComparison.Ordinal);
		}
		else
		{
			// 8.3 names are disabled on this volume: an alias is just a name that does not exist yet.
			Assert.Equal(folderAlias, paths.RequireRead(folderAlias, Tool));
		}

		// A real long name that contains a tilde is accepted.
		string tilde = _scratch.CreateFolder("backup~1");
		Assert.Equal(Path.Combine(tilde, "dump.bin"), paths.RequireRead(Path.Combine(tilde, "dump.bin"), Tool));
	}

	[Fact]
	public void RequireWrite_NoRootConfigured_RefusesAndNamesTheSetting()
	{
		McpFilePaths paths = Create();

		ToolError error = Refused(() => paths.RequireWrite(Path.Combine(_scratch.Root, "dump.bin"), Tool));

		Assert.Contains("no write root", error.Message, StringComparison.Ordinal);
		Assert.Contains("Mcp:Files:AllowedRoots", error.Hint, StringComparison.Ordinal);
	}

	[Fact]
	public void RequireWrite_InsideARoot_ReturnsTheFullPathIgnoringCase()
	{
		string root = _scratch.CreateFolder("root");
		McpFilePaths paths = Create(root);

		Assert.Equal(Path.Combine(root, "dump.bin"), paths.RequireWrite(Path.Combine(root, "dump.bin"), Tool));
		Assert.Equal(Path.Combine(root.ToUpperInvariant(), "sub", "dump.bin"),
			paths.RequireWrite(Path.Combine(root.ToUpperInvariant(), "sub", "dump.bin"), Tool));
	}

	[Theory]
	[InlineData("root2\\dump.bin")]
	[InlineData("root.bak\\dump.bin")]
	[InlineData("root")]
	[InlineData("root\\")]
	[InlineData("root\\..\\outside\\dump.bin")]
	[InlineData("dump.bin")]
	public void RequireWrite_OutsideOrOnTheRoot_IsRefused(string relative)
	{
		string root = _scratch.CreateFolder("root");
		McpFilePaths paths = Create(root);

		ToolError error = Refused(() => paths.RequireWrite(_scratch.Root + "\\" + relative, Tool));

		Assert.Contains("not inside a write root", error.Message, StringComparison.Ordinal);
		Assert.Contains("Mcp:Files:AllowedRoots", error.Hint, StringComparison.Ordinal);
	}

	[Fact]
	public void RequireWrite_ExistingDirectory_IsRefused()
	{
		string root = _scratch.CreateFolder("root");
		string folder = _scratch.CreateFolder("root\\existing");
		McpFilePaths paths = Create(root);

		ToolError error = Refused(() => paths.RequireWrite(folder, Tool));

		Assert.Contains("names a directory", error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void AllowedRoots_AreNormalizedWithoutTrailingSeparatorExceptADriveRoot()
	{
		string root = _scratch.CreateFolder("root");
		McpFilePaths paths = Create(root + "\\", "C:\\");

		string[] expected = [root, "C:\\"];
		Assert.Equal(expected, paths.AllowedRoots);
	}

	[Fact]
	public void Constructor_InvalidRootOrDirectory_Throws()
	{
		string registry = _scratch.CreateFolder("registry");
		string data = _scratch.CreateFolder("data");

		Assert.Throws<OptionsValidationException>(() => new McpFilePaths(
			new McpFileOptions { AllowedRoots = ["relative"] }, registry, data));
		Assert.Throws<ArgumentException>(() => new McpFilePaths(new McpFileOptions(), "relative", data));
		Assert.Throws<ArgumentException>(() => new McpFilePaths(new McpFileOptions(), registry, " "));
	}

	[Theory]
	[InlineData("relative\\tables", "Mcp:Files:AllowedRoots:1 must be an absolute path with a drive letter.")]
	[InlineData("\\\\server\\share", "Mcp:Files:AllowedRoots:1 must not be a UNC or device path")]
	[InlineData("\\\\.\\C:\\roots", "Mcp:Files:AllowedRoots:1 must not be a UNC or device path")]
	[InlineData("C:\\roots\\NUL", "Mcp:Files:AllowedRoots:1 must not name a Windows device")]
	[InlineData("C:\\roots:stream", "Mcp:Files:AllowedRoots:1 must not name an alternate data stream.")]
	[InlineData(" ", "Mcp:Files:AllowedRoots:1 is required.")]
	[InlineData("C:\\ROOTS\\", "Mcp:Files:AllowedRoots:1 repeats an earlier root.")]
	public void Validator_InvalidRoot_NamesItsIndex(string root, string message)
	{
		McpFileOptions options = new() { AllowedRoots = ["C:\\roots", root] };

		ValidateOptionsResult result = new McpFileOptionsValidator().Validate(Options.DefaultName, options);

		Assert.True(result.Failed);
		Assert.Contains(result.Failures, failure => failure.StartsWith(message, StringComparison.Ordinal));
		Assert.Throws<OptionsValidationException>(() => new McpFileOptionsValidator().ThrowIfInvalid(options));
	}

	[Fact]
	public void Validator_EmptyOrValidRoots_Succeed()
	{
		McpFileOptionsValidator validator = new();

		Assert.True(validator.Validate(Options.DefaultName, new McpFileOptions()).Succeeded);
		Assert.True(validator.Validate(Options.DefaultName,
			new McpFileOptions { AllowedRoots = ["C:\\dumps", "D:/tables/", "C:\\"] }).Succeeded);
	}

	[Fact]
	public void OpenRead_HeldFile_KeepsWritersAndDeletersOutUntilDisposed()
	{
		string file = Path.Combine(_scratch.CreateFolder("held"), "table.CT");
		File.WriteAllText(file, "<?xml version=\"1.0\"?><CheatTable/>");
		McpFilePaths paths = Create();

		using (HeldFile held = paths.OpenRead(file, Tool, 1024))
		{
			Assert.True(held.IsHeld);
			Assert.True(held.IsComplete);
			Assert.Equal(file, held.FullPath);
			Assert.Equal(held.Length, held.Content.Length);
			Assert.Throws<IOException>(() => File.OpenWrite(file).Dispose());
			Assert.Throws<IOException>(() => File.Delete(file));
			Assert.Throws<IOException>(() => File.Move(file, file + ".moved"));
			// Cheat Engine's loader shares everything (fmShareDenyNone) or denies writers (fmShareDenyWrite): both read.
			using (FileStream denyNone = new(file, FileMode.Open, FileAccess.Read,
				       FileShare.ReadWrite | FileShare.Delete))
			{
				Assert.Equal('<', denyNone.ReadByte());
			}

			using (FileStream denyWrite = new(file, FileMode.Open, FileAccess.Read, FileShare.Read))
			{
				Assert.Equal('<', denyWrite.ReadByte());
			}

			held.Dispose();
			Assert.False(held.IsHeld);
		}

		File.AppendAllText(file, " ");
	}

	[Fact]
	public void OpenRead_FileOverTheLimit_IsHeldWithoutContent()
	{
		string file = Path.Combine(_scratch.CreateFolder("held"), "big.bin");
		File.WriteAllBytes(file, new byte[100]);
		McpFilePaths paths = Create();

		using HeldFile held = paths.OpenRead(file, Tool, 99);

		Assert.False(held.IsComplete);
		Assert.True(held.Content.IsEmpty);
		Assert.Equal(100, held.Length);
		Assert.Throws<IOException>(() => File.Delete(file));
	}

	[Fact]
	public void OpenRead_MissingFileDirectoryOrWriter_AreContractErrors()
	{
		string folder = _scratch.CreateFolder("held");
		string file = Path.Combine(folder, "locked.bin");
		File.WriteAllBytes(file, [1, 2, 3]);
		McpFilePaths paths = Create();

		CheatEngineToolException missing = Assert.Throws<CheatEngineToolException>(() =>
			paths.OpenRead(Path.Combine(folder, "missing.bin"), Tool, 16));
		ToolError directory = Refused(() => paths.OpenRead(folder, Tool, 16));
		CheatEngineToolException busy;
		using (FileStream writer = new(file, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
		{
			busy = Assert.Throws<CheatEngineToolException>(() => paths.OpenRead(file, Tool, 16));
		}

		Assert.Equal((ToolErrorKind.NotFound, ToolHostEffect.NotStarted, Tool),
			(missing.Error.Kind, missing.Error.HostEffect, missing.Error.Operation));
		Assert.Contains("names a directory", directory.Message, StringComparison.Ordinal);
		Assert.Equal((ToolErrorKind.Busy, ToolHostEffect.NotStarted, true),
			(busy.Error.Kind, busy.Error.HostEffect, busy.Error.Retryable));
		using HeldFile released = paths.OpenRead(file, Tool, 16);
		Assert.Equal(new byte[] { 1, 2, 3 }, released.Content.ToArray());
	}

	[Fact]
	public void OpenRead_ProtectedDirectory_IsRefusedBeforeOpening()
	{
		string registry = _scratch.CreateFolder("registry");
		string token = Path.Combine(registry, "instance.json");
		File.WriteAllText(token, "{}");
		McpFilePaths paths = Create();

		ToolError error = Refused(() => paths.OpenRead(token, Tool, 1024));

		Assert.Contains("protected MCP directory", error.Message, StringComparison.Ordinal);
		File.AppendAllText(token, " ");
	}

	private McpFilePaths Create(params string[] roots)
	{
		return new McpFilePaths(new McpFileOptions { AllowedRoots = roots }, Path.Combine(_scratch.Root, "registry"),
			Path.Combine(_scratch.Root, "data"));
	}

	private static ToolError Refused(Action action)
	{
		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(action);
		ToolError error = exception.Error;
		Assert.Equal(ToolErrorKind.InvalidArgument, error.Kind);
		Assert.Equal(ToolHostEffect.NotStarted, error.HostEffect);
		Assert.Equal(Tool, error.Operation);
		Assert.False(error.Retryable);
		Assert.NotNull(error.Details);
		return error;
	}

	private static ToolError Refused(Func<object> action)
	{
		return Refused(() =>
		{
			_ = action();
		});
	}

	/// <summary>
	///     A folder on a local fixed drive whose path has no short-name segment (a <c>RUNNER~1</c> temp folder would be
	///     refused as an 8.3 alias), holding the protected directories and the files a test creates.
	/// </summary>
	internal sealed class Scratch : IDisposable
	{
		internal Scratch()
		{
			string temp = Path.GetFullPath(Path.GetTempPath());
			string parent = temp.Contains('~', StringComparison.Ordinal)
				? Path.Combine(RepositoryPaths.Root, "artifacts", "test-scratch")
				: temp;
			Root = Path.Combine(parent, $"CheatEngine.Mcp.FilePaths-{Guid.NewGuid():N}");
			Directory.CreateDirectory(Root);
		}

		internal string Root
		{
			get;
		}

		private List<string> Junctions
		{
			get;
		} = [];

		public void Dispose()
		{
			// A recursive deletion refuses junctions; removing one alone never touches its target.
			foreach (string link in Junctions)
			{
				Directory.Delete(link, false);
			}

			if (Directory.Exists(Root))
			{
				Directory.Delete(Root, true);
			}
		}

		internal string CreateFolder(string relative)
		{
			return Directory.CreateDirectory(Path.Combine(Root, relative)).FullName;
		}

		/// <summary>Creates a directory junction, which needs no privilege, unlike a symbolic link.</summary>
		internal string CreateJunction(string relative, string target)
		{
			string link = Path.Combine(Root, relative);
			ProcessStartInfo start = new("cmd.exe")
			{
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true
			};
			foreach (string argument in (string[]) ["/d", "/c", "mklink", "/J", link, target])
			{
				start.ArgumentList.Add(argument);
			}

			using Process process = Process.Start(start)!;
			string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
			process.WaitForExit();
			Assert.True(process.ExitCode == 0 && Directory.Exists(link), output);
			Assert.True((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0);
			Junctions.Add(link);
			return link;
		}
	}
}
