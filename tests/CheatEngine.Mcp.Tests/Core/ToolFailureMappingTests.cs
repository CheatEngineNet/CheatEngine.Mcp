using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Tests.Core;

public sealed class ToolFailureMappingTests
{
	private static readonly (CheatEngineFailureKind Client, ToolErrorKind Kind, string? Hint)[] Rows =
	[
		(CheatEngineFailureKind.Unknown, ToolErrorKind.Internal, null),
		(CheatEngineFailureKind.Cancelled, ToolErrorKind.Cancelled, null),
		(CheatEngineFailureKind.CapabilityUnavailable, ToolErrorKind.Unsupported, null),
		(CheatEngineFailureKind.OperationRejected, ToolErrorKind.HostRefused, null),
		(CheatEngineFailureKind.NotFound, ToolErrorKind.NotFound, null),
		(CheatEngineFailureKind.AmbiguousMatch, ToolErrorKind.InvalidArgument, ToolFailureMapping.AmbiguousHint),
		(CheatEngineFailureKind.ResultLimitExceeded, ToolErrorKind.LimitExceeded, ToolFailureMapping.LimitHint),
		(CheatEngineFailureKind.LuaError, ToolErrorKind.HostRefused, null),
		(CheatEngineFailureKind.BindingError, ToolErrorKind.Internal, null),
		(CheatEngineFailureKind.InvalidHostResult, ToolErrorKind.Internal, null),
		(CheatEngineFailureKind.Unsupported, ToolErrorKind.Unsupported, null),
		(CheatEngineFailureKind.TargetNotAttached, ToolErrorKind.NotAttached, ToolFailureMapping.AttachHint),
		(CheatEngineFailureKind.MemoryReadFailed, ToolErrorKind.MemoryReadFailed, ToolFailureMapping.MemoryReadHint),
		(CheatEngineFailureKind.MemoryWriteFailed, ToolErrorKind.MemoryWriteFailed,
			ToolFailureMapping.MemoryWriteHint),
		(CheatEngineFailureKind.ActivationExpired, ToolErrorKind.Stopping, ToolFailureMapping.ActivationEndedHint),
		(CheatEngineFailureKind.InvalidState, ToolErrorKind.InvalidState, null),
		(CheatEngineFailureKind.IndeterminateHostResult, ToolErrorKind.Internal,
			ToolFailureMapping.IndeterminateHint),
		(CheatEngineFailureKind.TargetChanged, ToolErrorKind.TargetChanged, ToolFailureMapping.TargetChangedHint),
		(CheatEngineFailureKind.TargetIdentityUnavailable, ToolErrorKind.TargetChanged,
			ToolFailureMapping.TargetChangedHint),
		(CheatEngineFailureKind.RuntimeChanged, ToolErrorKind.InvalidState, ToolFailureMapping.RuntimeChangedHint)
	];

	/// <summary>The reviewed table: every Client failure kind, its contract kind and hint.</summary>
	public static TheoryData<CheatEngineFailureKind, ToolErrorKind, string?> Table
	{
		get
		{
			TheoryData<CheatEngineFailureKind, ToolErrorKind, string?> data = [];
			foreach ((CheatEngineFailureKind client, ToolErrorKind kind, string? hint) in Rows)
			{
				data.Add(client, kind, hint);
			}

			return data;
		}
	}

	[Fact]
	public void Table_CoversEveryClientFailureKind()
	{
		Assert.Equal(Enum.GetValues<CheatEngineFailureKind>().Order(),
			Rows.Select(static row => row.Client).Order());
	}

	[Theory]
	[MemberData(nameof(Table))]
	public void Map_ClientFailureKind_UsesTheReviewedKindAndHint(CheatEngineFailureKind clientKind,
		ToolErrorKind expectedKind, string? expectedHint)
	{
		CheatEngineFailure failure = new(clientKind, "Probe.Operation", "Probe failure.",
			hostEffect: CheatEngineHostEffect.NotApplied);

		ToolError error = ToolFailureMapping.Map(failure, false);

		Assert.Equal(expectedKind, error.Kind);
		Assert.Equal(expectedHint, error.Hint);
		Assert.Equal("Probe failure.", error.Message);
		Assert.Equal("Probe.Operation", error.Operation);
		Assert.Equal(ToolHostEffect.NotApplied, error.HostEffect);
		Assert.Null(error.Details);
	}

