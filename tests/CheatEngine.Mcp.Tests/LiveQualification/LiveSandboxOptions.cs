namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Fixed, test-owned activation settings selected before a private CE copy starts.</summary>
internal sealed record LiveSandboxOptions(bool CompilerQualification, bool EnableTargetCodeExecution)
{
	internal static LiveSandboxOptions Smoke { get; } = new(false, false);
}

/// <summary>Per-host compiler files, all rooted below one live run.</summary>
internal sealed record LiveCompilerRoots(string Temp, string References, string Output);
