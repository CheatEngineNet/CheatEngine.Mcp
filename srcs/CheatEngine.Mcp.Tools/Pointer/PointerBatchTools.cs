using System.ComponentModel;

using CheatEngine.Client;
using CheatEngine.Client.Processes;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.SDK.Engine.Values;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools.Pointer;

/// <summary>Validates a bounded set of supplied pointer chains without retaining a scan.</summary>
[McpServerToolType]
public sealed class PointerBatchTools
{
	/// <summary>The largest caller-supplied batch.</summary>
	internal const int MaximumCandidates = 128;

	/// <summary>The candidates followed by one main-thread dispatch.</summary>
	internal const int CandidatesPerDispatch = 32;

	/// <summary>The pointer and final-value reads admitted to one main-thread dispatch.</summary>
	internal const int MaximumReadsPerDispatch = 128;

	/// <summary>The largest combined number of pointer dereferences and final-value reads in one request.</summary>
	internal const int MaximumTotalReads = 4_096;

	/// <summary>The largest combined maximum byte count of requested final values.</summary>
	internal const int MaximumFinalValueBytes = 65_536;

	private readonly ToolDispatch _dispatch;

	/// <summary>Creates the tool; no target state is read until validation starts.</summary>
	public PointerBatchTools(ToolDispatch dispatch)
	{
		ArgumentNullException.ThrowIfNull(dispatch);
		_dispatch = dispatch;
	}

	/// <summary>Follows supplied pointer chains in bounded dispatch slices.</summary>
	[McpServerTool(Name = CheatEngineToolNames.PointerReadChains, Title = "Read pointer chains", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Validates up to 128 supplied pointer chains without creating a scan. Each chain dereferences then adds its signed hexadecimal offset. " +
		"Module expressions are resolved afresh in every bounded slice; absolute roots remain absolute after a target restart. " +
		"An unreadable hop, target comparison, and optional final-value read are reported independently per candidate.")]
	public PointerChainBatchResult ReadChains(
		[Description("One to 128 candidates with a unique id, base expression and one to 64 signed hexadecimal offsets.")]
		PointerChainCandidate[] candidates,
		[Description("Optionally compare each resolved final address with this current-target expression.")]
		string? target = null,
		[Description("The optional type of the value read after comparison.")]
		McpValueType? valueType = null,
		[Description("For bytes, the required byte count; for string and wstring, the maximum length.")]
		int? length = null,
		[Description("Return only matching candidate entries while summary counts still include misses and errors.")]
		bool matchesOnly = false,
		CancellationToken cancellationToken = default)
	{
		PreparedCandidate[] prepared = Prepare(candidates);
		string? expectedExpression = target is null ? null : PointerSupport.Expression(target, "target");
		if (matchesOnly && expectedExpression is null)
		{
			throw CheatEngineToolException.InvalidArgument("matchesOnly", "requires target.");
		}

		ValidateValue(valueType, length, prepared.Length);
		ValidateReadCount(prepared, valueType);
		(ProcessSnapshot process, int width) = _dispatch.Run(CheatEngineToolNames.PointerReadChains,
			token =>
			{
				ICheatEngineClient client = _dispatch.Client;
				ProcessSnapshot snapshot = client.Processes.GetCurrentProcess(token);
				return (snapshot, PointerSupport.Width(snapshot));
			}, cancellationToken);

		List<PointerChainCandidateResult> all = new(prepared.Length);
		bool cancelled = false;
		for (int first = 0; first < prepared.Length;)
		{
			if (cancellationToken.IsCancellationRequested)
			{
				cancelled = true;
				break;
			}

			int start = first;
			int end = SliceEnd(prepared, start, valueType);
			try
			{
				SliceResult slice = _dispatch.Run(CheatEngineToolNames.PointerReadChains, token =>
				{
					ICheatEngineClient client = _dispatch.Client;
					EnsureSameTarget(client, process, token);
					ulong? expected = expectedExpression is null ? null : PointerTargets.Check(
						PointerSupport.Resolve(client, expectedExpression, token), width, "target");
					EnsureSameTarget(client, process, token);
					List<PointerChainCandidateResult> outcomes = new(end - start);
					for (int index = start; index < end; index++)
					{
						if (token.IsCancellationRequested)
						{
							return new SliceResult([.. outcomes], true);
						}

						try
						{
							EnsureSameTarget(client, process, token);
							outcomes.Add(Follow(client, process, prepared[index], width, expected, valueType, length, token));
						}
						catch (OperationCanceledException) when (token.IsCancellationRequested)
						{
							return new SliceResult([.. outcomes], true);
						}
					}

					return new SliceResult([.. outcomes], false);
				}, cancellationToken);
				all.AddRange(slice.Candidates);
				if (slice.Cancelled)
				{
					cancelled = true;
					break;
				}
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				cancelled = true;
				break;
			}

			first = end;
		}

		PointerChainBatchSummary summary = Summarize(prepared.Length, all, cancelled);
		IReadOnlyList<PointerChainCandidateResult> listed = matchesOnly
			? [.. all.Where(static item => item.ComparisonStatus is PointerChainComparisonStatus.Match)]
			: all;
		return new PointerChainBatchResult(listed, summary);
	}

