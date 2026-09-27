using System.Collections.Immutable;
using System.Text.Json;

using CheatEngine.Mcp.Core.Contract;

using Microsoft.Win32.SafeHandles;

namespace CheatEngine.Mcp.Core.Files;

/// <summary>
///     The activation's host-file policy: every tool that reads or writes a file on the Cheat Engine host passes its path
///     here first, before any Client call, and uses only the normalized path it returns.
/// </summary>
/// <remarks>
///     <para>
///         A path must be absolute, on a local fixed drive and in plain drive-letter form: UNC and device paths
///         (<c>\\server\share</c>, <c>\\?\</c>, <c>\\.\</c>), alternate data streams, DOS device names, wildcards and
///         segments ending with a space or a period are refused. Every existing ancestor, and the path itself, must be a
///         plain file or directory: a symbolic link, junction or any other reparse point is refused. Normalization
///         (<see cref="Path.GetFullPath(string)" />) expands the short (8.3) alias of an existing name, and an existing
///         segment that still names an alias is refused, so a protected directory cannot be reached under a second name.
///         The instance registry and the MCP data directory (settings and logs), including the targets of links on their
///         own paths, are always refused.
///     </para>
///     <para>
///         Writes additionally need a root under <see cref="McpFileOptions.AllowedRoots" />; the comparison appends a
///         separator and ignores case, so <c>C:\root2</c> is not under <c>C:\root</c>. Reads need no root.
///     </para>
///     <para>
///         A check cannot pin a path by itself: another process could swap a directory for a junction afterwards. A tool
///         that reads a file therefore opens it with <see cref="OpenRead" />, which checks again once the handle is held
///         and keeps writers and deleters out until it is disposed.
///     </para>
/// </remarks>
public sealed class McpFilePaths
{
	private const string DeniedHint =
		"The MCP data directory and the instance registry are never accessible through Cheat Engine tools; choose another folder.";

	private readonly ImmutableArray<string> _denied;
	private readonly ImmutableArray<string> _roots;

	/// <summary>Creates the policy of one activation.</summary>
	/// <param name="options">The <c>Mcp:Files</c> settings; they are validated again here.</param>
	/// <param name="registryDirectory">The absolute instance registry directory, whose files hold backend tokens.</param>
	/// <param name="dataDirectory">The absolute MCP data directory, which holds the user settings and logs.</param>
	/// <exception cref="Microsoft.Extensions.Options.OptionsValidationException">A root is invalid.</exception>
	/// <exception cref="ArgumentException">A directory is not fully qualified.</exception>
	public McpFilePaths(McpFileOptions options, string registryDirectory, string dataDirectory)
	{
		ArgumentNullException.ThrowIfNull(options);
		new McpFileOptionsValidator().ThrowIfInvalid(options);
		RequireFullyQualified(registryDirectory, nameof(registryDirectory));
		RequireFullyQualified(dataDirectory, nameof(dataDirectory));
		ImmutableArray<string>.Builder denied = ImmutableArray.CreateBuilder<string>(4);
		foreach (string directory in (ReadOnlySpan<string>) [registryDirectory, dataDirectory])
		{
			string full = McpPathRules.WithSeparator(Path.GetFullPath(directory));
			denied.Add(full);
			// A link on the directory's own path gives it a second name; refuse that one too.
			string resolved = McpPathRules.WithSeparator(ResolveLinks(full));
			if (!string.Equals(resolved, full, StringComparison.OrdinalIgnoreCase))
			{
				denied.Add(resolved);
			}
		}

		_denied = denied.ToImmutable();
		_roots = [.. options.AllowedRoots.Select(static root => McpPathRules.WithSeparator(Path.GetFullPath(root)))];
		AllowedRoots = [.. _roots.Select(static root => Path.GetPathRoot(root) == root ? root : root[..^1])];
	}

	/// <summary>The normalized write roots, without their trailing separator (a drive root keeps it); empty refuses writes.</summary>
	public ImmutableArray<string> AllowedRoots
	{
		get;
	}

