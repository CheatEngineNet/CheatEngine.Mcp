using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Tests.Core;

/// <summary>Every registered guard is consulted before a target change, instead of the legacy first-refusal chains.</summary>
public sealed class TargetTransitionGuardsTests
{
	private static readonly TargetTransition Change = new(42, 43);
	private static readonly string[] GuardOrder = ["a", "b"];
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public void EnsureCanChangeTarget_ReselectionOfTheSelectedProcess_ConsultsNoGuard()
	{
		ProbeGuard first = new(Busy("first"));
		ProbeGuard second = new(Busy("second"));
		TargetTransitionGuards guards = new([first, second]);

		guards.EnsureCanChangeTarget(new TargetTransition(42, 42), Token);

		Assert.Equal((0, 0), (first.Calls, second.Calls));
		Assert.False(new TargetTransition(null, null).IsReselection);
		Assert.False(new TargetTransition(null, 42).IsReselection);
		Assert.False(new TargetTransition(0, 0).IsReselection);
	}

	[Fact]
	public void EnsureCanChangeTarget_EveryGuardAllows_ConsultsThemAllInOrder()
	{
		List<string> order = [];
		TargetTransitionGuards guards = new([new ProbeGuard(null, order, "a"), new ProbeGuard(null, order, "b")]);

		guards.EnsureCanChangeTarget(Change, Token);

		Assert.Equal(GuardOrder, order);
		Assert.Equal(2, guards.Guards.Count);
	}

	[Fact]
	public void EnsureCanChangeTarget_SeveralRefusals_ConsultsEveryGuardAndCombinesThem()
	{
		ProbeGuard first = new(Busy("resources remain"));
		ProbeGuard allowing = new(null);
		ProbeGuard last = new(CheatEngineToolException.InvalidState("main scan runs"));
		TargetTransitionGuards guards = new([first, allowing, last]);

		CheatEngineToolException error =
			Assert.Throws<CheatEngineToolException>(() => guards.EnsureCanChangeTarget(Change, Token));

		Assert.Equal((1, 1, 1), (first.Calls, allowing.Calls, last.Calls));
		Assert.Equal((ToolErrorKind.Busy, ToolHostEffect.NotStarted), (error.Error.Kind, error.Error.HostEffect));
		Assert.Contains("resources remain", error.Error.Message, StringComparison.Ordinal);
		Assert.Contains("main scan runs", error.Error.Message, StringComparison.Ordinal);
		Assert.Equal(2, error.Error.Details!.Value.GetProperty("refusals").GetArrayLength());
	}

	[Fact]
	public void EnsureCanChangeTarget_SingleRefusal_IsRethrownUnchanged()
	{
		CheatEngineToolException refusal = Busy("only one");
		TargetTransitionGuards guards = new([new ProbeGuard(null), new ProbeGuard(refusal)]);

		Assert.Same(refusal,
			Assert.Throws<CheatEngineToolException>(() => guards.EnsureCanChangeTarget(Change, Token)));
	}

	[Fact]
	public void EnsureCanChangeTarget_GuardFailure_PropagatesAtOnceWithoutConsultingLaterGuards()
	{
		ProbeGuard failing = new(CheatEngineToolException.Internal("guard script failed"));
		ProbeGuard later = new(null);
		TargetTransitionGuards guards = new([new ProbeGuard(Busy("blocked")), failing, later]);

		CheatEngineToolException error =
			Assert.Throws<CheatEngineToolException>(() => guards.EnsureCanChangeTarget(Change, Token));

		Assert.Equal(ToolErrorKind.Internal, error.Error.Kind);
		Assert.Equal(0, later.Calls);
	}

	private static CheatEngineToolException Busy(string message)
	{
		return CheatEngineToolException.Busy(message, "hint");
	}

	private sealed class ProbeGuard(CheatEngineToolException? refusal, List<string>? order = null, string? name = null)
		: ITargetTransitionGuard
	{
		internal int Calls
		{
			get;
			private set;
		}

		public void EnsureCanChangeTarget(TargetTransition transition, CancellationToken cancellationToken)
		{
			Calls++;
			if (name is not null)
			{
				order?.Add(name);
			}

			if (refusal is not null)
			{
				throw refusal;
			}
		}
	}
}
