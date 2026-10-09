using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json.Serialization.Metadata;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Core.Files;
using CheatEngine.Mcp.Core.Lua;
using CheatEngine.Mcp.Tests.Core;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Exec;

using Microsoft.Extensions.Options;

namespace CheatEngine.Mcp.Tests.Tools.Exec;

/// <summary>Portable coverage for the bounded C# compiler bridge and its required immediate export.</summary>
public sealed class ExecCSharpToolsTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public void CompileCSharp_DisabledTargetExecution_RefusesBeforeFilesOrDispatch()
	{
		using McpFilePathsTests.Scratch scratch = new();
		Harness harness = new(new McpFeatureOptions { EnableTargetCodeExecution = false });

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() => harness.Tools(scratch)
			.CompileCSharp("public class C {}", Path.Combine(scratch.Root, "out.dll"), cancellationToken: Token));

		Assert.Equal((ToolErrorKind.CapabilityDisabled, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal(0, harness.Dispatcher.Calls);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(131_073)]
	public void CompileCSharp_SourceOutsideBound_RefusesBeforeFilesOrDispatch(int length)
	{
		using McpFilePathsTests.Scratch scratch = new();
		Harness harness = new();
		string source = length == 0 ? "" : new string('x', length);

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() => harness.Tools(scratch)
			.CompileCSharp(source, Path.Combine(scratch.Root, "out.dll"), cancellationToken: Token));

		Assert.Equal((ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal(0, harness.Dispatcher.Calls);
	}

	[Fact]
	public void CompileCSharp_MoreThanThirtyTwoReferences_RefusesBeforeFilesOrDispatch()
	{
		using McpFilePathsTests.Scratch scratch = new();
		Harness harness = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() => harness.Tools(scratch)
			.CompileCSharp("class C {}", Path.Combine(scratch.Root, "out.dll"), Enumerable.Repeat("C:\\missing.dll", 33).ToArray(),
				cancellationToken: Token));

		Assert.Equal((ToolErrorKind.LimitExceeded, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal(0, harness.Dispatcher.Calls);
	}

	[Fact]
	public void CompileCSharp_HeldReferenceCannotChangeAndGeneratedAssemblyIsAtomicallyExported()
	{
		using McpFilePathsTests.Scratch scratch = new();
		string input = Path.Combine(scratch.Root, "reference.dll");
		string generated = Path.Combine(scratch.Root, "ce-generated.dll");
		string output = Path.Combine(scratch.Root, "exported.dll");
		byte[] bytes = [1, 2, 3, 4, 5];
		File.WriteAllBytes(input, [0x4D, 0x5A]);
		File.WriteAllBytes(generated, bytes);
		Harness harness = new(executor: new CompilerLua(generated, () =>
			Assert.Throws<IOException>(() => File.WriteAllBytes(input, [9]))));

		ExecCSharpCompileResult result = harness.Tools(scratch).CompileCSharp("class C {}", output, [input],
			cancellationToken: Token);

		Assert.Equal(output, result.OutputPath);
		Assert.Equal(bytes.Length, result.Length);
		Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), result.Sha256);
		Assert.Equal(bytes, File.ReadAllBytes(output));
		Assert.True(File.Exists(generated));
		Assert.Equal(1, harness.Dispatcher.Calls);
	}

	[Fact]
	public void CompileCSharp_MissingGeneratedAssembly_ReportsPartialEffectWithoutPublishingOutput()
	{
		using McpFilePathsTests.Scratch scratch = new();
		string output = Path.Combine(scratch.Root, "exported.dll");
		Harness harness = new(executor: new CompilerLua(Path.Combine(scratch.Root, "expired.dll")));

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() => harness.Tools(scratch)
			.CompileCSharp("class C {}", output, cancellationToken: Token));

		Assert.Equal((ToolErrorKind.PartialEffect, ToolHostEffect.Started),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal("false", exception.Error.Details!.Value.GetProperty("outputPublicationMayHaveCompleted").GetRawText());
		Assert.False(File.Exists(output));
	}

	[Theory]
	[InlineData("line 4: expected ;")]
	[InlineData("Could not load file or assembly 'System.CodeDom'")]
	public void CompileCSharp_CompilerDiagnostic_ReportsBoundedDetailsWithoutInventedCodeOrSeverity(string diagnostic)
	{
		using McpFilePathsTests.Scratch scratch = new();
		Harness harness = new(executor: new OutcomeLua(new ExecCSharpCompilerOutput(null, diagnostic, false)));

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() => harness.Tools(scratch)
			.CompileCSharp("class C {}", Path.Combine(scratch.Root, "exported.dll"), cancellationToken: Token));

		Assert.Equal((ToolErrorKind.HostRefused, ToolHostEffect.Unknown),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal(diagnostic, exception.Error.Details!.Value.GetProperty("text").GetString());
		Assert.False(exception.Error.Details!.Value.GetProperty("truncated").GetBoolean());
		Assert.False(exception.Error.Details!.Value.TryGetProperty("code", out _));
		Assert.False(exception.Error.Details!.Value.TryGetProperty("severity", out _));
	}

	[Fact]
	public void CompileCSharp_CompilerFailureWithUnremovableReservedOutput_ReportsCleanupUnconfirmed()
	{
		using McpFilePathsTests.Scratch scratch = new();
		string? partial = null;
		Harness harness = new(executor: new OutcomeLua(new ExecCSharpCompilerOutput(null, "diagnostic"), () =>
		{
			partial = Assert.Single(Directory.EnumerateFiles(scratch.Root, "*.partial"));
			File.SetAttributes(partial, File.GetAttributes(partial) | FileAttributes.ReadOnly);
		}));
		try
		{
			CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() => harness.Tools(scratch)
				.CompileCSharp("class C {}", Path.Combine(scratch.Root, "exported.dll"), cancellationToken: Token));

			Assert.Equal((ToolErrorKind.PartialEffect, ToolHostEffect.CleanupUnconfirmed),
				(exception.Error.Kind, exception.Error.HostEffect));
			Assert.False(exception.Error.Details!.Value.GetProperty("outputPublicationMayHaveCompleted").GetBoolean());
		}
		finally
		{
			if (partial is not null && File.Exists(partial))
			{
				File.SetAttributes(partial, FileAttributes.Normal);
				File.Delete(partial);
			}
		}
	}

	[Fact]
	public void CompileCSharp_GeneratedAssemblyBeyondExportCap_ReportsPartialEffectWithoutPublishingOutput()
	{
		using McpFilePathsTests.Scratch scratch = new();
		string generated = Path.Combine(scratch.Root, "too-large.dll");
		string output = Path.Combine(scratch.Root, "exported.dll");
		using (FileStream stream = File.Create(generated))
		{
			stream.SetLength((16 * 1024 * 1024) + 1L);
		}

		Harness harness = new(executor: new CompilerLua(generated));

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() => harness.Tools(scratch)
			.CompileCSharp("class C {}", output, cancellationToken: Token));

		Assert.Equal((ToolErrorKind.PartialEffect, ToolHostEffect.Started),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.False(File.Exists(output));
	}

	[Fact]
	public void CompileCSharp_CancelledAfterCompilerReturn_ReportsPartialEffectWithoutPublishingOutput()
	{
		using McpFilePathsTests.Scratch scratch = new();
		using CancellationTokenSource cancellation = new();
		string generated = Path.Combine(scratch.Root, "ce-generated.dll");
		string output = Path.Combine(scratch.Root, "exported.dll");
		File.WriteAllBytes(generated, [1]);
		Harness harness = new(executor: new CompilerLua(generated, cancellation.Cancel));

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() => harness.Tools(scratch)
			.CompileCSharp("class C {}", output, cancellationToken: cancellation.Token));

		Assert.Equal((ToolErrorKind.PartialEffect, ToolHostEffect.Started),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.False(File.Exists(output));
	}

	[Fact]
	public void CompileCSharp_ExistingOutputWithoutOverwrite_RefusesBeforeCompilerDispatch()
	{
		using McpFilePathsTests.Scratch scratch = new();
		string output = Path.Combine(scratch.Root, "exported.dll");
		File.WriteAllBytes(output, [8]);
		Harness harness = new(executor: new CompilerLua(Path.Combine(scratch.Root, "unused.dll")));

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() => harness.Tools(scratch)
			.CompileCSharp("class C {}", output, cancellationToken: Token));

		Assert.Equal(ToolHostEffect.NotStarted, exception.Error.HostEffect);
		Assert.Equal([8], File.ReadAllBytes(output));
		Assert.Equal(0, harness.Dispatcher.Calls);
	}

	[Fact]
	public void CompileCSharp_DeclaresTargetCodeExecutionAndNeverLoadsCallerSource()
	{
		MethodInfo method = typeof(ExecCSharpTools).GetMethod(nameof(ExecCSharpTools.CompileCSharp))!;

		Assert.Contains(method.GetCustomAttributes<RequiresFeatureAttribute>(),
			static requirement => requirement.Feature == McpFeature.TargetCodeExecution);
		LuaFixedScriptAssert.NeverLoadsCode(ExecCSharpScripts.Compile);
		Assert.Equal([McpFeature.TargetCodeExecution], LuaFeatureScan.Scan(ExecCSharpScripts.Compile));
		Assert.DoesNotContain("inject", ExecCSharpScripts.Compile, StringComparison.OrdinalIgnoreCase);
	}

	private sealed class Harness
	{
		internal Harness(McpFeatureOptions? features = null, IFixedLuaExecutor? executor = null)
		{
			IOptions<McpExecutionOptions> execution = Options.Create(new McpExecutionOptions());
			Executor = executor ?? UnavailableFixedLuaExecutor.Instance;
			Dispatch = new ToolDispatch(ClientTestDouble.Client(Dispatcher.Dispatcher, CancellationToken.None),
				new McpFeatureGate(Options.Create(features ?? new McpFeatureOptions())), execution,
				new DispatchStatistics(execution), TimeProvider.System, new RecordingLogger<ToolDispatch>(), Executor);
		}

		internal RecordingDispatcher Dispatcher { get; } = new();
		internal IFixedLuaExecutor Executor
		{
			get;
		}
		internal ToolDispatch Dispatch
		{
			get;
		}

		internal ExecCSharpTools Tools(McpFilePathsTests.Scratch scratch)
		{
			string registry = scratch.CreateFolder("registry");
			string data = scratch.CreateFolder("data");
			return new ExecCSharpTools(Dispatch, new McpFilePaths(new McpFileOptions { AllowedRoots = [scratch.Root] },
				registry, data));
		}
	}

	private sealed class CompilerLua(string path, Action? duringExecution = null) : IFixedLuaExecutor
	{
		public LuaJsonResult<T> Execute<T>(string operation, string source, JsonTypeInfo<T> resultType,
			LuaJsonBufferPool buffers, LuaOpaqueValueHandling opaque, CancellationToken cancellationToken)
		{
			Assert.Equal(CheatEngineToolNames.ExecCompileCSharp, operation);
			duringExecution?.Invoke();
			return new LuaJsonResult<T>((T) (object) new ExecCSharpCompilerOutput(path), null, 0);
		}
	}

	private sealed class OutcomeLua(ExecCSharpCompilerOutput outcome, Action? duringExecution = null) : IFixedLuaExecutor
	{
		public LuaJsonResult<T> Execute<T>(string operation, string source, JsonTypeInfo<T> resultType,
			LuaJsonBufferPool buffers, LuaOpaqueValueHandling opaque, CancellationToken cancellationToken)
		{
			duringExecution?.Invoke();
			return new LuaJsonResult<T>((T) (object) outcome, null, 0);
		}
	}
}
