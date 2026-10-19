using System.Buffers.Binary;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tools.Modules;

namespace CheatEngine.Mcp.Tests.Tools.Modules;

/// <summary>Build-bound observations retain relative addresses without accepting a different target build.</summary>
public sealed class ModuleBuildBindingTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public void CaptureAndValidate_ReportsExactAddressAndPeSectionWithoutReadingCode()
	{
		ModuleSymbolTarget target = ModuleToolTests.LoadedSample();
		ModuleRvaBinding binding = ModuleBuildBindings.Capture(target.Client, "sample.dll", "0x4010", Token);
		ModuleRvaResolution result = ModuleBuildBindings.Validate(target.Client, binding, Token);

		Assert.Equal("sample.dll", binding.Identity.Name);
		Assert.Equal(8, binding.Identity.PointerBytes);
		Assert.Equal(0x6000, binding.Identity.ImageSize);
		Assert.Equal(64, binding.Identity.Fingerprint.Length);
		Assert.All(binding.Identity.Fingerprint, value => Assert.True(char.IsAsciiHexDigit(value)));
		Assert.Equal("4010", binding.Rva);
		Assert.Equal("7FFA00004010", result.Address);
		Assert.Equal("7FFA00000000", result.BaseAddress);
		Assert.Equal(ModuleSymbolTarget.ProcessId, result.ProcessId);
		Assert.Equal(7, result.SelectionEpoch);
		Assert.Equal(".text", result.Section);
		Assert.True(result.Executable);
		Assert.Equal(2, target.Calls("ReadBytesDetailed"));
	}

	[Fact]
	public void Validate_RebasedSameBuild_UsesTheCurrentBaseAndReportsDataAsNonExecutable()
	{
		TestPe pe = SampleModule.Build();
		ModuleSymbolTarget original = ModuleToolTests.LoadedSample(pe);
		ModuleRvaBinding binding = ModuleBuildBindings.Capture(original.Client, "sample.dll", "2008", Token);
		const ulong newBase = 0x7FFB00000000;
		ModuleSymbolTarget moved = new()
		{
			MemoryBase = newBase,
			Memory = pe.Map(newBase)
		};
		moved.AddModule("SAMPLE.DLL", newBase, pe.SizeOfImage);

		ModuleRvaResolution result = ModuleBuildBindings.Validate(moved.Client, binding, Token);

		Assert.Equal("7FFB00002008", result.Address);
		Assert.Equal(".data", result.Section);
		Assert.False(result.Executable);
		Assert.Equal(binding.Identity.Fingerprint, result.Module.Fingerprint);
	}

	[Theory]
	[InlineData(8)] // COFF timestamp
	[InlineData(24 + 16)] // entry point RVA
	public void Validate_ChangedBuildMetadata_RefusesTheOldObservation(int relativeHeaderOffset)
	{
		ModuleSymbolTarget target = ModuleToolTests.LoadedSample();
		ModuleRvaBinding binding = ModuleBuildBindings.Capture(target.Client, "sample.dll", "4010", Token);
		int offset = TestPe.NtOffset + relativeHeaderOffset;
		BinaryPrimitives.WriteUInt32LittleEndian(target.Memory.AsSpan(offset),
			BinaryPrimitives.ReadUInt32LittleEndian(target.Memory.AsSpan(offset)) + 1);

		CheatEngineToolException error = Assert.Throws<CheatEngineToolException>(() =>
			ModuleBuildBindings.Validate(target.Client, binding, Token));

		Assert.Equal(ToolErrorKind.InvalidState, error.Error.Kind);
		Assert.Contains("does not match", error.Error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void Capture_TargetChangesDuringHeaderRead_DiscardsTheBinding()
	{
		ModuleSymbolTarget target = ModuleToolTests.LoadedSample();
		target.BeforeRead = _ => target.SelectionEpoch++;

		CheatEngineToolException error = Assert.Throws<CheatEngineToolException>(() =>
			ModuleBuildBindings.Capture(target.Client, "sample.dll", "4010", Token));

		Assert.Equal(ToolErrorKind.TargetChanged, error.Error.Kind);
	}

	[Fact]
	public void Capture_AmbiguousModuleNames_RefusesBeforeReadingEitherImage()
	{
		ModuleSymbolTarget target = ModuleToolTests.LoadedSample();
		target.AddModule("sample.dll", 0x7FFC00000000, 0x6000);

		CheatEngineToolException error = Assert.Throws<CheatEngineToolException>(() =>
			ModuleBuildBindings.Capture(target.Client, "sample.dll", "4010", Token));

		Assert.Equal(ToolErrorKind.InvalidState, error.Error.Kind);
		Assert.Equal(0, target.Calls("ReadBytesDetailed"));
	}

	[Theory]
	[InlineData("")]
	[InlineData("-1")]
	[InlineData("100000000")]
	[InlineData(" 1")]
	[InlineData("1 ")]
	[InlineData("1\0")]
	[InlineData("0x1\0")]
	public void Capture_InvalidRva_RefusesBeforeAnyClientAccess(string rva)
	{
		ModuleSymbolTarget target = ModuleToolTests.LoadedSample();

		CheatEngineToolException error = Assert.Throws<CheatEngineToolException>(() =>
			ModuleBuildBindings.Capture(target.Client, "sample.dll", rva, Token));

		Assert.Equal(ToolErrorKind.InvalidArgument, error.Error.Kind);
		Assert.Equal(0, target.TotalClientCalls);
	}

	[Fact]
	public void Validate_RvaAtImageEnd_RefusesBeforeTargetAccess()
	{
		ModuleSymbolTarget target = ModuleToolTests.LoadedSample();
		ModuleRvaBinding binding = new(new ModuleBuildIdentity("sample.dll", 8, new string('A', 64), 0x6000), "6000");

		CheatEngineToolException error = Assert.Throws<CheatEngineToolException>(() =>
			ModuleBuildBindings.Validate(target.Client, binding, Token));

		Assert.Equal(ToolErrorKind.InvalidArgument, error.Error.Kind);
		Assert.Equal(0, target.TotalClientCalls);
	}

	[Fact]
	public void Capture_CancelledBeforeRead_DoesNotTouchTheTarget()
	{
		ModuleSymbolTarget target = ModuleToolTests.LoadedSample();
		using CancellationTokenSource cancelled = new();
		cancelled.Cancel();

		Assert.Throws<OperationCanceledException>(() =>
			ModuleBuildBindings.Capture(target.Client, "sample.dll", "4010", cancelled.Token));
		Assert.Equal(0, target.TotalClientCalls);
	}
}
