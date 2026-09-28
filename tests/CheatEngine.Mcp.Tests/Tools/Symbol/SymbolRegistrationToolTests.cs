using CheatEngine.Client.Inspection;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tests.Tools.Modules;
using CheatEngine.Mcp.Tools.Symbol;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Mcp.Tests.Tools.Symbol;

/// <summary>Registered symbols as activation-owned leases tracked in <see cref="TargetResources" />.</summary>
public sealed class SymbolRegistrationToolTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public void Register_Symbol_IsAnOwnedLeaseTrackedAsASymbolResource()
	{
		Harness harness = new();

		RegisteredSymbol symbol = harness.Tools.Register("playerBase", "game.exe+10", false, Token);

		Assert.Equal(("playerBase", "140000010", false), (symbol.Name, symbol.Address, symbol.DoNotSave));
		TargetResourceDescriptor resource = Assert.Single(harness.Resources.List());
		Assert.Equal((symbol.ResourceId, "symbol", TargetResourceCategory.ClientLease, "playerBase", "140000010"),
			(resource.Id, resource.Kind, resource.Category, resource.Name, resource.Address));
		SymbolRegistration registration = Assert.Single(harness.Registered);
		Assert.Equal(("playerBase", 0x140000010UL, false),
			(registration.Name, registration.Address.ToUInt64(), registration.DoNotSave));
		Assert.Equal(1, harness.Registrations.Count);
	}

	[Fact]
	public void Register_UnresolvedAddress_IsNotFoundAndReleasesTheReservation()
	{
		Harness harness = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			harness.Tools.Register("playerBase", "nowhere", cancellationToken: Token));

		Assert.Equal(ToolErrorKind.NotFound, exception.Error.Kind);
		Assert.Empty(harness.Registered);
		Assert.Equal(0, harness.Registrations.Count);
	}

	[Fact]
	public void Register_HostRefusal_KeepsNothingAndLeavesTheNameFree()
	{
		Harness harness = new();
		harness.Target.Register = static _ => throw new CheatEngineFailure(CheatEngineFailureKind.OperationRejected,
			"Inspection.RegisterSymbol", "The symbol name already resolves in Cheat Engine.",
			hostEffect: CheatEngineHostEffect.NotApplied).ToException();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			harness.Tools.Register("kernel32", "game.exe+10", cancellationToken: Token));

		Assert.Equal(ToolErrorKind.HostRefused, exception.Error.Kind);
		Assert.Empty(harness.Resources.List());
		Assert.Equal(0, harness.Registrations.Count);
	}

	[Fact]
	public void Register_OwnedName_IsRefusedWithoutDispatchInAnyCase()
	{
		Harness harness = new();
		harness.Tools.Register("playerBase", "game.exe+10", cancellationToken: Token);
		int dispatches = harness.Target.Dispatcher.Calls;

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			harness.Tools.Register("PLAYERBASE", "game.exe+10", cancellationToken: Token));

		Assert.Equal(ToolErrorKind.InvalidArgument, exception.Error.Kind);
		Assert.Equal(dispatches, harness.Target.Dispatcher.Calls);
	}

	[Fact]
	public void Register_BeyondTheOwnedCap_IsLimitExceededWithoutDispatch()
	{
		Harness harness = new();
		for (int index = 0; index < SymbolRegistrations.MaximumOwned; index++)
		{
			harness.Tools.Register($"s{index}", "game.exe+10", cancellationToken: Token);
		}

		int dispatches = harness.Target.Dispatcher.Calls;
		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			harness.Tools.Register("oneMore", "game.exe+10", cancellationToken: Token));

		Assert.Equal(ToolErrorKind.LimitExceeded, exception.Error.Kind);
		Assert.Equal(dispatches, harness.Target.Dispatcher.Calls);
		Assert.Equal(SymbolRegistrations.MaximumOwned, harness.Resources.Count);
	}

	[Theory]
	[InlineData("")]
	[InlineData(" padded")]
	[InlineData("tab\tname")]
	public void Register_RefusedName_NeverDispatches(string name)
	{
		Harness harness = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			harness.Tools.Register(name, "game.exe+10", cancellationToken: Token));

		Assert.Equal(("name", ToolErrorKind.InvalidArgument),
			(exception.Error.Details!.Value.GetProperty("parameter").GetString(), exception.Error.Kind));
		Assert.Equal(0, harness.Target.Dispatcher.Calls);
	}

	[Fact]
	public void Unregister_OwnedSymbol_ReleasesItsLeaseAndForgetsIt()
	{
		Harness harness = new();
		harness.Tools.Register("playerBase", "game.exe+10", cancellationToken: Token);

		SymbolReleaseResult result = harness.Tools.Unregister("PlayerBase", Token);

		Assert.Equal(("playerBase", "140000010", ResourceReleaseKind.Released, true),
			(result.Name, result.Address, result.Release.Kind, result.Release.IsComplete));
		Assert.Empty(harness.Resources.List());
		Assert.Equal(0, harness.Registrations.Count);
		Assert.Equal(1, harness.Leases.Single().Releases);
	}

	[Fact]
	public void Unregister_UnknownName_IsNotFoundWithoutDispatch()
	{
		Harness harness = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			harness.Tools.Unregister("tableSymbol", Token));

		Assert.Equal(ToolErrorKind.NotFound, exception.Error.Kind);
		Assert.Contains("symbol_list_registered", exception.Error.Hint, StringComparison.Ordinal);
		Assert.Equal(0, harness.Target.Dispatcher.Calls);
	}

	[Fact]
	public void Unregister_RetryableRelease_IsARetryablePartialEffectThatKeepsTheSymbolTracked()
	{
		Harness harness = new();
		harness.Tools.Register("playerBase", "game.exe+10", cancellationToken: Token);
		harness.Leases.Single().Outcome = new LeaseReleaseOutcome(LeaseReleaseKind.CleanupUnavailable,
			CheatEngineHostEffect.NotStarted);

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			harness.Tools.Unregister("playerBase", Token));

		Assert.Equal((ToolErrorKind.PartialEffect, ToolHostEffect.NotStarted, true),
			(exception.Error.Kind, exception.Error.HostEffect, exception.Error.Retryable));
		Assert.Equal("cleanup_unavailable",
			exception.Error.Details!.Value.GetProperty("release").GetProperty("kind").GetString());
		Assert.Single(harness.Resources.List());
		Assert.Equal(1, harness.Registrations.Count);
	}

	[Fact]
	public void Unregister_UnconfirmedRelease_IsAFinalPartialEffectAndForgetsTheSymbol()
	{
		Harness harness = new();
		harness.Tools.Register("playerBase", "game.exe+10", cancellationToken: Token);
		harness.Leases.Single().Outcome = new LeaseReleaseOutcome(LeaseReleaseKind.CleanupUnconfirmed,
			CheatEngineHostEffect.CleanupUnconfirmed);

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			harness.Tools.Unregister("playerBase", Token));

		Assert.Equal((ToolErrorKind.PartialEffect, false), (exception.Error.Kind, exception.Error.Retryable));
		Assert.Empty(harness.Resources.List());
		Assert.Equal(0, harness.Registrations.Count);
	}

	[Fact]
	public void ReleaseAll_OwnedSymbol_IsReleasedThroughTheResourcesAndForgottenHere()
	{
		Harness harness = new();
		harness.Tools.Register("playerBase", "game.exe+10", cancellationToken: Token);

		ReleaseAllResult released = harness.Target.Dispatch.Run("runtime_release_resources",
			token => harness.Resources.ReleaseAll(token), CancellationToken.None);

		Assert.Equal("symbol", Assert.Single(released.Released).Resource.Kind);
		Assert.Equal(0, harness.Registrations.Count);
		Assert.Throws<CheatEngineToolException>(() => harness.Tools.Unregister("playerBase", Token));
	}

	[Fact]
	public void ListRegistered_CheatEngineSymbols_ArePagedAndOwnedOnesMarked()
	{
		Harness harness = new();
		RegisteredSymbol owned = harness.Tools.Register("playerBase", "game.exe+10", cancellationToken: Token);
		harness.Target.LuaResults[typeof(LuaRegisteredSymbols)] = new LuaRegisteredSymbols(
		[
			new LuaRegisteredSymbol("playerbase", "140000010", null, null, true),
			new LuaRegisteredSymbol("tableAlloc", "20000000", 4096, ModuleSymbolTarget.ProcessId),
			new LuaRegisteredSymbol("other", "30000000")
		], 9000, true);

		RegisteredSymbolList page = harness.Tools.ListRegistered(limit: 2, cancellationToken: Token);
		RegisteredSymbolList filtered = harness.Tools.ListRegistered("ALLOC", cancellationToken: Token);

		Assert.Equal((3, 2, true), (page.Total, page.NextOffset, page.Truncated));
		Assert.Equal(new RegisteredSymbolEntry("playerbase", true, "140000010", true, null, null, owned.ResourceId),
			page.Symbols[0]);
		Assert.Equal(new RegisteredSymbolEntry("tableAlloc", false, "20000000", null, 4096,
			ModuleSymbolTarget.ProcessId), page.Symbols[1]);
		Assert.Equal("tableAlloc", Assert.Single(filtered.Symbols).Name);
		Assert.Contains($"[1] = {SymbolScripts.MaximumRegisteredSymbols}", harness.Target.LuaSources[0],
			StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(-1, 10, ToolErrorKind.InvalidArgument)]
	[InlineData(0, 1001, ToolErrorKind.LimitExceeded)]
	public void ListRegistered_RefusedPaging_NeverDispatches(int offset, int limit, ToolErrorKind kind)
	{
		Harness harness = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			harness.Tools.ListRegistered(null, offset, limit, Token));

		Assert.Equal(kind, exception.Error.Kind);
		Assert.Equal(0, harness.Target.Dispatcher.Calls);
	}

	/// <summary>A registration tool over a simulated target whose leases record their releases.</summary>
	private sealed class Harness
	{
		internal Harness()
		{
			Target.Addresses["game.exe+10"] = 0x140000010;
			Target.Register = registration =>
			{
				Registered.Add(registration);
				FakeLease lease = new(registration.Name, registration.Address);
				Leases.Add(lease);
				return lease.Proxy;
			};
			Tools = new SymbolRegistrationTools(Target.Dispatch, Resources, Registrations);
		}

		internal ModuleSymbolTarget Target
		{
			get;
		} = new();

		internal TargetResources Resources
		{
			get;
		} = new();

		internal SymbolRegistrations Registrations
		{
			get;
		} = new();

		internal SymbolRegistrationTools Tools
		{
			get;
		}

		internal List<SymbolRegistration> Registered
		{
			get;
		} = [];

		internal List<FakeLease> Leases
		{
			get;
		} = [];
	}

	/// <summary>A symbol lease whose release outcome a test chooses; a completed release ends it.</summary>
	private sealed class FakeLease
	{
		internal FakeLease(string name, Address address)
		{
			Proxy = ClientTestDouble.Create<ISymbolRegistrationLease>((method, _) => method.Name switch
			{
				"get_Name" => name,
				"get_Address" => address,
				"get_IsReleased" => Released,
				"get_RequiresManualRecovery" => Released && !Outcome.IsComplete && !Outcome.IsRetryable,
				"get_LastReleaseOutcome" => Released ? Outcome : null,
				"Release" => Release(),
				_ => throw new NotSupportedException($"Unexpected lease call {method.Name}.")
			});
		}

		internal ISymbolRegistrationLease Proxy
		{
			get;
		}

		internal LeaseReleaseOutcome Outcome
		{
			get;
			set;
		} = new(LeaseReleaseKind.Released, CheatEngineHostEffect.Completed);

		internal int Releases
		{
			get;
			private set;
		}

		private bool Released
		{
			get;
			set;
		}

		private LeaseReleaseOutcome Release()
		{
			Releases++;
			Released = !Outcome.IsRetryable;
			return Outcome;
		}
	}
}
