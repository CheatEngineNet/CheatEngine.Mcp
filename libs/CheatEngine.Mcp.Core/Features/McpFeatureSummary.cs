namespace CheatEngine.Mcp.Core.Features;

/// <summary>The state of every exposure switch of one activation.</summary>
/// <param name="UnsafeLua">Whether <c>Mcp:EnableUnsafeLua</c> is on.</param>
/// <param name="AutoAssembler">Whether <c>Mcp:EnableAutoAssembler</c> is on.</param>
/// <param name="TargetCodeExecution">Whether <c>Mcp:EnableTargetCodeExecution</c> is on.</param>
/// <param name="KernelAccess">Whether <c>Mcp:EnableKernelAccess</c> is on.</param>
public sealed record McpFeatureSummary(bool UnsafeLua, bool AutoAssembler, bool TargetCodeExecution, bool KernelAccess);