	private static PreparedCandidate[] Prepare(PointerChainCandidate[]? candidates)
	{
		if (candidates is null || candidates.Length == 0)
		{
			throw CheatEngineToolException.InvalidArgument("candidates", "must contain at least one candidate.");
		}

		PointerSupport.Range(candidates.Length, 1, MaximumCandidates, "candidates");
		HashSet<string> ids = new(StringComparer.Ordinal);
		PreparedCandidate[] prepared = new PreparedCandidate[candidates.Length];
		int dereferences = 0;
		for (int index = 0; index < candidates.Length; index++)
		{
			PointerChainCandidate candidate = candidates[index] ?? throw CheatEngineToolException.InvalidArgument(
				$"candidates[{index}]", "must not be null.");
			string id = PointerSupport.Name(candidate.Id, $"candidates[{index}].id");
			if (!ids.Add(id))
			{
				throw CheatEngineToolException.InvalidArgument($"candidates[{index}].id", "must be unique within candidates.");
			}

			string root = PointerSupport.Expression(candidate.Base, $"candidates[{index}].base");
			if (candidate.Offsets is null || candidate.Offsets.Length == 0)
			{
				throw CheatEngineToolException.InvalidArgument($"candidates[{index}].offsets",
					$"must contain 1 to {PointerChainTools.MaximumOffsets} offsets; a chain dereferences at least once.");
			}
			dereferences = checked(dereferences + candidate.Offsets.Length);
			if (dereferences > MaximumTotalReads)
			{
				throw CheatEngineToolException.LimitExceeded("candidates",
					$"must contain no more than {MaximumTotalReads} total pointer dereferences.");
			}

			prepared[index] = new PreparedCandidate(id, root, $"candidates[{index}].base",
				HexParse.Offsets(candidate.Offsets, $"candidates[{index}].offsets", PointerChainTools.MaximumOffsets));
		}

		return prepared;
	}

	private static void ValidateValue(McpValueType? valueType, int? length, int candidateCount)
	{
		if (valueType is { } type && !Enum.IsDefined(type))
		{
			throw CheatEngineToolException.InvalidArgument("valueType", "must name a supported value type.");
		}

		if (length is not null && valueType is null)
		{
			throw CheatEngineToolException.InvalidArgument("length", "requires valueType.");
		}

		if (length is { } requested)
		{
			PointerSupport.Range(requested, 1, McpValueCodec.MaxLength, "length");
		}
		else if (valueType is McpValueType.Bytes)
		{
			throw CheatEngineToolException.InvalidArgument("length", "is required for the bytes value type.");
		}

		if (valueType is not { } requestedType)
		{
			return;
		}

		long total = (long) candidateCount * ValueByteBound(requestedType, length);
		if (total > MaximumFinalValueBytes)
		{
			throw CheatEngineToolException.LimitExceeded("length",
				$"the combined final-value read limit is {MaximumFinalValueBytes} bytes.");
		}
	}

