namespace CheatEngine.Mcp.Core.Composition;

/// <summary>The values an <see cref="IMcpCompletionSource" /> listed for one variable.</summary>
/// <param name="Values">Every value, unfiltered; duplicates and empty values are dropped by the handler.</param>
/// <param name="SelectionEpoch">
///     The target-selection epoch of a listing that depends on the attached process, such as its module names;
///     <see langword="null" /> for a listing that does not, such as Cheat Engine's global structures. Values of an
///     older epoch are never offered once a newer epoch was observed.
/// </param>
public sealed record McpCompletionValues(IReadOnlyList<string> Values, long? SelectionEpoch = null);
