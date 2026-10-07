using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text.Json;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Core.Files;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools.Exec;

/// <summary>Compiles bounded C# source through Cheat Engine and immediately exports its CE-owned temporary assembly.</summary>
[McpServerToolType]
public sealed class ExecCSharpTools
{
	private const int MaximumSourceLength = 131_072;
	private const int MaximumReferences = 32;
	private const int MaximumArtifactBytes = 16 * 1024 * 1024;
	private const int MaximumPathLength = 32_767;
	private readonly ToolDispatch _dispatch;
	private readonly McpFilePaths _files;

	/// <summary>Creates the C# compiler tool container without accessing Cheat Engine or the host filesystem.</summary>
	public ExecCSharpTools(ToolDispatch dispatch, McpFilePaths files)
	{
		ArgumentNullException.ThrowIfNull(dispatch);
		ArgumentNullException.ThrowIfNull(files);
		_dispatch = dispatch;
		_files = files;
	}

	/// <summary>Compiles C# source through Cheat Engine and atomically exports the generated assembly.</summary>
	[McpServerTool(Name = CheatEngineToolNames.ExecCompileCSharp, Title = "Compile managed source", ReadOnly = false,
		Destructive = true, Idempotent = false, OpenWorld = true, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.BlockingNative)]
	[RequiresFeature(McpFeature.TargetCodeExecution)]
	[Description(
		"Compile up to 131072 characters of C# source with Cheat Engine's compileCS bridge and immediately copy its " +
		"generated assembly to outputPath under an approved write root. The compiler uses the optional checked coreAssembly " +
		"and up to 32 checked reference assemblies. Compilation does not inject or invoke the assembly. Cheat Engine owns " +
		"its original generated file and may remove it when Cheat Engine, or another instance, closes; MCP never deletes that " +
		"file. Compiler availability still depends on Cheat Engine's supported .NET Framework compiler prerequisites.")]
	public ExecCSharpCompileResult CompileCSharp(
		[Description("C# source, 1 to 131072 characters.")]
		string source,
		[Description("Approved-root destination for the exported compiled assembly. It must not already exist unless overwrite is true.")]
		string outputPath,
		[Description("0 to 32 absolute local reference-assembly paths held against changes while Cheat Engine compiles.")]
		string[]? referencePaths = null,
		[Description("Optional absolute local core assembly path held against changes while Cheat Engine compiles.")]
		string? coreAssembly = null,
		[Description("Allow the approved-root outputPath to atomically replace an existing file.")]
		bool overwrite = false,
		CancellationToken cancellationToken = default)
	{
		_dispatch.Features.Require(McpFeature.TargetCodeExecution, CheatEngineToolNames.ExecCompileCSharp);
		if (string.IsNullOrWhiteSpace(source) || source.Length > MaximumSourceLength)
		{
			throw CheatEngineToolException.InvalidArgument("source", $"must contain 1 to {MaximumSourceLength} characters.");
		}

		string[] references = referencePaths ?? [];
		if (references.Length > MaximumReferences)
		{
			throw CheatEngineToolException.LimitExceeded("referencePaths", $"contains more than {MaximumReferences} entries.");
		}

		using HeldFiles held = HoldInputs(references, coreAssembly);
		using McpFileWrite write = BeginWrite(outputPath, overwrite);
		bool exportStarted = false;
		try
		{
			return _dispatch.Run(CheatEngineToolNames.ExecCompileCSharp, token =>
			{
				ExecCSharpCompilerOutput compiled = _dispatch.ExecuteLua(CheatEngineToolNames.ExecCompileCSharp,
					ExecCSharpScripts.Compile, ExecCSharpJsonContext.Default.ExecCSharpCompilerOutput, token, source,
					held.ReferencePaths, held.CoreAssemblyPath);
				if (!compiled.CompilerAvailable)
				{
					throw CheatEngineToolException.Unsupported("Cheat Engine does not provide the compileCS C# compiler.",
						CheatEngineToolNames.ExecCompileCSharp);
				}

				if (compiled.AssemblyPath is null)
				{
					throw CompilerFailure(compiled);
				}

				exportStarted = true;
				return Export(compiled, write, token);
			}, cancellationToken);
		}
		catch (Exception exception) when (!exportStarted)
		{
			if (write.TryAbort())
			{
				ExceptionDispatchInfo.Capture(exception).Throw();
			}

			throw PreExportCleanupFailure(exception);
		}
	}

	private HeldFiles HoldInputs(string[] references, string? coreAssembly)
	{
		List<HeldFile> held = new(references.Length + (coreAssembly is null ? 0 : 1));
		try
		{
			foreach (string reference in references)
			{
				held.Add(_files.OpenRead(reference, CheatEngineToolNames.ExecCompileCSharp, 0, "referencePaths"));
			}

			HeldFile? core = coreAssembly is null
				? null
				: _files.OpenRead(coreAssembly, CheatEngineToolNames.ExecCompileCSharp, 0, "coreAssembly");
			return new HeldFiles(held, core);
		}
		catch
		{
			foreach (HeldFile file in held)
			{
				file.Dispose();
			}

			throw;
		}
	}

	private McpFileWrite BeginWrite(string outputPath, bool overwrite)
	{
		try
		{
			return _files.BeginWrite(outputPath, CheatEngineToolNames.ExecCompileCSharp, overwrite, "outputPath");
		}
		catch (IOException exception)
		{
			throw new CheatEngineToolException(new ToolError(ToolErrorKind.InvalidState,
				"The outputPath destination could not be reserved.", CheatEngineToolNames.ExecCompileCSharp,
				ToolHostEffect.NotStarted, false,
				"Check that outputPath is writable and does not already exist, then repeat the call."), exception);
		}
	}