	private static int ValueByteBound(McpValueType type, int? length)
	{
		return type switch
		{
			McpValueType.Bytes => length!.Value,
			McpValueType.String => length ?? McpValueCodec.DefaultStringLength,
			McpValueType.WString => checked((length ?? McpValueCodec.DefaultStringLength) * 2),
			_ => 8
		};
	}

	private static void ValidateReadCount(IReadOnlyList<PreparedCandidate> candidates, McpValueType? valueType)
	{
		int reads = candidates.Sum(static candidate => candidate.Offsets.Length);
		if (valueType is not null)
		{
			reads = checked(reads + candidates.Count);
		}

		if (reads > MaximumTotalReads)
		{
			throw CheatEngineToolException.LimitExceeded("candidates",
				$"must contain no more than {MaximumTotalReads} total pointer and final-value reads.");
		}
	}

	private static int SliceEnd(IReadOnlyList<PreparedCandidate> candidates, int start, McpValueType? valueType)
	{
		int reads = 0;
		int end = start;
		while (end < candidates.Count && end - start < CandidatesPerDispatch)
		{
			int candidateReads = candidates[end].Offsets.Length + (valueType is null ? 0 : 1);
			if (end > start && reads + candidateReads > MaximumReadsPerDispatch)
			{
				break;
			}

			reads += candidateReads;
			end++;
		}

		return end;
	}

	private static void EnsureSameTarget(ICheatEngineClient client, ProcessSnapshot expected, CancellationToken token)
	{
		token.ThrowIfCancellationRequested();
		ProcessSnapshot current = client.Processes.GetCurrentProcess(token);
		if (current.Id != expected.Id || current.SelectionEpoch != expected.SelectionEpoch)
		{
			throw PointerSupport.TargetChanged(
				"The selected process changed during pointer-chain validation; no mixed-target results were returned.");
		}
	}

	private static PointerChainCandidateResult Follow(ICheatEngineClient client, ProcessSnapshot process,
		PreparedCandidate candidate, int width,
		ulong? expected, McpValueType? valueType, int? length, CancellationToken token)
	{
		try
		{
			EnsureSameTarget(client, process, token);
			ulong current = PointerTargets.Check(PointerSupport.Resolve(client, candidate.Root, token), width,
				candidate.BaseParameter);
			EnsureSameTarget(client, process, token);
			for (int index = 0; index < candidate.Offsets.Length; index++)
			{
				EnsureSameTarget(client, process, token);
				if (!client.Memory.TryReadPrimitive(new Address(current), out Address pointer,
						out CheatEngineFailure failure, token))
				{
					EnsureSameTarget(client, process, token);
					CheatEngineToolException mapped = CheatEngineToolException.FromFailure(failure,
						client.Stopping.IsCancellationRequested);
					if (mapped.Error.Kind is ToolErrorKind.TargetChanged)
					{
						throw mapped;
					}

					return failure.Kind is CheatEngineFailureKind.MemoryReadFailed
						? Unreadable(candidate.Id, index, current, expected is not null, valueType is not null)
						: Error(candidate.Id, mapped.Error, expected is not null, valueType is not null);
				}
				EnsureSameTarget(client, process, token);

				if (!PointerMap.TryAdd(pointer.ToUInt64(), candidate.Offsets[index], width, out current))
				{
					return Error(candidate.Id, CheatEngineToolException.InvalidArgument("offsets",
						"pointer plus offset leaves the target address space.").Error, expected is not null, valueType is not null);
				}
			}

			PointerChainComparisonStatus comparison = expected is null
				? PointerChainComparisonStatus.NotRequested
				: current == expected.Value ? PointerChainComparisonStatus.Match : PointerChainComparisonStatus.Miss;
			if (valueType is not { } type)
			{
				return new PointerChainCandidateResult(candidate.Id, PointerChainResolutionStatus.Resolved,
					HexFormat.Address(current), null, null, null, comparison, PointerChainValueStatus.NotRequested, null, null);
			}

			try
			{
				EnsureSameTarget(client, process, token);
				string value = McpValueCodec.Read(client, new Address(current), type, length, token);
				EnsureSameTarget(client, process, token);
				return new PointerChainCandidateResult(candidate.Id, PointerChainResolutionStatus.Resolved,
					HexFormat.Address(current), null, null, null, comparison, PointerChainValueStatus.Read,
					value, null);
			}
			catch (CheatEngineClientException exception)
			{
				CheatEngineToolException mapped = CheatEngineToolException.FromFailure(exception.Failure,
					client.Stopping.IsCancellationRequested);
				if (mapped.Error.Kind is ToolErrorKind.TargetChanged)
				{
					throw mapped;
				}

				return new PointerChainCandidateResult(candidate.Id, PointerChainResolutionStatus.Resolved,
					HexFormat.Address(current), null, null, null, comparison, PointerChainValueStatus.Failed, null,
					mapped.Error);
			}
			catch (CheatEngineToolException exception) when (exception.Error.Kind is not ToolErrorKind.TargetChanged)
			{
				return new PointerChainCandidateResult(candidate.Id, PointerChainResolutionStatus.Resolved,
					HexFormat.Address(current), null, null, null, comparison, PointerChainValueStatus.Failed, null,
					exception.Error);
			}
		}
		catch (CheatEngineToolException exception) when (exception.Error.Kind is not ToolErrorKind.TargetChanged)
		{
			return Error(candidate.Id, exception.Error, expected is not null, valueType is not null);
		}
		catch (CheatEngineClientException exception)
		{
			CheatEngineToolException mapped = CheatEngineToolException.FromFailure(exception.Failure,
				client.Stopping.IsCancellationRequested);
			if (mapped.Error.Kind is ToolErrorKind.TargetChanged)
			{
				throw mapped;
			}

			return Error(candidate.Id, mapped.Error,
				expected is not null, valueType is not null);
		}
	}

