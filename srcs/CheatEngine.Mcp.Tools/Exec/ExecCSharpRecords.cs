using System.ComponentModel;

namespace CheatEngine.Mcp.Tools.Exec;

/// <summary>The bounded outcome of Cheat Engine's C# compiler before its generated artifact is exported.</summary>
internal sealed record ExecCSharpCompilerOutput(string? AssemblyPath, string? Diagnostic = null,
	bool DiagnosticTruncated = false, bool CompilerAvailable = true);

/// <summary>A bounded compiler diagnostic reported by Cheat Engine without fabricated code or severity fields.</summary>
public sealed record ExecCSharpCompilerDiagnostic(
	[property: Description("The compiler's line-message text, bounded to 16384 UTF-8 bytes.")]
	string Text,
	[property: Description("Whether MCP truncated compiler diagnostic text at its 16384-byte bound.")]
	bool Truncated);

/// <summary>The completed approved-root export of a C# assembly compiled by Cheat Engine.</summary>
public sealed record ExecCSharpCompileResult(
	[property: Description("The anchored approved-root path where MCP copied the compiled assembly.")]
	string OutputPath,
	[property: Description("The exported assembly length in bytes.")]
	long Length,
	[property: Description("The uppercase SHA-256 hash of the exported assembly bytes.")]
	string Sha256);

/// <summary>Details for a compilation that completed in Cheat Engine but could not be exported through MCP.</summary>
public sealed record ExecCSharpExportFailure(
	[property: Description("Whether MCP removed its uncommitted protected temporary export file.")]
	bool TemporaryOutputCleanupConfirmed,
	[property: Description("Whether the atomic destination publish had begun, so the destination may already have changed.")]
	bool OutputPublicationMayHaveCompleted);
