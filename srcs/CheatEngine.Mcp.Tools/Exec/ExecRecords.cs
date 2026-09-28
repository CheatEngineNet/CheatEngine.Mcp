using System.ComponentModel;
using System.Text.Json.Serialization;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Tools.Exec;

/// <summary>
///     One explicit numeric argument of a remote or instance-method call.
///     Pass a caller-owned string or buffer through a named <c>memory_allocate</c> allocation and use its address as an
///     integral argument, so the allocation stays alive until the target has definitely stopped using it.
/// </summary>
public sealed record ExecCallArgument(
	[property: Description(
		"integer (a 64-bit integer or pointer), float (32-bit) or double (64-bit); strings and buffers are " +
		"not accepted.")]
	ExecArgumentType Type,
	[property: Description(
		"The value as a JSON string, even for integer: \"10\", not 10. An integer is decimal, 0x-prefixed " +
		"hexadecimal, or bare hexadecimal when it contains A-F; float and double take finite invariant text such as " +
		"\"1.5\". At most 4096 characters.")]
	string Value);

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
public sealed record ExecLibraryInjection(
	[property: Description("The checked full path Cheat Engine injected.")]
	string LibraryPath,
	[property: Description("Whether Cheat Engine was told not to wait for its symbol reload after the injection.")]
	bool SkippedSymbolReloadWait);

/// <summary>Confirms a managed assembly injection and static method call that Cheat Engine reported successful.</summary>
public sealed record ExecDotNetInjection(
	[property: Description("The checked full path Cheat Engine loaded.")]
	string AssemblyPath,
	[property: Description("The type name that declares the called method.")]
	string ClassName,
	[property: Description("The called static method.")]
	string MethodName,
	[property: Description(
		"The method's signed int return value as decimal text, such as \"-1\"; Cheat Engine reads it back unsigned " +
		"and MCP restores the sign.")]
	string Result);

/// <summary>The copied return value of a target or local function call.</summary>
public sealed record ExecCallResult(
	[property: Description("The called function address, uppercase hexadecimal without 0x.")]
	string FunctionAddress,
	[property: Description(
		"The function's integer return register (EAX or RAX) as Cheat Engine reported it, as decimal text.")]
	string ReturnValue);

/// <summary>One symbol produced by Cheat Engine's C compiler.</summary>
public sealed record ExecCompiledSymbol(
	[property: Description("The C symbol name.")]
	string Name,
	[property: Description("The symbol's address, uppercase hexadecimal without 0x.")]
	string Address);

/// <summary>The bounded symbol list returned by a C compilation.</summary>
public sealed record ExecCompileResult(
	[property: Description("Up to 1024 compiled symbols, sorted by name.")]
	ExecCompiledSymbol[] Symbols,
	[property: Description("Whether the compiler produced more than 1024 symbols.")]
	bool SymbolsTruncated,
	[property: Description("Whether the code was compiled into Cheat Engine's own process.")]
	bool TargetSelf,
	[property: Description("Whether the code was allocated in kernel memory.")]
	bool KernelMode);