	/// <summary>Checks a path the tool will read, such as a file to open as a process or a module file.</summary>
	/// <param name="path">The caller's path.</param>
	/// <param name="operation">The tool or operation, reported in the error.</param>
	/// <param name="parameter">The tool parameter that carried the path, named in the error.</param>
	/// <returns>The normalized full path to use instead of <paramref name="path" />.</returns>
	/// <exception cref="CheatEngineToolException">
	///     <c>invalid_argument</c> with <c>not_started</c> when a rule refuses the
	///     path.
	/// </exception>
	public string RequireRead(string? path, string operation, string parameter = "path")
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(operation);
		ArgumentException.ThrowIfNullOrWhiteSpace(parameter);
		return Check(path, operation, parameter);
	}

	/// <summary>
	///     Checks a file the tool will create or overwrite: the read rules, plus a root under
	///     <see cref="McpFileOptions.AllowedRoots" />, and the path must not name a root or an existing directory.
	/// </summary>
	/// <param name="path">The caller's path.</param>
	/// <param name="operation">The tool or operation, reported in the error.</param>
	/// <param name="parameter">The tool parameter that carried the path, named in the error.</param>
	/// <returns>The normalized full path to use instead of <paramref name="path" />.</returns>
	/// <exception cref="CheatEngineToolException">
	///     <c>invalid_argument</c> with <c>not_started</c> when a rule refuses the
	///     path.
	/// </exception>
	public string RequireWrite(string? path, string operation, string parameter = "path")
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(operation);
		ArgumentException.ThrowIfNullOrWhiteSpace(parameter);
		if (_roots.IsEmpty)
		{
			throw Refuse(operation, parameter, "cannot be written: no write root is configured.",
				$"Add a folder to {McpFileOptions.SectionName}:AllowedRoots in appsettings.json, then disable and re-enable the plugin.");
		}

		string full = Check(path, operation, parameter);
		string? root = _roots.FirstOrDefault(root => full.StartsWith(root, StringComparison.OrdinalIgnoreCase));
		if (root is null || McpPathRules.WithSeparator(full).Length == root.Length)
		{
			throw Refuse(operation, parameter, "is not inside a write root.",
				$"Write inside one of the folders listed in {McpFileOptions.SectionName}:AllowedRoots.");
		}

		if (Directory.Exists(full))
		{
			throw Refuse(operation, parameter, "names a directory, not a file.", null);
		}

		return full;
	}

	/// <summary>
	///     Checks a path like <see cref="RequireRead" />, opens the file for reading while denying writers and deleters,
	///     checks the path again now that the file is pinned, and reads it when it fits <paramref name="maximumBytes" />.
	/// </summary>
	/// <remarks>
	///     Keep the returned file until the Cheat Engine work that uses the path is over: while it is held, no process
	///     can change, replace, rename or delete the file, so what was inspected is what Cheat Engine opens by name.
	///     Readers that share reading, such as Cheat Engine's table loader, still open it.
	/// </remarks>
	/// <param name="path">The caller's path.</param>
	/// <param name="operation">The tool or operation, reported in the error.</param>
	/// <param name="maximumBytes">The largest content read into memory; a longer file is held but not read.</param>
	/// <param name="parameter">The tool parameter that carried the path, named in the error.</param>
	/// <returns>The held file; dispose it to release the handle.</returns>
	/// <exception cref="CheatEngineToolException">
	///     <c>invalid_argument</c> when a rule refuses the path or it names a directory, <c>not_found</c> when the file does
	///     not exist, <c>busy</c> when another process holds it open for writing, <c>invalid_state</c> when access is
	///     denied; all with <c>not_started</c>.
	/// </exception>
	public HeldFile OpenRead(string? path, string operation, int maximumBytes, string parameter = "path")
	{
		ArgumentOutOfRangeException.ThrowIfNegative(maximumBytes);
		string full = RequireRead(path, operation, parameter);
		if (Directory.Exists(full))
		{
			throw Refuse(operation, parameter, "names a directory, not a file.", null);
		}

		SafeFileHandle handle = OpenHandle(full, operation);
		try
		{
			// The handle now pins the file and its folders; a link swapped in before it was opened is caught here.
			Check(full, operation, parameter);
			long length = RandomAccess.GetLength(handle);
			if (length > maximumBytes)
			{
				return new HeldFile(full, handle, length, ReadOnlyMemory<byte>.Empty, false);
			}

			byte[] content = new byte[length];
			int read = 0;
			while (read < content.Length)
			{
				int count = RandomAccess.Read(handle, content.AsSpan(read), read);
				if (count == 0)
				{
					break;
				}

				read += count;
			}

			return new HeldFile(full, handle, length, content.AsMemory(0, read), read == length);
		}
		catch
		{
			handle.Dispose();
			throw;
		}
	}

	private string Check(string? path, string operation, string parameter)
	{
		string? problem = McpPathRules.FindFormProblem(path);
		if (problem is not null)
		{
			throw Refuse(operation, parameter, problem, McpPathRules.FormHint);
		}

		string full;
		try
		{
			full = Path.GetFullPath(path!);
		}
		catch (Exception exception) when (exception is ArgumentException or NotSupportedException or
			                                  PathTooLongException)
		{
			throw Refuse(operation, parameter, "is not a valid path.", McpPathRules.FormHint);
		}

		problem = McpPathRules.FindFormProblem(full);
		if (problem is not null)
		{
			throw Refuse(operation, parameter, problem, McpPathRules.FormHint);
		}

		if (!IsFixedDrive(full))
		{
			throw Refuse(operation, parameter, "must be on a local fixed drive.",
				"Copy the file to a local disk first; network, removable and virtual drives are not accepted.");
		}

		if (_denied.Any(directory => McpPathRules.IsSameOrUnder(full, directory)))
		{
			throw Refuse(operation, parameter, "is inside a protected MCP directory.", DeniedHint);
		}

		string? linkProblem = FindLinkProblem(full);
		if (linkProblem is not null)
		{
			throw Refuse(operation, parameter, linkProblem,
				"Use the real location of the file, without symbolic links, junctions or short names.");
		}

		return full;
	}

	/// <summary>Walks the existing part of a normalized path from its drive root, refusing links and short names.</summary>
	private static string? FindLinkProblem(string full)
	{
		string root = Path.GetPathRoot(full)!;
		string current = root;
		foreach (string segment in full[root.Length..].Split(Path.DirectorySeparatorChar,
			         StringSplitOptions.RemoveEmptyEntries))
		{
			string parent = current;
			current = Path.Join(current, segment);
			FileAttributes attributes;
			try
			{
				attributes = File.GetAttributes(current);
			}
			catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
			{
				// Nothing below a missing segment exists yet, so nothing below it can be a link.
				return null;
			}
			catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
			{
				return "could not be verified: a folder on the path cannot be inspected.";
			}

			if ((attributes & FileAttributes.ReparsePoint) != 0)
			{
				return "passes through a symbolic link, junction or other reparse point.";
			}

			if (segment.Contains('~', StringComparison.Ordinal) && !HasLongName(parent, segment))
			{
				return "uses a short (8.3) name instead of the long name.";
			}
		}

		return null;
	}

	/// <summary>
	///     Whether a folder holds an entry whose long name is <paramref name="segment" />. The whole folder is listed and
	///     compared by long name, so the result never depends on how a search pattern treats short names: an existing
	///     segment that is not listed under its own name was resolved through its short (8.3) alias. Normalization already
	///     expands such aliases; this is the second line of defence, for a segment it could not expand.
	/// </summary>
	private static bool HasLongName(string parent, string segment)
	{
		EnumerationOptions options = new()
		{
			AttributesToSkip = 0, IgnoreInaccessible = true, RecurseSubdirectories = false
		};
		try
		{
			return Directory.EnumerateFileSystemEntries(parent, "*", options).Any(entry =>
				Path.GetFileName(entry.AsSpan()).Equals(segment, StringComparison.OrdinalIgnoreCase));
		}
		catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
		{
			return false;
		}
	}

	/// <summary>Replaces each linked prefix of an existing path with its final target, so a denied folder keeps one name.</summary>
	private static string ResolveLinks(string full)
	{
		string resolved = Path.GetPathRoot(full)!;
		foreach (string segment in full[resolved.Length..].Split(Path.DirectorySeparatorChar,
			         StringSplitOptions.RemoveEmptyEntries))
		{
			resolved = Path.Join(resolved, segment);
			try
			{
				DirectoryInfo directory = new(resolved);
				if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0 &&
				    directory.ResolveLinkTarget(true) is { } target)
				{
					resolved = Path.GetFullPath(target.FullName);
				}
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
			{
				// An unresolvable link keeps its own name, which stays denied.
			}
		}

		return resolved;
	}

	private static bool IsFixedDrive(string full)
	{
		try
		{
			return new DriveInfo(full[..3]).DriveType == DriveType.Fixed;
		}
		catch (ArgumentException)
		{
			return false;
		}
	}

	private static SafeFileHandle OpenHandle(string full, string operation)
	{
		try
		{
			return File.OpenHandle(full);
		}
		catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
		{
			throw Fail(ToolErrorKind.NotFound, "The file does not exist.", operation, null, exception);
		}
		catch (UnauthorizedAccessException exception)
		{
			throw Fail(ToolErrorKind.InvalidState, "Access to the file is denied.", operation,
				"Check the file's permissions for the Cheat Engine process.", exception);
		}
		catch (IOException exception)
		{
			throw Fail(ToolErrorKind.Busy, "Another process has the file open for writing.", operation,
				"Close the program that writes the file, then repeat the call.", exception);
		}
	}

	private static CheatEngineToolException Refuse(string operation, string parameter, string problem, string? hint)
	{
		JsonElement details = JsonSerializer.SerializeToElement(new ToolParameterDetails(parameter),
			CoreJsonContext.Default.ToolParameterDetails);
		return new CheatEngineToolException(new ToolError(ToolErrorKind.InvalidArgument, $"{parameter} {problem}",
			operation, ToolHostEffect.NotStarted, false, hint, details));
	}

	private static CheatEngineToolException Fail(ToolErrorKind kind, string message, string operation, string? hint,
		Exception exception)
	{
		return new CheatEngineToolException(new ToolError(kind, message, operation, ToolHostEffect.NotStarted,
			ToolFailureMapping.IsRetryable(kind, ToolHostEffect.NotStarted), hint), exception);
	}

	private static void RequireFullyQualified(string directory, string parameterName)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(directory, parameterName);
		if (!Path.IsPathFullyQualified(directory))
		{
			throw new ArgumentException("The directory must be fully qualified.", parameterName);
		}
	}
}
