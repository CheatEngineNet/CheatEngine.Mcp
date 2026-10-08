namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Closed-world compiler qualification variants; no value accepts a caller-selected path or Lua body.</summary>
internal enum LiveCompilerVariant
{
	Normal,
	PrivateCompilerAbsent,
	HeldExportAfterB
}

/// <summary>Temporary-root topology selected only by the reviewed compiler scenario.</summary>
internal enum CompilerTempTopology
{
	Isolated,
	Shared
}
