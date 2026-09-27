namespace CheatEngine.Mcp.Tests.LiveQualification.Infrastructure;

/// <summary>The PE facts the verification needs about an executable.</summary>
/// <param name="Machine">The COFF machine, for example <c>Amd64</c>.</param>
/// <param name="FileVersion">The file version resource, or <see langword="null" />.</param>
internal readonly record struct ExecutableFacts(string Machine, string? FileVersion);
