using CheatEngine.Client.Inspection;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tests.Tools.Modules;
using CheatEngine.Mcp.Tools.Symbol;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Mcp.Tests.Tools.Symbol;

/// <summary>Prepared navigation over the bounded global registered-symbol copy.</summary>
public sealed class PreparedSymbolNavigationTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public void ListPreparedRegistered_WithoutPreparation_IsInvalidState()
	{
		Harness harness = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			harness.Tools.ListPreparedRegistered(cancellationToken: Token));

		Assert.Equal(ToolErrorKind.InvalidState, exception.Error.Kind);
		Assert.Contains("symbol_list_registered", exception.Error.Hint, StringComparison.Ordinal);
		Assert.Empty(harness.Target.LuaSources);
	}

	[Fact]
	public void ListPreparedRegistered_ExpiresAtFiveSeconds()
	{
		ManualTimeProvider time = new();
		Harness harness = new(time);
		harness.Serve(Symbols("one"));
		harness.Tools.ListRegistered(cancellationToken: Token);
		time.Advance(TimeSpan.FromSeconds(5));

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			harness.Tools.ListPreparedRegistered(cancellationToken: Token));

		Assert.Equal(ToolErrorKind.InvalidState, exception.Error.Kind);
	}

	[Fact]
	public void FilteredExplicitList_PreparesTheUnfilteredResourceProjectionWithoutLua()
	{
		Harness harness = new();
		harness.Serve(Symbols("alpha", "beta"));

		RegisteredSymbolList filtered = harness.Tools.ListRegistered("beta", cancellationToken: Token);
		int luaCalls = harness.Target.LuaSources.Count;
		RegisteredSymbolList prepared = harness.Tools.ListPreparedRegistered(cancellationToken: Token);

		Assert.Equal("beta", Assert.Single(filtered.Symbols).Name);
		Assert.Collection(prepared.Symbols, symbol => Assert.Equal("alpha", symbol.Name),
			symbol => Assert.Equal("beta", symbol.Name));
		Assert.Equal(luaCalls, harness.Target.LuaSources.Count);
	}

	[Fact]
	public void NestedLaterPublication_WinsOverAnEarlierCopyThatCompletesAfterIt()
	{
		Harness harness = new();
		harness.Serve(Symbols("first"));
		harness.Target.BeforeLua = () =>
		{
			harness.Target.BeforeLua = null;
			harness.Serve(Symbols("second"));
			harness.Tools.ListRegistered(cancellationToken: Token);
			harness.Serve(Symbols("first"));
		};

		harness.Tools.ListRegistered(cancellationToken: Token);

		Assert.Equal("second", Assert.Single(harness.Tools.ListPreparedRegistered(cancellationToken: Token).Symbols).Name);
	}

	[Fact]
	public void ListPreparedRegistered_PagesAndPreservesTheCopyTruncationFlag()
	{
		Harness harness = new();
		harness.Serve(new LuaRegisteredSymbols(
		[
			new LuaRegisteredSymbol("one", "1000"), new LuaRegisteredSymbol("two", "2000"),
			new LuaRegisteredSymbol("three", "3000")
		], 9000, true));
		harness.Tools.ListRegistered(cancellationToken: Token);

		RegisteredSymbolList page = harness.Tools.ListPreparedRegistered(offset: 1, limit: 1, cancellationToken: Token);

		Assert.Equal((3, true, 2), (page.Total, page.Truncated, page.NextOffset));
		Assert.Equal("two", Assert.Single(page.Symbols).Name);
	}

	[Fact]
	public void PreparedCopy_IsDetachedFromExplicitAndPreparedCallers()
	{
		Harness harness = new();
		harness.Serve(Symbols("one"));
		RegisteredSymbolList explicitList = harness.Tools.ListRegistered(cancellationToken: Token);
		explicitList.Symbols[0] = explicitList.Symbols[0] with
		{
			Name = "mutated-explicit"
		};
		RegisteredSymbolList prepared = harness.Tools.ListPreparedRegistered(cancellationToken: Token);
		prepared.Symbols[0] = prepared.Symbols[0] with
		{
			Name = "mutated-prepared"
		};

		Assert.Equal("one", Assert.Single(harness.Tools.ListPreparedRegistered(cancellationToken: Token).Symbols).Name);
	}

	[Fact]
	public void PreparedProjection_ReevaluatesOwnershipAfterResourceRelease()
	{
		Harness harness = new();
		harness.Serve(Symbols("playerBase"));
		harness.Tools.Register("playerBase", "game.exe+10", cancellationToken: Token);
		harness.Tools.ListRegistered(cancellationToken: Token);

		harness.Target.Dispatch.Run("runtime_release_resources", token => harness.Resources.ReleaseAll(token), Token);

		RegisteredSymbolEntry symbol = Assert.Single(harness.Tools.ListPreparedRegistered(cancellationToken: Token).Symbols);
		Assert.False(symbol.OwnedByMcp);
		Assert.Null(symbol.ResourceId);
	}

	[Fact]
	public void PreparedRegistrationNavigation_DoesNotRequireAnAttachedTarget()
	{
		Harness harness = new();
		harness.Target.Attached = false;
		harness.Serve(Symbols("global"));

		harness.Tools.ListRegistered(cancellationToken: Token);

		Assert.Equal("global", Assert.Single(harness.Tools.ListPreparedRegistered(cancellationToken: Token).Symbols).Name);
		Assert.Equal(0, harness.Target.Calls("GetCurrentProcess"));
	}

	private static LuaRegisteredSymbols Symbols(params string[] names)
	{
		return new LuaRegisteredSymbols([.. names.Select(static name => new LuaRegisteredSymbol(name, "1000"))], names.Length,
			false);
	}

	private sealed class Harness
	{
		internal Harness(TimeProvider? time = null)
		{
			Target.Addresses["game.exe+10"] = 0x140000010;
			Target.Register = registration => new FakeLease(registration.Name, registration.Address).Lease;
			Tools = new SymbolRegistrationTools(Target.Dispatch, Resources, Registrations, time);
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

		internal void Serve(LuaRegisteredSymbols symbols)
		{
			Target.LuaResults[typeof(LuaRegisteredSymbols)] = symbols;
		}
	}

	private sealed class FakeLease
	{
		internal FakeLease(string name, Address address)
		{
			Lease = ClientTestDouble.Create<ISymbolRegistrationLease>((method, _) => method.Name switch
			{
				"get_Name" => name,
				"get_Address" => address,
				"get_IsReleased" => Released,
				"get_RequiresManualRecovery" => false,
				"get_LastReleaseOutcome" => Released ? new LeaseReleaseOutcome(LeaseReleaseKind.Released,
					CheatEngineHostEffect.Completed) : null,
				"Release" => Release(),
				_ => throw new NotSupportedException($"Unexpected lease call {method.Name}.")
			});
		}

		internal ISymbolRegistrationLease Lease
		{
			get;
		}

		private bool Released
		{
			get;
			set;
		}

		private LeaseReleaseOutcome Release()
		{
			Released = true;
			return new LeaseReleaseOutcome(LeaseReleaseKind.Released, CheatEngineHostEffect.Completed);
		}
	}
}
