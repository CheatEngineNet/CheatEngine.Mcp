namespace CheatEngine.Mcp.Tools.Code;

internal sealed record CodeLuaByteDisassembly(string Origin, string Text);

internal sealed record CodeLuaDisassemblyColumns(string AddressText, string Opcode, string Extra);

internal sealed record CodeLuaFunction(
	bool Found,
	string Address,
	string? StartAddress = null,
	string? EndAddress = null,
	long? Size = null,
	bool AddressIsJumpDestination = false);

internal sealed record CodeLuaReference(string FromAddress, string ToAddress, string Kind);

internal sealed record CodeLuaReferencePage(
	string Address,
	int Total,
	bool Exact,
	CodeLuaReference[] References,
	int? NextOffset = null);

internal sealed record CodeLuaString(string Address, string Text);

internal sealed record CodeLuaStringPage(int Total, bool Exact, CodeLuaString[] Strings, int? NextOffset = null);

internal sealed record CodeLuaFunctionPage(int Total, string[] Functions, int? NextOffset = null);

internal sealed record CodeLuaComment(string Address, string? Comment);

internal sealed record CodeLuaComments(CodeLuaComment[] Comments);

internal sealed record CodeLuaCleared(bool Cleared);

internal sealed record CodeLuaDissected(string Address, int Size);
