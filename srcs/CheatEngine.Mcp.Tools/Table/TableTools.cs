using System.ComponentModel;

using CheatEngine.Client.Extensions.DependencyInjection;
using CheatEngine.Client.Tables;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Core.Files;
using CheatEngine.Mcp.Core.Tables;

using Microsoft.Extensions.Options;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools.Table;

/// <summary>Inspected trusted-table loading, explicit saves and bounded file discovery for the <c>table_*</c> domain.</summary>
[McpServerToolType]
public sealed class TableTools
{
	internal const int MaximumListedFiles = 10000;
	internal const int MaximumPage = 1000;
	private readonly CheatEngineClientOptions _clientOptions;

	private readonly ToolDispatch _dispatch;
	private readonly McpFilePaths _files;

	/// <summary>Creates the table tools; options are read but neither files nor Cheat Engine are touched.</summary>
	public TableTools(ToolDispatch dispatch, McpFilePaths files, IOptions<CheatEngineClientOptions> clientOptions)
	{
		ArgumentNullException.ThrowIfNull(dispatch);
		ArgumentNullException.ThrowIfNull(files);
		ArgumentNullException.ThrowIfNull(clientOptions);
		_dispatch = dispatch;
		_files = files;
		_clientOptions = clientOptions.Value;
	}

	/// <summary>Inspects and loads a table while its source file remains pinned.</summary>
	[McpServerTool(Name = CheatEngineToolNames.TableLoad, Title = "Load cheat table", ReadOnly = false,
		Destructive = true, Idempotent = false, OpenWorld = true, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.MayPrompt)]
	[Description(
		"Inspect and load a .CT, .XML or .CETRAINER file through CheatEngine.Client. The source is pinned while inspected and loaded, and Client independently enforces CheatEngineClient:AllowedTableRoots. The inspection derives and enforces capability gates before Cheat Engine can execute Lua, Auto Assembler records or a Mono auto attach. A native load failure can leave a partial table or script effect.")]
	public TableLoadResult Load(
		[Description("The absolute local table path. CheatEngineClient:AllowedTableRoots must admit it.")]
		string path,
		[Description("Whether to merge into the existing address list rather than replace it.")]
		bool merge = false,
		CancellationToken cancellationToken = default)
	{
		string fullPath = RequireAllowedTablePath(path, CheatEngineToolNames.TableLoad);
		using HeldFile file =
			_files.OpenRead(fullPath, CheatEngineToolNames.TableLoad, CheatTableInspector.MaximumTableBytes);
		CheatTableInspection inspection = CheatTableInspector.Inspect(file);
		inspection.Requirements.Enforce(_dispatch.Features, CheatEngineToolNames.TableLoad);
		TableInspectionResult described = Describe(inspection);
		return _dispatch.Run(CheatEngineToolNames.TableLoad, token =>
		{
			MonoAutoAttachState state = MonoAutoAttachProbe.ReadInDispatch(_dispatch, CheatEngineToolNames.TableLoad,
				token);
			bool autoAttach = state.RequireForTableLoad(_dispatch.Features, CheatEngineToolNames.TableLoad,
				inspection.UsesMono);
			TrustedTableFile trusted = new(file.FullPath);
			_dispatch.Client.Tables.LoadTrustedTable(new TableLoadRequest(trusted, merge), token);
			return new TableLoadResult(trusted.FullPath, merge, described, autoAttach);
		}, cancellationToken);
	}

	/// <summary>Saves the current table to a configured Client table root.</summary>
	[McpServerTool(Name = CheatEngineToolNames.TableSave, Title = "Save cheat table", ReadOnly = false,
		Destructive = true, Idempotent = false, OpenWorld = true, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.MayPrompt)]
	[Description(
		"Save the current table through CheatEngine.Client to a .CT, .XML or .CETRAINER path under CheatEngineClient:AllowedTableRoots. MCP reserves an anchored temporary table in that root and publishes it atomically only after Cheat Engine saves it. overwrite must be true when a destination file already exists.")]
	public TableSaveResult Save(
		[Description("The absolute destination table path inside CheatEngineClient:AllowedTableRoots.")]
		string path,
		[Description("Whether an existing destination may be replaced.")]
		bool overwrite = false,
		CancellationToken cancellationToken = default)
	{
		(string fullPath, string root) = RequireAllowedTableDestination(path, CheatEngineToolNames.TableSave);
		using McpFileWrite write = BeginExternalWrite(fullPath, root, overwrite);
		return _dispatch.Run(CheatEngineToolNames.TableSave, token =>
		{
			string temporary = write.PrepareForExternalWrite();
			try
			{
				_dispatch.Client.Tables.SaveTable(new TableSaveRequest(new TrustedTableFile(temporary)), token);
			}
			catch (Exception exception)
			{
				if (!write.TryAbort())
				{
					throw ExternalSaveCleanupFailure(exception);
				}

				throw;
			}

			try
			{
				write.Commit();
			}
			catch (IOException exception)
			{
				throw PublishFailure(exception, write.TryAbort());
			}

			return new TableSaveResult(write.FullPath, write.DestinationExisted);
		}, cancellationToken);
	}

