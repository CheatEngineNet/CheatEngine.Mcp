using System.ComponentModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using CheatEngine.Client.Processes;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Files;
using CheatEngine.Mcp.Tools.Modules;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools.Investigation;

/// <summary>Stores append-only investigation notes in a user-selected directory.</summary>
[McpServerToolType]
public sealed class InvestigationTools
{
	internal const int MaximumFiles = 4096, MaximumFileBytes = 256 * 1024;
	internal const int MaximumDirectoryBytes = 16 * 1024 * 1024;
	private const string Schema = "cemcp.investigation-note", ExportSchema = "cemcp.investigation-export";
	private readonly McpFilePaths _files;
	private readonly ToolDispatch _dispatch;

	/// <summary>Creates the tools.</summary>
	public InvestigationTools(McpFilePaths files, ToolDispatch dispatch)
	{
		ArgumentNullException.ThrowIfNull(files);
		ArgumentNullException.ThrowIfNull(dispatch);
		_files = files;
		_dispatch = dispatch;
	}

	/// <summary>Creates immutable revision one.</summary>
	[McpServerTool(Name = CheatEngineToolNames.InvestigationCreateNote, Title = "Create investigation note", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = true, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description("Creates a durable named investigation note as immutable revision one in an allowed local directory. Optional module/RVA evidence is bound to the current PE build, never to an absolute address.")]
	public InvestigationNote CreateNote([Description("An existing allowed local note directory.")] string directory, [Description("A 1 to 64 character ASCII name.")] string name, [Description("A nonempty title up to 256 characters.")] string title, [Description("A body up to 16384 characters.")] string body, [Description("Optional tags.")] string[]? tags = null, [Description("Optional low, medium, or high confidence.")] string? confidence = null, [Description("Optional observations.")] string[]? observations = null, [Description("Optional loaded module; requires rva.")] string? module = null, [Description("Optional hexadecimal RVA; requires module.")] string? rva = null, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		string dir = WriteDirectory(directory, CheatEngineToolNames.InvestigationCreateNote);
		string key = Name(name);
		Validate(title, body, tags, confidence, observations);
		using DirectoryLease lease = DirectoryLease.Acquire(dir, cancellationToken);
		NoteSnapshot snapshot = Latest(dir, CheatEngineToolNames.InvestigationCreateNote, cancellationToken);
		return snapshot.Notes.ContainsKey(key)
			? throw CheatEngineToolException.InvalidState("The note already exists; update its latest revision instead.")
			: Publish(dir, new(key, 1, title, body, tags ?? [], confidence, observations ?? [], Capture(module, rva, CheatEngineToolNames.InvestigationCreateNote, cancellationToken)), snapshot, CheatEngineToolNames.InvestigationCreateNote, cancellationToken);
	}

	/// <summary>Searches latest revisions from one current read; later calls are not a shared snapshot.</summary>
	[McpServerTool(Name = CheatEngineToolNames.InvestigationSearchNotes, Title = "Search investigation notes", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description("Searches the latest durable note revisions by name, text or tag, with bounded ordinal paging. Optional binding validation applies only to the returned page and requires the matching loaded module build.")]
	public InvestigationNotePage SearchNotes([Description("An existing local note directory.")] string directory, [Description("Optional name substring up to 64 characters.")] string? name = null, [Description("Optional title or body substring up to 256 characters.")] string? text = null, [Description("Optional tag filter up to 64 characters.")] string? tag = null, [Description("Zero-based result offset.")] int offset = 0, [Description("Page size from 1 to 100.")] int limit = 50, [Description("Validate saved bindings against the current target.")] bool validateBinding = false, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		ValidateFilters(name, text, tag, offset, limit);
		string dir = ReadDirectory(directory, CheatEngineToolNames.InvestigationSearchNotes);
		InvestigationNote[] all;
		using (DirectoryLease lease = DirectoryLease.Acquire(dir, cancellationToken))
		{
			all = Filter(Latest(dir, CheatEngineToolNames.InvestigationSearchNotes, cancellationToken).Notes.Values, name, text, tag);
		}
		InvestigationNote[] page = [.. all.Skip(offset).Take(limit)];
		if (validateBinding)
		{
			ValidateBindings(page, CheatEngineToolNames.InvestigationSearchNotes, cancellationToken);
		}

		int next = offset + page.Length;
		return new(all.Length, next < all.Length ? next : null, next < all.Length, page);
	}

	/// <summary>Appends a successor only when the supplied revision remains latest.</summary>
	[McpServerTool(Name = CheatEngineToolNames.InvestigationUpdateNote, Title = "Update investigation note", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = true, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description("Appends a durable note revision only when expectedRevision is still current. A retained module binding must still match; explicitly replace or clear it when the investigation changes builds.")]
	public InvestigationNote UpdateNote([Description("An existing allowed local note directory.")] string directory, [Description("The note name.")] string name, [Description("The currently observed revision.")] int expectedRevision, [Description("A nonempty title up to 256 characters.")] string title, [Description("A body up to 16384 characters.")] string body, [Description("Optional tags.")] string[]? tags = null, [Description("Optional low, medium, or high confidence.")] string? confidence = null, [Description("Optional observations.")] string[]? observations = null, [Description("Replacement module; requires rva.")] string? module = null, [Description("Replacement RVA; requires module.")] string? rva = null, [Description("Clear the binding and do not supply module or rva.")] bool clearBinding = false, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (expectedRevision < 1)
		{
			throw CheatEngineToolException.InvalidArgument("expectedRevision", "must be at least 1.");
		}

		if (clearBinding && (module is not null || rva is not null))
		{
			throw CheatEngineToolException.InvalidArgument("clearBinding", "cannot be combined with module or rva.");
		}

		string dir = WriteDirectory(directory, CheatEngineToolNames.InvestigationUpdateNote);
		string key = Name(name);
		Validate(title, body, tags, confidence, observations);
		using DirectoryLease lease = DirectoryLease.Acquire(dir, cancellationToken);
		NoteSnapshot snapshot = Latest(dir, CheatEngineToolNames.InvestigationUpdateNote, cancellationToken);
		if (!snapshot.Notes.TryGetValue(key, out InvestigationNote? previous))
		{
			throw CheatEngineToolException.NotFound("The note does not exist.");
		}

		if (previous.Revision != expectedRevision)
		{
			throw CheatEngineToolException.InvalidState("The note revision changed; search notes and retry with its latest revision.");
		}

		if (previous.Revision == int.MaxValue)
		{
			throw CheatEngineToolException.InvalidState("The note revision cannot be incremented further.");
		}

		ModuleRvaBinding? binding = clearBinding ? null : module is null && rva is null ? previous.Binding : Capture(module, rva, CheatEngineToolNames.InvestigationUpdateNote, cancellationToken);
		if (previous.Binding is not null && module is null && rva is null && !clearBinding)
		{
			ValidateBindings([previous], CheatEngineToolNames.InvestigationUpdateNote, cancellationToken);
		}

		return Publish(dir, new(key, previous.Revision + 1, title, body, tags ?? [], confidence, observations ?? [], binding), snapshot, CheatEngineToolNames.InvestigationUpdateNote, cancellationToken);
	}

	/// <summary>Exports every matching latest note in a version-one envelope.</summary>
	[McpServerTool(Name = CheatEngineToolNames.InvestigationExportNotes, Title = "Export investigation notes", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description("Exports every matching latest note in a versioned JSON envelope, bounded to 16 MiB. Replacing an existing export requires overwrite. Canonical note revision names are always refused as destinations.")]
	public InvestigationExportResult ExportNotes([Description("An existing local note directory.")] string directory, [Description("A new allowed export file.")] string path, [Description("Optional name substring.")] string? name = null, [Description("Optional text substring.")] string? text = null, [Description("Optional tag filter.")] string? tag = null, [Description("Permit replacing an existing export.")] bool overwrite = false, [Description("Validate bindings against the current target.")] bool validateBinding = false, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		ValidateFilters(name, text, tag, 0, 100);
		string dir = ReadDirectory(directory, CheatEngineToolNames.InvestigationExportNotes);
		string output = _files.RequireWrite(path, CheatEngineToolNames.InvestigationExportNotes);
		RejectCanonicalDestination(output);
		InvestigationNote[] notes;
		using (DirectoryLease lease = DirectoryLease.Acquire(dir, cancellationToken))
		{
			notes = Filter(Latest(dir, CheatEngineToolNames.InvestigationExportNotes, cancellationToken).Notes.Values, name, text, tag);
		}
		if (validateBinding)
		{
			if (notes.Count(static note => note.Binding is not null) > 100)
			{
				throw CheatEngineToolException.LimitExceeded("validateBinding", "supports at most 100 bound notes; narrow the export filters.");
			}
			ValidateBindings(notes, CheatEngineToolNames.InvestigationExportNotes, cancellationToken);
		}

		byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new InvestigationExportFile(ExportSchema, 1, notes), InvestigationFileJsonContext.Default.InvestigationExportFile);
		if (bytes.Length > MaximumDirectoryBytes)
		{
			throw CheatEngineToolException.LimitExceeded("export", "is larger than 16 MiB.");
		}

		cancellationToken.ThrowIfCancellationRequested();
		using McpFileWrite write = _files.BeginWrite(output, CheatEngineToolNames.InvestigationExportNotes, overwrite);
		write.Stream.Write(bytes);
		cancellationToken.ThrowIfCancellationRequested();
		write.Commit();
		return new(Path.GetFileName(write.FullPath), notes.Length, "cemcp.investigation-export.v1");
	}

	private InvestigationNote Publish(string dir, InvestigationNote note, NoteSnapshot snapshot, string operation, CancellationToken token)
	{
		byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new InvestigationNoteFile(Schema, 1, note.Name, note.Revision, note.Title, note.Body, note.Tags, note.Confidence, note.Observations, note.Binding), InvestigationFileJsonContext.Default.InvestigationNoteFile);
		if (bytes.Length > MaximumFileBytes)
		{
			throw CheatEngineToolException.LimitExceeded("note", "is larger than 256 KiB.");
		}
		if (snapshot.RevisionCount >= MaximumFiles || bytes.Length > MaximumDirectoryBytes - snapshot.Bytes)
		{
			throw CheatEngineToolException.LimitExceeded("directory", "cannot exceed 4096 revisions or 16 MiB of revision content.");
		}

		token.ThrowIfCancellationRequested();
		using McpFileWrite write = _files.BeginWrite(Path.Combine(dir, FileName(note.Name, note.Revision)), operation, false);
		write.Stream.Write(bytes);
		token.ThrowIfCancellationRequested();
		write.Commit();
		return note;
	}

	private NoteSnapshot Latest(string dir, string operation, CancellationToken token)
	{
		Dictionary<string, InvestigationNote> latest = new(StringComparer.Ordinal);
		Dictionary<string, SortedSet<int>> all = new(StringComparer.Ordinal);
		int count = 0;
		int bytes = 0;
		foreach (string path in Directory.EnumerateFiles(dir, "cemcp-note-*.json"))
		{
			token.ThrowIfCancellationRequested();
			if (++count > MaximumFiles)
			{
				throw CheatEngineToolException.LimitExceeded("directory", "contains more than 4096 note revisions.");
			}

			(string fileName, string expectedName, int expectedRevision) = ParseFileName(path);
			using HeldFile held = _files.OpenRead(path, operation, Math.Min(MaximumFileBytes, MaximumDirectoryBytes - bytes));
			if (!held.IsComplete)
			{
				throw CheatEngineToolException.LimitExceeded("directory", "exceeds the 256 KiB revision or 16 MiB directory content limit.");
			}
			bytes += held.Content.Length;
			InvestigationNoteFile value = ParseStrict(held.Content);
			ValidateFile(value, fileName, expectedName, expectedRevision);
			InvestigationNote note = new(value.Name, value.Revision, value.Title, value.Body, value.Tags, value.Confidence, value.Observations, value.Binding);
			if (!all.TryGetValue(note.Name, out SortedSet<int>? revisions))
			{
				all.Add(note.Name, revisions = []);
			}

			if (!revisions.Add(note.Revision))
			{
				throw CheatEngineToolException.InvalidState("The note directory contains duplicate revisions.");
			}

			if (!latest.TryGetValue(note.Name, out InvestigationNote? old) || note.Revision > old.Revision)
			{
				latest[note.Name] = note;
			}
		}
		foreach (SortedSet<int> revisions in all.Values)
		{
			int next = 1;
			foreach (int revision in revisions)
			{
				if (revision != next++)
				{
					throw CheatEngineToolException.InvalidState("A note revision sequence has a gap.");
				}
			}
		}
		token.ThrowIfCancellationRequested();
		return new NoteSnapshot(latest, count, bytes);
	}

	private static InvestigationNoteFile ParseStrict(ReadOnlyMemory<byte> source)
	{
		try
		{
			using JsonDocument document = JsonDocument.Parse(source, new JsonDocumentOptions { MaxDepth = 8 });
			JsonElement root = document.RootElement;
			ValidateShape(root, ["schema", "version", "name", "revision", "title", "body", "tags", "observations"], ["confidence", "binding"]);
			if (root.GetProperty("tags").ValueKind != JsonValueKind.Array || root.GetProperty("observations").ValueKind != JsonValueKind.Array)
			{
				throw new JsonException();
			}
			if (root.TryGetProperty("binding", out JsonElement binding) && binding.ValueKind != JsonValueKind.Null)
			{
				ValidateShape(binding, ["identity", "rva"], []);
				ValidateShape(binding.GetProperty("identity"), ["name", "pointerBytes", "fingerprint", "imageSize"], []);
			}

			return JsonSerializer.Deserialize(root, InvestigationFileJsonContext.Default.InvestigationNoteFile) ?? throw new JsonException();
		}
		catch (Exception exception) when (exception is JsonException or NotSupportedException) { throw CheatEngineToolException.InvalidState("A note revision has malformed or unsupported JSON."); }
	}

	private static void ValidateShape(JsonElement value, string[] required, string[] optional)
	{
		if (value.ValueKind != JsonValueKind.Object)
		{
			throw new JsonException();
		}
		HashSet<string> seen = new(StringComparer.Ordinal);
		foreach (JsonProperty property in value.EnumerateObject())
		{
			if (!seen.Add(property.Name) || (!required.Contains(property.Name, StringComparer.Ordinal) && !optional.Contains(property.Name, StringComparer.Ordinal)))
			{
				throw new JsonException();
			}
		}
		if (!required.All(seen.Contains))
		{
			throw new JsonException();
		}
	}

	private static void ValidateFile(InvestigationNoteFile value, string fileName, string expectedName, int expectedRevision)
	{
		if (value.Schema != Schema || value.Version != 1 || value.Name != expectedName || value.Revision != expectedRevision || fileName != FileName(value.Name, value.Revision))
		{
			throw CheatEngineToolException.InvalidState("A note revision does not match its canonical file name or schema.");
		}

		try
		{
			Name(value.Name);
			Validate(value.Title, value.Body, value.Tags, value.Confidence, value.Observations);
			if (value.Binding is not null)
			{
				ModuleBuildBindings.ValidateBinding(value.Binding);
			}
		}
		catch (CheatEngineToolException exception) { throw CheatEngineToolException.InvalidState($"A note revision is invalid: {exception.Error.Message}"); }
	}

	private string WriteDirectory(string directory, string operation)
	{
		string probe = _files.RequireWrite(Path.Combine(directory ?? string.Empty, "cemcp-note-probe.json"), operation, "directory");
		string result = Path.GetDirectoryName(probe)!;
		return System.IO.Directory.Exists(result) ? result : throw CheatEngineToolException.InvalidArgument("directory", "must name an existing allowed local directory.");
	}
	private string ReadDirectory(string directory, string operation)
	{
		string probe = _files.RequireRead(Path.Combine(directory ?? string.Empty, "cemcp-note-probe.json"), operation, "directory");
		string result = Path.GetDirectoryName(probe)!;
		return System.IO.Directory.Exists(result) ? result : throw CheatEngineToolException.InvalidArgument("directory", "must name an existing local directory.");
	}
	private ModuleRvaBinding? Capture(string? module, string? rva, string operation, CancellationToken token)
	{
		return module is null && rva is null
			? null
			: string.IsNullOrWhiteSpace(module) || string.IsNullOrWhiteSpace(rva)
			? throw CheatEngineToolException.InvalidArgument("module", "and rva must be supplied together.")
			: _dispatch.Run(operation, current => ModuleBuildBindings.Capture(_dispatch.Client, module, rva, current), token);
	}
	private void ValidateBindings(IEnumerable<InvestigationNote> notes, string operation, CancellationToken token)
	{
		ModuleRvaBinding[] bindings = [.. notes.Where(static note => note.Binding is not null).Select(static note => note.Binding!)];
		if (bindings.Length == 0)
		{
			return;
		}
		_dispatch.Run(operation, current =>
		{
			ProcessSnapshot before = _dispatch.Client.Processes.GetCurrentProcess(current);
			foreach (ModuleRvaBinding binding in bindings)
			{
				ModuleRvaResolution resolved = ModuleBuildBindings.Validate(_dispatch.Client, binding, current);
				EnsureSameTarget(before, resolved.ProcessId, resolved.SelectionEpoch, operation);
			}
			ProcessSnapshot after = _dispatch.Client.Processes.GetCurrentProcess(current);
			EnsureSameTarget(before, after.Id.Value, after.SelectionEpoch, operation);
			return true;
		}, token);
	}

	private static void EnsureSameTarget(ProcessSnapshot before, int processId, long epoch, string operation)
	{
		if (before.Id.Value != processId || before.SelectionEpoch != epoch)
		{
			throw new CheatEngineToolException(new ToolError(ToolErrorKind.TargetChanged,
				"The selected target changed while the note bindings were validated.", operation, ToolHostEffect.Completed, false));
		}
	}
	private static InvestigationNote[] Filter(IEnumerable<InvestigationNote> notes, string? name, string? text, string? tag) => [.. notes.Where(note => (name is null || note.Name.Contains(name, StringComparison.OrdinalIgnoreCase)) && (text is null || note.Title.Contains(text, StringComparison.OrdinalIgnoreCase) || note.Body.Contains(text, StringComparison.OrdinalIgnoreCase)) && (tag is null || note.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))).OrderBy(note => note.Name, StringComparer.Ordinal)];
	private static void ValidateFilters(string? name, string? text, string? tag, int offset, int limit)
	{
		if (name is { Length: > 64 })
		{
			throw CheatEngineToolException.InvalidArgument("name", "must be at most 64 characters.");
		}

		if (text is { Length: > 256 })
		{
			throw CheatEngineToolException.InvalidArgument("text", "must be at most 256 characters.");
		}

		if (tag is { Length: > 64 })
		{
			throw CheatEngineToolException.InvalidArgument("tag", "must be at most 64 characters.");
		}

		if (offset < 0)
		{
			throw CheatEngineToolException.InvalidArgument("offset", "must be nonnegative.");
		}

		if (limit is < 1 or > 100)
		{
			throw CheatEngineToolException.InvalidArgument("limit", "must be 1 to 100.");
		}
	}
	private static string Name(string value)
	{
		return string.IsNullOrWhiteSpace(value) || value.Length > 64 || value.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '_' or '-'))
			? throw CheatEngineToolException.InvalidArgument("name", "must be 1 to 64 ASCII letters, digits, underscores or hyphens.")
			: value.ToLowerInvariant();
	}
	private static void Validate(string title, string body, string[]? tags, string? confidence, string[]? observations)
	{
		if (string.IsNullOrWhiteSpace(title) || title.Length > 256)
		{
			throw CheatEngineToolException.InvalidArgument("title", "must be nonempty and at most 256 characters.");
		}

		if (body is null || body.Length > 16384)
		{
			throw CheatEngineToolException.InvalidArgument("body", "must be at most 16384 characters.");
		}

		if (tags is { Length: > 16 } || tags?.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 64) == true)
		{
			throw CheatEngineToolException.InvalidArgument("tags", "must contain at most 16 nonempty 64-character values.");
		}

		if (observations is { Length: > 32 } || observations?.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 1024) == true)
		{
			throw CheatEngineToolException.InvalidArgument("observations", "must contain at most 32 nonempty 1024-character values.");
		}

		if (confidence is not null and not ("low" or "medium" or "high"))
		{
			throw CheatEngineToolException.InvalidArgument("confidence", "must be low, medium, high, or omitted.");
		}
	}
	private static (string FileName, string Name, int Revision) ParseFileName(string path)
	{
		string fileName = Path.GetFileName(path);
		const string prefix = "cemcp-note-", marker = ".r";
		int markerIndex = fileName.LastIndexOf(marker, StringComparison.Ordinal);
		if (!fileName.StartsWith(prefix, StringComparison.Ordinal) || markerIndex <= prefix.Length || !fileName.EndsWith(".json", StringComparison.Ordinal) || fileName.Length != markerIndex + marker.Length + 10 + 5 || !int.TryParse(fileName.AsSpan(markerIndex + marker.Length, 10), NumberStyles.None, CultureInfo.InvariantCulture, out int revision) || revision < 1)
		{
			throw CheatEngineToolException.InvalidState("A note file name is not canonical.");
		}

		string name = fileName[prefix.Length..markerIndex];
		return name.Length > 64 || name.Any(static c => c is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '-'))
			? throw CheatEngineToolException.InvalidState("A note file name is not canonical.")
			: ((string FileName, string Name, int Revision)) (fileName, name, revision);
	}
	private static string FileName(string name, int revision) => string.Create(CultureInfo.InvariantCulture, $"cemcp-note-{name}.r{revision:D10}.json");
	private static void RejectCanonicalDestination(string output)
	{
		if (Path.GetFileName(output).StartsWith("cemcp-note-", StringComparison.OrdinalIgnoreCase))
		{
			throw CheatEngineToolException.InvalidArgument("path", "cannot replace a canonical note revision.");
		}
	}

	private sealed record NoteSnapshot(Dictionary<string, InvestigationNote> Notes, int RevisionCount, int Bytes);

	private sealed class DirectoryLease : IDisposable
	{
		private readonly Mutex _mutex;
		private DirectoryLease(Mutex mutex) => _mutex = mutex;

		internal static DirectoryLease Acquire(string directory, CancellationToken token)
		{
			token.ThrowIfCancellationRequested();
			string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(directory.ToUpperInvariant())));
			Mutex mutex = new(false, $"Local\\CheatEngine.Mcp.Notes.{hash}");
			bool acquired = false;
			try
			{
				try
				{
					acquired = WaitHandle.WaitAny([mutex, token.WaitHandle], TimeSpan.FromSeconds(5)) == 0;
				}
				catch (AbandonedMutexException) { acquired = true; }
				token.ThrowIfCancellationRequested();
				return !acquired
					? throw CheatEngineToolException.InvalidState("The note directory is busy; retry after the current operation finishes.")
					: new DirectoryLease(mutex);
			}
			catch
			{
				if (acquired)
				{
					mutex.ReleaseMutex();
				}
				mutex.Dispose();
				throw;
			}
		}

		public void Dispose()
		{
			_mutex.ReleaseMutex();
			_mutex.Dispose();
		}
	}
}
