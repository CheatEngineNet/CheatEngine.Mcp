using System.Collections.Frozen;

namespace CheatEngine.Mcp.Core.Files;

/// <summary>
///     The textual rules every host path must pass before it is normalized, shared by <see cref="McpFilePaths" /> and the
///     <see cref="McpFileOptions" /> validator. They look at the text only; link and drive checks need the file system.
/// </summary>
internal static class McpPathRules
{
	/// <summary>The longest path accepted, the Windows extended-length limit.</summary>
	internal const int MaximumLength = 32767;

	internal const string FormHint =
		"Pass an absolute local path with a drive letter, such as C:\\CheatEngine\\dump.bin.";

	// Win32 device names, reserved in every folder and with any extension (CON.txt still opens the console).
	private static readonly FrozenSet<string> DeviceNames = new[]
	{
		"CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$", "CLOCK$", "COM0", "COM1", "COM2", "COM3", "COM4", "COM5",
		"COM6", "COM7", "COM8", "COM9", "COM\u00B9", "COM\u00B2", "COM\u00B3", "LPT0", "LPT1", "LPT2", "LPT3",
		"LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT\u00B9", "LPT\u00B2", "LPT\u00B3"
	}.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	///     Finds why a path is not an absolute, local, drive-letter path: a UNC or device prefix (<c>\\server</c>,
	///     <c>\\?\</c>, <c>\\.\</c>), a relative or drive-relative form, an alternate data stream, a DOS device name, a
	///     wildcard or control character, or a segment that Windows would silently rename by trimming a trailing space or
	///     period.
	/// </summary>
	/// <param name="path">The path as given, or a normalized one.</param>
	/// <returns>A caller-safe reason that never repeats the path, or <see langword="null" /> when the form is accepted.</returns>
	internal static string? FindFormProblem(string? path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return "is required.";
		}

		if (path.Length > MaximumLength)
		{
			return $"is longer than {MaximumLength} characters.";
		}

		foreach (char character in path)
		{
			if (character is < ' ' or '"' or '*' or '<' or '>' or '?' or '|')
			{
				return "contains a wildcard, control or reserved character.";
			}
		}

		if (IsSeparator(path[0]) && path.Length > 1 && IsSeparator(path[1]))
		{
			return "must not be a UNC or device path (\\\\server\\share, \\\\?\\ or \\\\.\\).";
		}

		if (!Path.IsPathFullyQualified(path) || path is not [_, ':', '\\' or '/', ..] ||
		    !char.IsAsciiLetter(path[0]))
		{
			return "must be an absolute path with a drive letter.";
		}

		if (path.IndexOf(':', 2) >= 0)
		{
			return "must not name an alternate data stream.";
		}

		foreach (Range range in path.AsSpan(3).SplitAny('\\', '/'))
		{
			ReadOnlySpan<char> segment = path.AsSpan(3)[range];
			if (segment.IsEmpty || segment is "." or "..")
			{
				continue;
			}

			if (segment[^1] is ' ' or '.')
			{
				return "must not have a segment that ends with a space or a period.";
			}

			if (IsDeviceName(segment))
			{
				return "must not name a Windows device such as CON, NUL, COM1 or LPT1.";
			}
		}

		return null;
	}

	/// <summary>Whether a path segment names a DOS device, with or without an extension.</summary>
	/// <param name="segment">One segment, without separators.</param>
	/// <returns><see langword="true" /> for names such as <c>CON</c>, <c>nul.txt</c> or <c>COM1 .log</c>.</returns>
	internal static bool IsDeviceName(ReadOnlySpan<char> segment)
	{
		int dot = segment.IndexOf('.');
		ReadOnlySpan<char> name = (dot < 0 ? segment : segment[..dot]).TrimEnd(' ');
		FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>> lookup =
			DeviceNames.GetAlternateLookup<ReadOnlySpan<char>>();
		return lookup.Contains(name);
	}

	/// <summary>Adds a trailing separator so that a prefix test cannot match a sibling such as <c>C:\root2</c>.</summary>
	/// <param name="fullPath">A normalized full path.</param>
	/// <returns>The path, ending with exactly one <see cref="Path.DirectorySeparatorChar" />.</returns>
	internal static string WithSeparator(string fullPath)
	{
		return Path.EndsInDirectorySeparator(fullPath)
			? fullPath
			: fullPath + Path.DirectorySeparatorChar;
	}

	/// <summary>Whether a full path is a directory or lies anywhere under it, ignoring case.</summary>
	/// <param name="fullPath">A normalized full path.</param>
	/// <param name="directory">A normalized directory ending with a separator.</param>
	/// <returns><see langword="true" /> when <paramref name="fullPath" /> is <paramref name="directory" /> or inside it.</returns>
	internal static bool IsSameOrUnder(string fullPath, string directory)
	{
		return WithSeparator(fullPath).StartsWith(directory, StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsSeparator(char character)
	{
		return character is '\\' or '/';
	}
}
