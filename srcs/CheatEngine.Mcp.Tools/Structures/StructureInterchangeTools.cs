using System.ComponentModel;

using CheatEngine.Client.Processes;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Files;
using CheatEngine.Mcp.Core.Values;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools.Structures;

/// <summary>Exports and imports bounded, lossless representable Structure Dissect layouts.</summary>
[McpServerToolType]
public sealed class StructureInterchangeTools
{
	private readonly ToolDispatch _dispatch;
	private readonly McpFilePaths _files;
	public StructureInterchangeTools(ToolDispatch dispatch, McpFilePaths files)
	{
		_dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
		_files = files ?? throw new ArgumentNullException(nameof(files));
	}

	[McpServerTool(Name = CheatEngineToolNames.StructureExport, Title = "Export structures", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description("Exports selected Structure Dissect layouts and their pointer-child closure as strict version-1 JSON. Fields that version 1 cannot restore, including embedded, binary and custom fields, are refused rather than omitted.")]
	public StructureInterchangeResult Export([Description("One to 128 root structure names.")] string[] names, [Description("Absolute local .json destination below an allowed write root.")] string path, [Description("Replace an existing destination atomically.")] bool overwrite = false, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		string[] roots = StructureArguments.Names(names, "names", StructureInterchangeCodec.MaximumLayouts);
		string full = CheckExtension(_files.RequireWrite(path, CheatEngineToolNames.StructureExport), ".json");
		(StructureInterchangeLuaExport Copy, int Width) = _dispatch.Run(
			CheatEngineToolNames.StructureExport,
			token =>
			{
				ProcessSnapshot process = _dispatch.Client.Processes.GetCurrentProcess(token);
				int width = PointerWidth(process);
				StructureInterchangeLuaExport copy = _dispatch.ExecuteLua(CheatEngineToolNames.StructureExport,
					StructureInterchangeLua.Export, StructureInterchangeJsonContext.Default.StructureInterchangeLuaExport,
					token, roots, StructureInterchangeCodec.MaximumLayouts, StructureInterchangeCodec.MaximumFields);
				EnsureSameTarget(process, _dispatch.Client.Processes.GetCurrentProcess(token),
					CheatEngineToolNames.StructureExport, ToolHostEffect.Completed);
				return (copy, width);
			}, cancellationToken);
		StructureInterchangeDocument document = ToDocument(roots, Width, Copy, cancellationToken);
		_dispatch.ValidateLuaArguments(CheatEngineToolNames.StructureExport, (object) Encode(document));
		using McpFileWrite write = _files.BeginWrite(full, CheatEngineToolNames.StructureExport, overwrite);
		try
		{
			StructureInterchangeCodec.Save(write.Stream, document, cancellationToken);
		}
		catch (InvalidDataException exception) { throw CheatEngineToolException.InvalidState($"The selected structures cannot be exported: {exception.Message}"); }
		cancellationToken.ThrowIfCancellationRequested();
		write.Commit();
		return new(Path.GetFileName(write.FullPath), StructureInterchangeCodec.Version, Width, roots, document.Layouts.Length);
	}

	[McpServerTool(Name = CheatEngineToolNames.StructureImport, Title = "Import structures", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description("Imports strict version-1 Structure Dissect JSON. Every layout is validated before mutation; existing names are refused and a failed native transaction rolls back private layouts before publication.")]
	public StructureInterchangeResult Import([Description("Absolute local version-1 structure interchange .json file.")] string path, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		string full = CheckExtension(_files.RequireRead(path, CheatEngineToolNames.StructureImport), ".json");
		using HeldFile file = _files.OpenRead(full, CheatEngineToolNames.StructureImport, StructureInterchangeCodec.MaximumBytes);
		if (!file.IsComplete || file.Length is <= 0 or > StructureInterchangeCodec.MaximumBytes)
		{
			throw CheatEngineToolException.LimitExceeded("path", $"file length must be 1 to {StructureInterchangeCodec.MaximumBytes} bytes.");
		}

		StructureInterchangeDocument document;
		try
		{
			using MemoryStream stream = new(file.Content.ToArray(), writable: false);
			document = StructureInterchangeCodec.Load(stream, cancellationToken);
		}
		catch (Exception exception) when (exception is InvalidDataException or System.Text.Json.JsonException) { throw CheatEngineToolException.InvalidArgument("path", $"is not a supported structure interchange file: {exception.Message}"); }
		object?[][] layouts = Encode(document);
		_dispatch.ValidateLuaArguments(CheatEngineToolNames.StructureImport, (object) layouts);
		StructureInterchangeLuaImport applied = _dispatch.Run(CheatEngineToolNames.StructureImport, token =>
		{
			ProcessSnapshot process = _dispatch.Client.Processes.GetCurrentProcess(token);
			int targetWidth = PointerWidth(process);
			if (targetWidth != document.PointerWidth)
			{
				throw CheatEngineToolException.InvalidState($"The selected target has {targetWidth}-byte pointers, but the file requires {document.PointerWidth}-byte pointers.");
			}

			StructureInterchangeLuaImport applied = _dispatch.ExecuteLua(CheatEngineToolNames.StructureImport,
				StructureInterchangeLua.Import, StructureInterchangeJsonContext.Default.StructureInterchangeLuaImport, token, (object) layouts);
			EnsureSameTarget(process, _dispatch.Client.Processes.GetCurrentProcess(token),
				CheatEngineToolNames.StructureImport, ToolHostEffect.Completed);
			return applied;
		}, cancellationToken);
		return new(Path.GetFileName(file.FullPath), StructureInterchangeCodec.Version, document.PointerWidth, document.Roots, applied.LayoutCount);
	}

	private static StructureInterchangeDocument ToDocument(string[] roots, int pointerWidth, StructureInterchangeLuaExport exported, CancellationToken token)
	{
		if (exported.Layouts is null || exported.Layouts.Length > StructureInterchangeCodec.MaximumLayouts)
		{
			throw CheatEngineToolException.LimitExceeded("names", "pointer-child closure contains more than 128 layouts.");
		}

		List<StructureInterchangeLayout> layouts = [];
		foreach (StructureInterchangeLuaLayout layout in exported.Layouts)
		{
			token.ThrowIfCancellationRequested();
			List<StructureInterchangeField> fields = [];
			foreach (StructureInterchangeLuaField field in layout.Fields)
			{
				if (field.Nested || field.Vartype is StructureValueCodec.VtBinary or StructureValueCodec.VtCustom || StructureValueCodec.ElementType(field.Vartype, field.Display) is StructureElementType.Other)
				{
					throw CheatEngineToolException.Unsupported($"Structure '{layout.Name}' has field {field.Index} that version 1 cannot represent.");
				}

				StructureElementType kind = StructureValueCodec.ElementType(field.Vartype, field.Display);
				McpValueType type = kind switch
				{
					StructureElementType.Int8 => McpValueType.Int8,
					StructureElementType.UInt8 => McpValueType.UInt8,
					StructureElementType.Int16 => McpValueType.Int16,
					StructureElementType.UInt16 => McpValueType.UInt16,
					StructureElementType.Int32 => McpValueType.Int32,
					StructureElementType.UInt32 => McpValueType.UInt32,
					StructureElementType.Int64 => McpValueType.Int64,
					StructureElementType.UInt64 => McpValueType.UInt64,
					StructureElementType.Float => McpValueType.Float,
					StructureElementType.Double => McpValueType.Double,
					StructureElementType.String => McpValueType.String,
					StructureElementType.WString => McpValueType.WString,
					StructureElementType.Bytes => McpValueType.Bytes,
					StructureElementType.Pointer => McpValueType.Pointer,
					_ => throw new InvalidOperationException()
				};
				fields.Add(new(HexFormat.Offset(field.Offset), field.Name ?? string.Empty, type.ToString(), StructureValueCodec.Display(field.Vartype, field.Display)?.ToString(), StructureValueCodec.IsSized(type) ? field.ByteSize : null, field.ChildStructure, field.ChildStructureStart is null ? null : HexFormat.Offset(field.ChildStructureStart.Value), null, null, null));
			}
			layouts.Add(new(layout.Name, layout.Internal, [.. fields]));
		}
		StructureInterchangeDocument result = new(StructureInterchangeCodec.Version, pointerWidth, roots, [.. layouts]);
		try
		{
			StructureInterchangeCodec.Validate(result, token);
		}
		catch (InvalidDataException e) { throw CheatEngineToolException.InvalidState($"The selected structures cannot be exported: {e.Message}"); }
		return result;
	}

	private static object?[][] Encode(StructureInterchangeDocument document)
	{
		Dictionary<string, int> indices = new(StringComparer.Ordinal);
		for (int i = 0; i < document.Layouts.Length; i++)
		{
			indices.Add(document.Layouts[i].Name, i + 1);
		}

		return [.. document.Layouts.Select(layout => (object?[]) [layout.Name, layout.Internal, StructureArguments.Specs([.. layout.Fields.Select(field => new StructureElementSpec(field.Offset, field.Name, Enum.Parse<McpValueType>(field.ValueType, true), field.Display is null ? null : Enum.Parse<StructureDisplay>(field.Display, true), field.ByteSize, field.ChildStructure, field.ChildStructureStart))], "layouts").Select((field, index) => { StructureInterchangeField original = layout.Fields[index]; object?[] result = [.. field]; result[5] = original.ChildStructure is null ? null : indices[original.ChildStructure]; return result; }).ToArray()])];
	}

	private static int PointerWidth(ProcessSnapshot process) => process.Bitness.IsKnown
		? process.Bitness.Bytes
		: throw CheatEngineToolException.Unsupported(
			"Cheat Engine does not report the target's pointer size, so structure layouts cannot be imported or exported.");

	private static void EnsureSameTarget(ProcessSnapshot before, ProcessSnapshot after, string operation,
		ToolHostEffect effect)
	{
		if (before.Id != after.Id || before.SelectionEpoch != after.SelectionEpoch)
		{
			throw new CheatEngineToolException(new ToolError(ToolErrorKind.TargetChanged,
				"The selected target changed while structure interchange was running; the result was discarded.", operation,
				effect, false, "Repeat the operation for the current target."));
		}
	}
	private static string CheckExtension(string full, string expected) => string.Equals(Path.GetExtension(full), expected, StringComparison.OrdinalIgnoreCase) ? full : throw CheatEngineToolException.InvalidArgument("path", $"must end in {expected}.");
}