	/// <summary>Lists table files below the explicit CheatEngine.Client table roots.</summary>
	[McpServerTool(Name = CheatEngineToolNames.TableListFiles, Title = "List table files", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"List .CT, .XML and .CETRAINER files below the configured CheatEngineClient:AllowedTableRoots. The enumeration skips inaccessible and reparse-pointed entries, considers at most 10000 files, and returns absolute paths suitable for table_load. An empty roots list means Client will refuse every table load and save.")]
	public TableFilePage ListFiles(
		[Description("The zero-based first matching file.")]
		int offset = 0,
		[Description("The maximum returned files, 1 to 1000.")]
		int limit = 100)
	{
		if (offset < 0)
		{
			throw CheatEngineToolException.InvalidArgument("offset", "must be zero or greater.");
		}

		if (limit is < 1 or > MaximumPage)
		{
			throw CheatEngineToolException.InvalidArgument("limit", $"must be between 1 and {MaximumPage}.");
		}

		string[] roots = AllowedTableRoots(CheatEngineToolNames.TableListFiles);
		List<TableFileEntry> found = [];
		HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
		bool exact = true;
		foreach (string root in roots)
		{
			if (!CanEnumerateRoot(root))
			{
				exact = false;
				continue;
			}

			try
			{
				foreach (string path in Directory.EnumerateFiles(root, "*",
							 new EnumerationOptions
							 {
								 RecurseSubdirectories = true,
								 IgnoreInaccessible = false,
								 AttributesToSkip = FileAttributes.ReparsePoint
							 }))
				{
					if (!IsTablePath(path) || IsReparsePoint(path))
					{
						continue;
					}

					FileInfo file = new(path);
					file.Refresh();
					if (!file.Exists)
					{
						continue;
					}

					if (!seen.Add(file.FullName))
					{
						continue;
					}

					found.Add(new TableFileEntry(
						file.FullName,
						file.Length,
						new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero)));
					if (found.Count > MaximumListedFiles)
					{
						exact = false;
						break;
					}
				}
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
			{
				exact = false;
			}

			if (!exact && found.Count > MaximumListedFiles)
			{
				break;
			}
		}

		if (found.Count > MaximumListedFiles)
		{
			found.RemoveRange(MaximumListedFiles, found.Count - MaximumListedFiles);
		}

		TableFileEntry[] ordered = [.. found.OrderBy(static file => file.Path, StringComparer.OrdinalIgnoreCase)];
		int total = ordered.Length;
		TableFileEntry[] page = ordered.Skip(offset).Take(limit).ToArray();
		int? next = offset + page.Length < total ? offset + page.Length : null;
		return new TableFilePage(roots, total, exact, offset, page, next);
	}

	private static TableInspectionResult Describe(CheatTableInspection inspection)
	{
		return new TableInspectionResult(Format(inspection.Format), inspection.ContainsLua, inspection.ContainsForms,
			inspection.UsesMono, inspection.RecordCount, inspection.AssemblerScriptCount,
			[.. inspection.Requirements.ContractNames]);
	}

	private string RequireAllowedTablePath(string? path, string operation)
	{
		ValidateExtension(path, "path");
		string fullPath = _files.RequireRead(path, operation);
		if (!AllowedTableRoots(operation).Any(root => IsUnder(fullPath, root)))
		{
			throw CheatEngineToolException.InvalidArgument("path",
				"must be inside a CheatEngineClient:AllowedTableRoots directory.");
		}

		return fullPath;
	}

	private (string FullPath, string Root) RequireAllowedTableDestination(string? path, string operation)
	{
		string fullPath = RequireAllowedTablePath(path, operation);
		string? root = AllowedTableRoots(operation)
			.Where(root => IsUnder(fullPath, root))
			.OrderByDescending(static root => root.Length)
			.FirstOrDefault();
		return root is null
			? throw new InvalidOperationException("The admitted table path did not retain an allowed root.")
			: (fullPath, root);
	}

