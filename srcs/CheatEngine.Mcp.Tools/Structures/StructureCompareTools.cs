using System.ComponentModel;
using System.Globalization;

using CheatEngine.Client;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Values;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools.Structures;

/// <summary>Compares groups of structure instances to find the fields that tell them apart.</summary>
[McpServerToolType]
public sealed class StructureCompareTools
{
	internal const int MaxGroup = 32;
	internal const int MaxSize = 16384;
	internal const int MaxCompareLimit = 128;
	internal const int MaxElements = 4096;

	private readonly ToolDispatch _dispatch;

	/// <summary>Creates the tools; nothing is dispatched until a call.</summary>
	/// <param name="dispatch">The activation's dispatch facade.</param>
	public StructureCompareTools(ToolDispatch dispatch)
	{
		ArgumentNullException.ThrowIfNull(dispatch);
		_dispatch = dispatch;
	}

	/// <summary>Compares two groups of structure instances field by field.</summary>
	[McpServerTool(Name = CheatEngineToolNames.StructureCompare, Title = "Compare structure instances",
		ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Compares instances byte for byte, like the dissect window's group compare, to find a field that separates " +
		"group A (such as the player) from group B (enemies). Rows are cells of granularity bytes over size bytes, or " +
		"the elements of structureName. Each row is constant, discriminator (constant inside each group, different " +
		"between them), varies_a, varies_b, varies_both or unreadable; mode picks which rows return. Validate a " +
		"discriminator with more samples before relying on it.")]
	public StructureComparison Compare(
		[Description("Group A's base addresses or symbol expressions (1-32).")]
		string[] groupA,
		[Description("Group B's base addresses or symbol expressions (0-32); discriminate needs at least one.")]
		string[]? groupB = null,
		[Description(
			"The bytes to compare from each base, a decimal count (1-16384) that is a multiple of granularity; pass this or structureName.")]
		int? size = null,
		[Description("Compare along this structure's elements instead of raw cells; pass this or size.")]
		string? structureName = null,
		[Description("The cell size in bytes for size mode: 1, 2, 4 or 8.")]
		int granularity = 4,
		[Description("How size-mode cells read: auto, signed, unsigned, float (granularity 4 or 8) or hex.")]
		StructureCompareFormat interpretAs = StructureCompareFormat.Auto,
		[Description("Which rows to return: discriminate, constant, differs (not equal everywhere) or all.")]
		StructureCompareMode mode = StructureCompareMode.Discriminate,
		[Description("The zero-based index of the first selected row to return.")]
		int offset = 0,
		[Description("The most rows to return (1-128).")]
		int limit = 64,
		CancellationToken cancellationToken = default)
	{
		string[] expressionsA = StructureArguments.Expressions(groupA, "groupA", 1, MaxGroup);
		string[] expressionsB = StructureArguments.Expressions(groupB, "groupB", 0, MaxGroup);
		if ((size is null) == (structureName is null))
		{
			throw CheatEngineToolException.InvalidArgument("size", "pass exactly one of size and structureName.");
		}

		string? structure = structureName is null ? null : StructureArguments.Name(structureName, "structureName");
		if (granularity is not (1 or 2 or 4 or 8))
		{
			throw CheatEngineToolException.InvalidArgument("granularity", "must be 1, 2, 4 or 8.");
		}

		if (size is { } bytes)
		{
			if (bytes < granularity)
			{
				throw CheatEngineToolException.InvalidArgument("size",
					$"must be at least the granularity, {granularity.ToString(CultureInfo.InvariantCulture)}.");
			}

			if (bytes > MaxSize)
			{
				throw CheatEngineToolException.LimitExceeded("size", $"must be at most {MaxSize}.");
			}

			if (bytes % granularity != 0)
			{
				throw CheatEngineToolException.InvalidArgument("size",
					$"must be a multiple of the granularity, {granularity.ToString(CultureInfo.InvariantCulture)}.");
			}

			if (interpretAs is StructureCompareFormat.Float && granularity < 4)
			{
				throw CheatEngineToolException.InvalidArgument("interpretAs",
					"float needs a granularity of 4 or 8.");
			}
		}

		if (mode is StructureCompareMode.Discriminate && expressionsB.Length == 0)
		{
			throw CheatEngineToolException.InvalidArgument("groupB",
				"must contain at least one address in discriminate mode.");
		}

		StructureArguments.Page(offset, limit, MaxCompareLimit);
		return _dispatch.Run(CheatEngineToolNames.StructureCompare, token =>
		{
			ICheatEngineClient client = _dispatch.Client;
			StructureField[] fields;
			long start;
			int span;
			if (structure is not null)
			{
				StructureLuaDefinition layout = _dispatch.ExecuteLua(CheatEngineToolNames.StructureCompare,
					StructureLuaScripts.Elements, StructureLuaJsonContext.Default.StructureLuaDefinition, token,
					structure, null, null, 0, MaxElements, false);
				if (layout.Total > MaxElements)
				{
					throw CheatEngineToolException.LimitExceeded("structureName",
						$"has {layout.Total.ToString(CultureInfo.InvariantCulture)} elements; at most {MaxElements} can be compared.");
				}

				(start, span) = Layout(layout.Elements, out fields);
			}
			else
			{
				start = 0;
				span = size!.Value;
				fields = new StructureField[span / granularity];
				for (int index = 0; index < fields.Length; index++)
				{
					fields[index] = new StructureField(index * granularity, index * granularity, granularity, null,
						null);
				}
			}

			StructureSample[] samplesA = Sample(client, expressionsA, "groupA", start, span, token);
			StructureSample[] samplesB = Sample(client, expressionsB, "groupB", start, span, token);
			List<(StructureField Field, StructureFieldClassification Classification)> selected = [];
			foreach (StructureField field in fields)
			{
				StructureFieldClassification classification = StructureComparer.Classify(samplesA, samplesB, field);
				if (StructureComparer.Selects(mode, classification))
				{
					selected.Add((field, classification));
				}
			}

			PageSlice<(StructureField Field, StructureFieldClassification Classification)> page =
				Paging.Slice(selected, offset, limit, MaxCompareLimit);
			StructureCompareRow[] rows = new StructureCompareRow[page.Items.Count];
			for (int index = 0; index < rows.Length; index++)
			{
				(StructureField field, StructureFieldClassification classification) = page.Items[index];
				StructureElementType? type = field.Type ??
											 StructureComparer.CellType(interpretAs, granularity,
												 samplesA.Concat(samplesB), field);
				rows[index] = new StructureCompareRow(HexFormat.Offset(field.Offset), field.Size, classification,
					[.. samplesA.Select(sample => StructureComparer.Format(sample, field, type))],
					[.. samplesB.Select(sample => StructureComparer.Format(sample, field, type))], field.Name);
			}

			return new StructureComparison([.. samplesA.Select(static sample => HexFormat.Address(sample.Address))],
				[.. samplesB.Select(static sample => HexFormat.Address(sample.Address))], rows, page.Total,
				page.NextOffset);
		}, cancellationToken);
	}

