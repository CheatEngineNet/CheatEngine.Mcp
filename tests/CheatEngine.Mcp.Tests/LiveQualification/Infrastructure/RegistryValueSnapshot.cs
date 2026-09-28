using Microsoft.Win32;

namespace CheatEngine.Mcp.Tests.LiveQualification.Infrastructure;

/// <summary>One registry value, with its data in the type the registry returned it.</summary>
/// <param name="Name">The value name (empty for the default value).</param>
/// <param name="Kind">The registry type.</param>
/// <param name="Data">
///     A <see cref="string" /> (String, ExpandString, unexpanded), a <see cref="string" /> array (MultiString), an
///     <see cref="int" /> (DWord), a <see cref="long" /> (QWord) or a <see cref="byte" /> array (Binary, None).
/// </param>
internal sealed record RegistryValueSnapshot(string Name, RegistryValueKind Kind, object Data);