	[Theory]
	[InlineData(CheatEngineFailureKind.Cancelled)]
	[InlineData(CheatEngineFailureKind.InvalidState)]
	[InlineData(CheatEngineFailureKind.ActivationExpired)]
	public void Map_WhileTheActivationStops_ReportsStopping(CheatEngineFailureKind clientKind)
	{
		ToolError error = ToolFailureMapping.Map(new CheatEngineFailure(clientKind, "Probe", "Stopping."), true);

		Assert.Equal(ToolErrorKind.Stopping, error.Kind);
		Assert.Equal(ToolFailureMapping.ActivationEndedHint, error.Hint);
		Assert.False(error.Retryable);
	}

	[Theory]
	[InlineData(CheatEngineHostEffect.NotStarted, ToolHostEffect.NotStarted)]
	[InlineData(CheatEngineHostEffect.NotApplied, ToolHostEffect.NotApplied)]
	[InlineData(CheatEngineHostEffect.Started, ToolHostEffect.Started)]
	[InlineData(CheatEngineHostEffect.Completed, ToolHostEffect.Completed)]
	[InlineData(CheatEngineHostEffect.CleanupUnconfirmed, ToolHostEffect.CleanupUnconfirmed)]
	[InlineData(CheatEngineHostEffect.Unknown, ToolHostEffect.Unknown)]
	public void MapHostEffect_EveryClientEffect_MapsOneForOne(CheatEngineHostEffect client, ToolHostEffect expected)
	{
		Assert.Equal(expected, ToolFailureMapping.MapHostEffect(client));
	}

	[Fact]
	public void MapHostEffect_ClientMembers_AreAllCoveredByTheContract()
	{
		Assert.Equal(Enum.GetValues<CheatEngineHostEffect>().Length, Enum.GetValues<ToolHostEffect>().Length);
		Assert.Equal(Enum.GetValues<ToolHostEffect>().Order(),
			Enum.GetValues<CheatEngineHostEffect>().Select(ToolFailureMapping.MapHostEffect).Order());
	}

	[Theory]
	[InlineData(ToolErrorKind.Busy, ToolHostEffect.NotStarted, true)]
	[InlineData(ToolErrorKind.Busy, ToolHostEffect.NotApplied, true)]
	[InlineData(ToolErrorKind.Cancelled, ToolHostEffect.NotStarted, true)]
	[InlineData(ToolErrorKind.Cancelled, ToolHostEffect.Started, false)]
	[InlineData(ToolErrorKind.Busy, ToolHostEffect.Unknown, false)]
	[InlineData(ToolErrorKind.Timeout, ToolHostEffect.NotStarted, false)]
	[InlineData(ToolErrorKind.NotFound, ToolHostEffect.NotStarted, false)]
	[InlineData(ToolErrorKind.Internal, ToolHostEffect.NotApplied, false)]
	public void IsRetryable_OnlyBusyOrCancelledWithoutEffect_IsRetryable(ToolErrorKind kind, ToolHostEffect effect,
		bool expected)
	{
		Assert.Equal(expected, ToolFailureMapping.IsRetryable(kind, effect));
	}

	[Fact]
	public void Map_CancelledBeforeWork_IsRetryable()
	{
		ToolError error = ToolFailureMapping.Map(new CheatEngineFailure(CheatEngineFailureKind.Cancelled,
			"Memory.ReadBytes", "Cancelled.", hostEffect: CheatEngineHostEffect.NotStarted), false);

		Assert.Equal(ToolErrorKind.Cancelled, error.Kind);
		Assert.True(error.Retryable);
	}

	[Fact]
	public void Map_DefaultFailure_IsInternalWithAMessage()
	{
		ToolError error = ToolFailureMapping.Map(default, false);

		Assert.Equal(ToolErrorKind.Internal, error.Kind);
		Assert.False(string.IsNullOrWhiteSpace(error.Message));
		Assert.Null(error.Operation);
	}
}
