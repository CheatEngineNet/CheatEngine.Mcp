using System.Text.Json.Serialization;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Tools.Exec;

/// <summary>
///     One explicit numeric argument of a remote or instance-method call.
///     Pass a caller-owned string or buffer through a named <c>memory_allocate</c> allocation and use its address as an
///     integral argument, so the allocation stays alive until the target has definitely stopped using it.
/// </summary>
public sealed record ExecCallArgument(ExecArgumentType Type, string Value);

/// <summary>The calling convention understood by Cheat Engine's <c>executeCodeEx</c> APIs.</summary>
[JsonConverter(typeof(ContractEnumConverter<ExecCallingConvention>))]
public enum ExecCallingConvention
{
	/// <summary>Windows stdcall.</summary>
	Stdcall,

	/// <summary>Cdecl, where the caller cleans the stack.</summary>
	Cdecl
}

/// <summary>The numeric argument types that remain safe when Cheat Engine times out a remote call.</summary>
[JsonConverter(typeof(ContractEnumConverter<ExecArgumentType>))]
public enum ExecArgumentType
{
	/// <summary>A signed integer or a pointer-sized integer.</summary>
	[JsonStringEnumMemberName("integer")] Integral,

	/// <summary>A 32-bit IEEE floating-point number.</summary>
	[JsonStringEnumMemberName("float")] SinglePrecision,

	/// <summary>A 64-bit IEEE floating-point number.</summary>
	[JsonStringEnumMemberName("double")] DoublePrecision
}

/// <summary>Confirms a native library injection request that Cheat Engine reported successful.</summary>
public sealed record ExecLibraryInjection(string LibraryPath, bool SkippedSymbolReloadWait);

/// <summary>Confirms a managed assembly injection and static method call that Cheat Engine reported successful.</summary>
public sealed record ExecDotNetInjection(string AssemblyPath, string ClassName, string MethodName, string Result);

/// <summary>The copied return value of a target or local function call.</summary>
public sealed record ExecCallResult(string FunctionAddress, string ReturnValue);

/// <summary>One symbol produced by Cheat Engine's C compiler.</summary>
public sealed record ExecCompiledSymbol(string Name, string Address);

/// <summary>The bounded symbol list returned by a C compilation.</summary>
public sealed record ExecCompileResult(
	ExecCompiledSymbol[] Symbols,
	bool SymbolsTruncated,
	bool TargetSelf,
	bool KernelMode);