	private static PointerChainCandidateResult Unreadable(string id, int hop, ulong readAt, bool comparisonRequested,
		bool valueRequested)
	{
		return new PointerChainCandidateResult(id, PointerChainResolutionStatus.Unreadable, null, hop,
			HexFormat.Address(readAt), null, comparisonRequested ? PointerChainComparisonStatus.NotResolved : PointerChainComparisonStatus.NotRequested,
			valueRequested ? PointerChainValueStatus.NotAttempted : PointerChainValueStatus.NotRequested, null, null);
	}

	private static PointerChainCandidateResult Error(string id, ToolError error, bool comparisonRequested,
		bool valueRequested)
	{
		return new PointerChainCandidateResult(id, PointerChainResolutionStatus.Error, null, null, null, error,
			comparisonRequested ? PointerChainComparisonStatus.NotResolved : PointerChainComparisonStatus.NotRequested,
			valueRequested ? PointerChainValueStatus.NotAttempted : PointerChainValueStatus.NotRequested, null, null);
	}

	private static PointerChainBatchSummary Summarize(int submitted, IEnumerable<PointerChainCandidateResult> candidates,
		bool cancelled)
	{
		PointerChainCandidateResult[] items = [.. candidates];
		return new PointerChainBatchSummary(submitted, items.Length,
			items.Count(static item => item.ChainStatus is PointerChainResolutionStatus.Resolved),
			items.Count(static item => item.ChainStatus is PointerChainResolutionStatus.Unreadable),
			items.Count(static item => item.ChainStatus is PointerChainResolutionStatus.Error),
			items.Count(static item => item.ComparisonStatus is PointerChainComparisonStatus.Match),
			items.Count(static item => item.ComparisonStatus is PointerChainComparisonStatus.Miss),
			items.Count(static item => item.ValueStatus is PointerChainValueStatus.Read),
			items.Count(static item => item.ValueStatus is PointerChainValueStatus.Failed), cancelled);
	}

	private sealed record PreparedCandidate(string Id, string Root, string BaseParameter, long[] Offsets);

	private sealed record SliceResult(PointerChainCandidateResult[] Candidates, bool Cancelled);
}
