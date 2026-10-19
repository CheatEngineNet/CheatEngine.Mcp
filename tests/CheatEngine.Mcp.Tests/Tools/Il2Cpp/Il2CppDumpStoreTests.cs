using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Files;
using CheatEngine.Mcp.Tests.Core;
using CheatEngine.Mcp.Tests.Tools.Modules;
using CheatEngine.Mcp.Tools.Il2Cpp;

namespace CheatEngine.Mcp.Tests.Tools.Il2Cpp;

/// <summary>Dataset lookup, source identity and explicit lifecycle behavior.</summary>
public sealed class Il2CppDumpStoreTests : IDisposable
{
	private readonly McpFilePathsTests.Scratch _scratch = new();
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	public void Dispose() => _scratch.Dispose();

	[Fact]
	public void Tools_OfflineCatalogOperations_ImportSearchResolveAndDelete()
	{
		ModuleSymbolTarget target = ModuleToolTests.LoadedSample();
		McpFilePaths paths = Paths();
		Il2CppTools tools = new(target.Dispatch, paths, new Il2CppDumpStore());
		string dumpPath = Write("dump.cs", """
// Image 0: Assembly-CSharp.dll - 0
public class Player // TypeDefIndex: 1
{
// Methods
// RVA: 0x4010 Offset: 0xA10
public void Heal() { }
}
""");
		string modulePath = Path.Combine(_scratch.Root, "sample.dll");
		File.WriteAllBytes(modulePath, SampleModule.Build().File());

		Il2CppDumpImport imported = tools.ImportDump(dumpPath, modulePath, Token);
		Assert.Equal(("dump_cs_v1", 1, "dump.cs", 1, 1),
			(imported.Format, imported.FormatVersion, imported.FileName, imported.TypeCount, imported.MemberCount));
		Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dumpPath))), imported.Sha256);
		Il2CppTypePage types = tools.FindTypes(imported.DatasetId, "player", 0, 1, Token);
		Il2CppType type = Assert.Single(types.Types);
		Assert.Equal((1, (int?) null, "Assembly-CSharp.dll:1"), (types.Total, types.NextOffset, type.TypeId));
		Il2CppMemberPage members = tools.FindMembers(imported.DatasetId, "method", type.TypeId, "heal", 0, 1, Token);
		Il2CppMember method = Assert.Single(members.Members);
		Assert.Equal((1, "4010", "Heal"), (members.Total, method.Rva, method.Name));
		Il2CppMethodResolution resolution = tools.ResolveMethod(imported.DatasetId, method.MemberId, Token);

		Assert.Equal(("4010", "7FFA00004010", ".text", true),
			(resolution.Binding.Rva, resolution.Resolution.Address, resolution.Resolution.Section,
				resolution.Resolution.Executable));
		Assert.True(tools.DeleteDump(imported.DatasetId, Token).Deleted);
		Assert.Equal(ToolErrorKind.NotFound, Assert.Throws<CheatEngineToolException>(() =>
			tools.FindTypes(imported.DatasetId, cancellationToken: Token)).Error.Kind);
	}

	[Fact]
	public void Tools_ResolveMethod_RejectsChangedTargetBuildAndUnboundImports()
	{
		ModuleSymbolTarget target = ModuleToolTests.LoadedSample();
		Il2CppTools tools = new(target.Dispatch, Paths(), new Il2CppDumpStore());
		string dumpPath = Write("bound.cs", "// Image 0: sample.dll - 0\npublic class Probe // TypeDefIndex: 0\n{\n// Methods\n// RVA: 0x4010\npublic void Run() { }\n}\n");
		string modulePath = Path.Combine(_scratch.Root, "sample.dll");
		File.WriteAllBytes(modulePath, SampleModule.Build().File());
		Il2CppDumpImport bound = tools.ImportDump(dumpPath, modulePath, Token);
		Il2CppDumpImport unbound = tools.ImportDump(dumpPath, cancellationToken: Token);

		Assert.Equal(ToolErrorKind.InvalidState, Assert.Throws<CheatEngineToolException>(() =>
			tools.ResolveMethod(unbound.DatasetId, "sample.dll:0:method:0", Token)).Error.Kind);
		int timestamp = TestPe.NtOffset + 8;
		BinaryPrimitives.WriteUInt32LittleEndian(target.Memory.AsSpan(timestamp),
			BinaryPrimitives.ReadUInt32LittleEndian(target.Memory.AsSpan(timestamp)) + 1);

		CheatEngineToolException error = Assert.Throws<CheatEngineToolException>(() =>
			tools.ResolveMethod(bound.DatasetId, "sample.dll:0:method:0", Token));
		Assert.Equal(ToolErrorKind.InvalidState, error.Error.Kind);
		Assert.Contains("does not match", error.Error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void Tools_ImportRejectsMalformedUtf8AndOversizeFiles()
	{
		ModuleSymbolTarget target = ModuleToolTests.LoadedSample();
		Il2CppTools tools = new(target.Dispatch, Paths(), new Il2CppDumpStore());
		string malformed = Path.Combine(_scratch.Root, "malformed.cs");
		File.WriteAllBytes(malformed, [0xFF, 0xFE, 0xFA]);

		Assert.Equal(ToolErrorKind.InvalidArgument, Assert.Throws<CheatEngineToolException>(() =>
			tools.ImportDump(malformed, cancellationToken: Token)).Error.Kind);

		string oversized = Path.Combine(_scratch.Root, "oversized.cs");
		using (FileStream stream = File.Create(oversized))
		{
			stream.SetLength(Il2CppDumpStore.MaximumBytes + 1L);
		}

		Assert.Equal(ToolErrorKind.LimitExceeded, Assert.Throws<CheatEngineToolException>(() =>
			tools.ImportDump(oversized, cancellationToken: Token)).Error.Kind);
	}

	[Fact]
	public void ImportFindAndDelete_PreservesSourceIdentityAndStableFilteredPages()
	{
		string source = """
// Image 0: A.dll - 0
// Namespace: N
public class Alpha // TypeDefIndex: 1
{
// Methods
// RVA: 0x10 Offset: 0x10
public void Apply() { }
// RVA: 0x20 Offset: 0x20
public void Build() { }
}
public class Beta // TypeDefIndex: 2
{
// Methods
// RVA: 0x30 Offset: 0x30
public void ApplyBeta() { }
}
""";
		Il2CppDumpStore store = new();
		Il2CppDumpImport imported = Import(store, source);

		Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))), imported.Sha256);
		Assert.Equal((2, 3), (imported.TypeCount, imported.MemberCount));
		Il2CppMemberPage page = store.FindMembers(imported.DatasetId, "method", "A.dll:1", "b", 0, 1);
		Assert.Equal((1, (int?) null), (page.Total, page.NextOffset));
		Assert.Equal("Build", Assert.Single(page.Members).Name);
		Assert.Equal("A.dll:2", Assert.Single(store.FindTypes(imported.DatasetId, "beta", 0, 100).Types).TypeId);

		Assert.True(store.Delete(imported.DatasetId).Deleted);
		Assert.False(store.Delete(imported.DatasetId).Deleted);
	}

	[Fact]
	public void Import_FourDatasetCapRefusesFifthWithoutReplacingAnEarlierDataset()
	{
		Il2CppDumpStore store = new();
		Il2CppDumpImport[] imports = [.. Enumerable.Range(0, 4).Select(index => Import(store, Source(index)))];

		CheatEngineToolException error = Assert.Throws<CheatEngineToolException>(() => Import(store, Source(9)));

		Assert.Equal(ToolErrorKind.LimitExceeded, error.Error.Kind);
		Assert.Equal("A.dll:0", Assert.Single(store.FindTypes(imports[0].DatasetId, null, 0, 100).Types).TypeId);
	}

	private Il2CppDumpImport Import(Il2CppDumpStore store, string source)
	{
		string path = Write($"{Guid.NewGuid():N}.cs", source);
		McpFilePaths paths = Paths();
		using HeldFile file = paths.OpenRead(path, "il2cpp_import_dump", Il2CppDumpStore.MaximumBytes);
		return store.Import(file, null, Token);
	}

	private McpFilePaths Paths() => new(new McpFileOptions(), _scratch.CreateFolder($"registry-{Guid.NewGuid():N}"), _scratch.CreateFolder($"data-{Guid.NewGuid():N}"));

	private string Write(string name, string source)
	{
		string path = Path.Combine(_scratch.Root, name);
		File.WriteAllText(path, source, new UTF8Encoding(false));
		return path;
	}

	private static string Source(int index) => $"// Image 0: A.dll - 0\npublic class T{index} // TypeDefIndex: {index}\n{{\n}}\n";
}
