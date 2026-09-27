namespace CheatEngine.Mcp.Tests.LiveQualification.Infrastructure;

/// <summary>One <c>OutputDebugString</c> message.</summary>
/// <param name="ProcessId">The process that wrote it.</param>
/// <param name="Text">The decoded text, without its trailing line break.</param>
internal readonly record struct DebugOutputMessage(int ProcessId, string Text);