	private static CheatEngineToolException PublishFailure(IOException exception, bool cleanupConfirmed)
	{
		ToolHostEffect effect = cleanupConfirmed ? ToolHostEffect.Started : ToolHostEffect.CleanupUnconfirmed;
		string hint = cleanupConfirmed
			? "Cheat Engine saved a protected temporary table, but MCP did not replace the destination; inspect the table root before retrying."
			: "Cheat Engine saved a protected temporary table, but MCP could not confirm its cleanup; inspect the table root before retrying.";
		return new CheatEngineToolException(new ToolError(ToolErrorKind.PartialEffect,
			"Cheat Engine saved a protected temporary table, but it could not be atomically published to the requested destination.",
			CheatEngineToolNames.TableSave, effect, false, hint), exception);
	}

	private static CheatEngineToolException ExternalSaveCleanupFailure(Exception exception)
	{
		return new CheatEngineToolException(new ToolError(ToolErrorKind.PartialEffect,
			"Cheat Engine failed while saving a protected temporary table, and MCP could not confirm its cleanup.",
			CheatEngineToolNames.TableSave, ToolHostEffect.CleanupUnconfirmed, false,
			"Inspect the configured table root for MCP's protected temporary table before retrying; do not overwrite or delete unrelated files."),
			exception);
	}

	private McpFileWrite BeginExternalWrite(string path, string root, bool overwrite)
	{
		try
		{
			return _files.BeginWriteUnderRoot(path, root, CheatEngineToolNames.TableSave, overwrite,
				preserveDestinationExtension: true);
		}
		catch (IOException exception)
		{
			throw new CheatEngineToolException(new ToolError(ToolErrorKind.InvalidState,
				"The destination table could not be reserved.", CheatEngineToolNames.TableSave,
				ToolHostEffect.NotStarted, false,
				"Check that the table root is writable and the destination is not held by another program, then repeat the call."),
				exception);
		}
	}

	private string[] AllowedTableRoots(string operation)
	{
		return
		[
			.. _clientOptions.AllowedTableRoots
				.Select(root => _files.RequireRead(root, operation, "allowedTableRoots"))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.Order(StringComparer.OrdinalIgnoreCase)
		];
	}

	private static bool IsUnder(string path, string root)
	{
		string fullRoot = Path.GetFullPath(root);
		string prefix = Path.EndsInDirectorySeparator(fullRoot)
			? fullRoot
			: fullRoot + Path.DirectorySeparatorChar;
		return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
	}

	private static string Format(CheatTableFormat format)
	{
		return format switch
		{
			CheatTableFormat.Xml => "xml",
			CheatTableFormat.Trainer => "trainer",
			CheatTableFormat.LegacyBinary => "legacy_binary",
			CheatTableFormat.Protected => "protected",
			CheatTableFormat.Obfuscated => "obfuscated",
			CheatTableFormat.Unparseable => "unparseable",
			CheatTableFormat.TooLarge => "too_large",
			_ => "unknown"
		};
	}

	private static void ValidateExtension(string? path, string parameter)
	{
		if (!IsTablePath(path))
		{
			throw CheatEngineToolException.InvalidArgument(parameter,
				"must name a .CT, .XML or .CETRAINER table.");
		}
	}

	private static bool IsTablePath(string? path)
	{
		try
		{
			string extension = path is null ? string.Empty : Path.GetExtension(path);
			return extension.Equals(".ct", StringComparison.OrdinalIgnoreCase) ||
				   extension.Equals(".xml", StringComparison.OrdinalIgnoreCase) ||
				   extension.Equals(".cetrainer", StringComparison.OrdinalIgnoreCase);
		}
		catch (Exception exception) when (exception is ArgumentException or NotSupportedException
											  or PathTooLongException)
		{
			return false;
		}
	}

	private static bool CanEnumerateRoot(string root)
	{
		try
		{
			DirectoryInfo directory = new(root);
			directory.Refresh();
			return directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) == 0 &&
				   directory.LinkTarget is null;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
											  or NotSupportedException)
		{
			return false;
		}
	}

	private static bool IsReparsePoint(string path)
	{
		try
		{
			FileInfo file = new(path);
			file.Refresh();
			return (file.Attributes & FileAttributes.ReparsePoint) != 0 || file.LinkTarget is not null;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
											  or NotSupportedException)
		{
			return true;
		}
	}
}