	/// <summary>Turns a structure's elements into compared fields and returns the byte range they cover.</summary>
	internal static (long Start, int Span) Layout(IReadOnlyList<StructureLuaElement> elements,
		out StructureField[] fields)
	{
		fields = [];
		if (elements.Count == 0)
		{
			return (0, 0);
		}

		long start = long.MaxValue, end = long.MinValue;
		foreach (StructureLuaElement element in elements)
		{
			start = Math.Min(start, element.Offset);
			end = Math.Max(end, element.Offset + Math.Max(element.ByteSize, 1));
		}

		if (end - start > MaxSize)
		{
			throw CheatEngineToolException.LimitExceeded("structureName",
				$"spans {(end - start).ToString(CultureInfo.InvariantCulture)} bytes; at most {MaxSize} can be compared.");
		}

		fields = new StructureField[elements.Count];
		for (int index = 0; index < fields.Length; index++)
		{
			StructureLuaElement element = elements[index];
			fields[index] = new StructureField(element.Offset, (int) (element.Offset - start),
				Math.Max(element.ByteSize, 1), element.Name ?? string.Empty,
				StructureValueCodec.ElementType(element.Vartype, element.Display));
		}

		return (start, (int) (end - start));
	}

	private static StructureSample[] Sample(ICheatEngineClient client, string[] expressions, string parameter,
		long start, int span, CancellationToken cancellationToken)
	{
		StructureSample[] samples = new StructureSample[expressions.Length];
		for (int index = 0; index < samples.Length; index++)
		{
			ulong address = StructureArguments.Resolve(client, expressions[index],
				StructureArguments.Item(parameter, index), cancellationToken);
			samples[index] = new StructureSample(address,
				StructureValueTools.ReadSpan(client, address, start, span, cancellationToken));
		}

		return samples;
	}
}