	private ExecCSharpCompileResult Export(ExecCSharpCompilerOutput compiled, McpFileWrite write,
		CancellationToken cancellationToken)
	{
		bool publishAttempted = false;
		try
		{
			if (compiled.AssemblyPath!.Length > MaximumPathLength)
			{
				throw CheatEngineToolException.InvalidArgument("generatedAssemblyPath",
					$"must contain an absolute path of at most {MaximumPathLength} characters.");
			}

			using HeldFile generated = _files.OpenRead(compiled.AssemblyPath, CheatEngineToolNames.ExecCompileCSharp,
				MaximumArtifactBytes, "generatedAssemblyPath");
			if (!generated.IsComplete)
			{
				throw CheatEngineToolException.LimitExceeded("generatedAssemblyPath",
					$"exceeds the {MaximumArtifactBytes} byte export limit.");
			}

			using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
			Copy(generated, write.Stream, hash, cancellationToken);
			string sha256 = Convert.ToHexString(hash.GetHashAndReset());
			publishAttempted = true;
			write.Commit();
			return new ExecCSharpCompileResult(write.FullPath, generated.Length, sha256);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CheatEngineToolException or OperationCanceledException)
		{
			bool cleanup = !publishAttempted && write.TryAbort();
			throw ExportFailure(exception, cleanup, publishAttempted);
		}
	}

	private static void Copy(HeldFile source, Stream destination, IncrementalHash hash, CancellationToken cancellationToken)
	{
		byte[] buffer = new byte[65_536];
		long offset = 0;
		while (offset < source.Length)
		{
			cancellationToken.ThrowIfCancellationRequested();
			int read = source.ReadAt(buffer, offset);
			if (read == 0)
			{
				throw new IOException("The generated assembly ended before its held length.");
			}

			destination.Write(buffer, 0, read);
			hash.AppendData(buffer, 0, read);
			offset += read;
		}
	}

	private static CheatEngineToolException CompilerFailure(ExecCSharpCompilerOutput output)
	{
		ExecCSharpCompilerDiagnostic details = new(output.Diagnostic ?? "Cheat Engine returned no compiler diagnostic.",
			output.DiagnosticTruncated);
		JsonElement serialized = JsonSerializer.SerializeToElement(details,
			ExecCSharpJsonContext.Default.ExecCSharpCompilerDiagnostic);
		return new CheatEngineToolException(new ToolError(ToolErrorKind.HostRefused,
			"Cheat Engine could not compile the C# source; inspect the bounded compiler diagnostic.",
			CheatEngineToolNames.ExecCompileCSharp, ToolHostEffect.Unknown, false,
			"Correct the C# source or references, then retry.", serialized));
	}

	private static CheatEngineToolException PreExportCleanupFailure(Exception exception)
	{
		CheatEngineToolException partial = CheatEngineToolException.PartialEffect(
			"MCP did not start assembly export, but could not confirm removal of its protected temporary destination file.",
			ToolHostEffect.CleanupUnconfirmed, new ExecCSharpExportFailure(false, false),
			ExecCSharpJsonContext.Default.ExecCSharpExportFailure,
			hint: "The destination was not published, but a protected .partial file may remain under outputPath's directory; inspect it before retrying.");
		return new CheatEngineToolException(partial.Error, exception);
	}

	private static CheatEngineToolException ExportFailure(Exception exception, bool cleanupConfirmed,
		bool outputPublicationMayHaveCompleted)
	{
		ToolHostEffect effect = outputPublicationMayHaveCompleted || !cleanupConfirmed
			? ToolHostEffect.CleanupUnconfirmed
			: ToolHostEffect.Started;
		string message = exception is CheatEngineToolException { Error.Kind: ToolErrorKind.NotFound }
			? "Cheat Engine compiled the C# source, but its generated assembly was missing or expired before MCP could export it."
			: "Cheat Engine compiled the C# source, but MCP could not export its generated assembly.";
		string hint = outputPublicationMayHaveCompleted
			? "The destination publish may have completed; inspect outputPath before retrying the compilation."
			: cleanupConfirmed
				? "The exported destination was not published; inspect the output path and repeat the compilation if needed."
				: "The exported destination was not published, but MCP could not confirm removal of its protected temporary file; inspect the output path before retrying.";
		CheatEngineToolException partial = CheatEngineToolException.PartialEffect(message, effect,
			new ExecCSharpExportFailure(cleanupConfirmed, outputPublicationMayHaveCompleted),
			ExecCSharpJsonContext.Default.ExecCSharpExportFailure, hint: hint);
		return new CheatEngineToolException(partial.Error, exception);
	}

	private sealed class HeldFiles : IDisposable
	{
		private readonly List<HeldFile> _references;
		private readonly HeldFile? _coreAssembly;

		internal HeldFiles(List<HeldFile> references, HeldFile? coreAssembly)
		{
			_references = references;
			_coreAssembly = coreAssembly;
			ReferencePaths = references.Select(static reference => reference.FullPath).ToArray();
			CoreAssemblyPath = coreAssembly?.FullPath;
		}

		internal string[] ReferencePaths
		{
			get;
		}

		internal string? CoreAssemblyPath
		{
			get;
		}

		public void Dispose()
		{
			_coreAssembly?.Dispose();
			foreach (HeldFile reference in _references)
			{
				reference.Dispose();
			}
		}
	}
}
