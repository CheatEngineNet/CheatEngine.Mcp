using System.Collections.Concurrent;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;

using CheatEngine.Client;
using CheatEngine.Client.Results;
using CheatEngine.Client.Scanning;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Resources.Live;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tests.Tools.Modules;
using CheatEngine.Mcp.Tests.Tools.Pointer;
using CheatEngine.Mcp.Tests.Tools.Structures;
using CheatEngine.Mcp.Tools.Modules;
using CheatEngine.Mcp.Tools.Runtime;
using CheatEngine.Mcp.Tools.Scan;
using CheatEngine.Mcp.Tools.Structures;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tests.Resources;

/// <summary>
///     The completion of live resource templates on a backend: which variables complete, from which container, and how
///     the handler bounds, caches, rate-limits and times out their listings so a keystroke never fails or blocks.
/// </summary>
public sealed class LiveCompletionTests
{
	private const string Instance = McpResourceUris.InstancePrefix;

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public void Catalog_LiveTemplates_CompleteOnlyTheCheapBoundedPathVariables()
	{
		McpPrimitiveCatalog catalog = McpPrimitiveCatalog.Create(TestComposition.BackendManifest);

		string[] completed =
		[
			.. catalog.InstanceResources.SelectMany(static resource =>
					VariablesOf(resource.Template.UriTemplate).Where(resource.IsCompletedByInstance)
						.Select(variable => resource.Template.UriTemplate + " " + variable))
				.Order(StringComparer.Ordinal)
		];

		Assert.Equal(
		[
			Instance + "modules/{module} module",
			Instance + "modules/{module}/exports{?offset,limit} module",
			Instance + "pointer-scans/{scanName}/paths{?offset,limit} scanName",
			Instance + "scanners/{scannerName} scannerName",
			Instance + "structures/{structure}{?offset,limit} structure"
		], completed);
		// Record ids expire and addresses are free-form expressions: neither is offered.
		McpCatalogResource record = catalog.InstanceResources.Single(static resource =>
			resource.Template.UriTemplate == Instance + "records/{recordId}");
		Assert.False(record.IsCompletedByInstance("recordId"));
		Assert.All(catalog.InstanceResources, static resource => Assert.All(
			VariablesOf(resource.Template.UriTemplate), variable => Assert.Empty(resource.AllowedValues(variable))));
	}

	[Fact]
	public void Catalog_ProbeTemplate_ReportsItsAllowedValuesAndCompletedVariables()
	{
		McpPrimitiveCatalog catalog = McpPrimitiveCatalog.Create(ProbeManifest());

		McpCatalogResource details =
			catalog.InstanceResources.Single(static resource => resource.Template.UriTemplate == Probe.ItemDetails);

		Assert.Equal(["concise", "detailed"], details.AllowedValues("format"));
		Assert.Empty(details.AllowedValues("item"));
		Assert.True(details.IsCompletedByInstance("item"));
		Assert.False(details.IsCompletedByInstance("format"));
		Assert.False(details.IsCompletedByInstance("missing"));
	}

	[Theory]
	[InlineData(typeof(LocalMarked), "must not mark 'slug' with [McpCompletion]")]
	[InlineData(typeof(QueryMarked), "which only a path variable may carry")]
	[InlineData(typeof(BothMarkers), "both [AllowedValues] and [McpCompletion]")]
	[InlineData(typeof(NoSource), "does not implement IMcpCompletionSource")]
	public void Validator_InvalidCompletionMarker_FailsStartup(Type container, string expected)
	{
		CheatEngineMcpPrimitiveOptions manifest = new();
		manifest.Add(new CheatEngineMcpPrimitive(CheatEngineMcpPrimitiveKind.Resource, container));

		OptionsValidationException exception =
			Assert.Throws<OptionsValidationException>(() => McpPrimitiveCatalog.Create(manifest));

		Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Complete_MemoryVariable_IsListedForEveryRequestAndMatchedByPrefixIgnoringCase()
	{
		await using ProbeHost host = ProbeHost.Create();
		host.Listings.Set("item", "Alpha", "alpine", "Beta", "alpha", "", "Alpha");

		Completion first = await host.CompleteAsync(Probe.Items, "item", "AL");
		Completion all = await host.CompleteAsync(Probe.ItemDetails, "item", "");

		Assert.Equal(["Alpha", "alpine", "alpha"], first.Values);
		Assert.Equal((3, false), (first.Total, first.HasMore));
		Assert.Equal(["Alpha", "alpine", "Beta", "alpha"], all.Values);
		Assert.Equal(2, host.Listings.Calls("item"));
	}

	[Fact]
	public async Task Complete_ManyValues_OffersTheFirstHundredWithTotalAndHasMore()
	{
		await using ProbeHost host = ProbeHost.Create();
		host.Listings.Set("item", [.. Enumerable.Range(0, 150).Select(static index => $"v{index:D3}")]);

		Completion everything = await host.CompleteAsync(Probe.Items, "item", "V");
		Completion narrowed = await host.CompleteAsync(Probe.Items, "item", "v14");

		Assert.Equal(McpCompletions.MaximumValues, everything.Values.Count);
		Assert.Equal("v099", everything.Values[^1]);
		Assert.Equal((150, true), (everything.Total, everything.HasMore));
		Assert.Equal([.. Enumerable.Range(140, 10).Select(static index => $"v{index:D3}")], narrowed.Values);
		Assert.Equal((10, false), (narrowed.Total, narrowed.HasMore));
	}

	[Fact]
	public async Task Complete_UnmarkedVariableUnknownTemplateOrPrompt_OffersNothingWithoutListing()
	{
		await using ProbeHost host = ProbeHost.Create();
		host.Listings.Set("item", "alpha");
		host.Listings.Set("name", "alpha");

		Completion unmarked = await host.CompleteAsync(Probe.NameIds, "id", "a");
		Completion allowed = await host.CompleteAsync(Probe.ItemDetails, "format", "c");
		Completion unknown = await host.CompleteAsync(Instance + "probe-items/{other}", "item", "a");
		CompleteResult prompt = await host.Completions.CompleteAsync(new CompleteRequestParams
		{
			Ref = new PromptReference { Name = "find_writer" },
			Argument = new Argument { Name = "item", Value = "a" }
		}, Token);

		Assert.Empty(unmarked.Values);
		// The SDK appends [AllowedValues] after this handler, so the handler itself offers nothing for them.
		Assert.Empty(allowed.Values);
		Assert.Empty(unknown.Values);
		Assert.Empty(prompt.Completion.Values);
		Assert.Equal(0, host.Listings.Calls("item") + host.Listings.Calls("name") + host.Listings.Calls("id"));
	}

	[Fact]
	public async Task Complete_DispatchVariable_IsCachedForItsTimeToLiveAcrossTheTemplatesThatShareIt()
	{
		await using ProbeHost host = ProbeHost.Create();
		host.Listings.Set("name", "game.exe", "gdi32.dll");

		Completion first = await host.CompleteAsync(Probe.Names, "name", "g");
		Completion shared = await host.CompleteAsync(Probe.NameIds, "name", "gd");
		host.Time.Advance(McpResourceCompletions.TimeToLive - TimeSpan.FromMilliseconds(1));
		Completion cached = await host.CompleteAsync(Probe.Names, "name", "GAME");
		int beforeExpiry = host.Listings.Calls("name");
		host.Time.Advance(TimeSpan.FromMilliseconds(2));
		host.Listings.Set("name", "game.exe", "gdi32.dll", "gfx.dll");
		Completion refreshed = await host.CompleteAsync(Probe.Names, "name", "g");

		Assert.Equal(["game.exe", "gdi32.dll"], first.Values);
		Assert.Equal(["gdi32.dll"], shared.Values);
		Assert.Equal(["game.exe"], cached.Values);
		Assert.Equal(1, beforeExpiry);
		Assert.Equal(["game.exe", "gdi32.dll", "gfx.dll"], refreshed.Values);
		Assert.Equal(2, host.Listings.Calls("name"));
	}

	[Fact]
	public async Task Complete_FailedDispatchListing_OffersNothingAndIsRetriedAtMostOncePerInterval()
	{
		await using ProbeHost host = ProbeHost.Create();
		host.Listings.Fail("name", CheatEngineToolException.Busy("4 dispatches are running.", "Repeat later."));

		Completion failed = await host.CompleteAsync(Probe.Names, "name", "");
		Completion limited = await host.CompleteAsync(Probe.Names, "name", "");
		host.Time.Advance(McpResourceCompletions.RefreshInterval);
		host.Listings.Set("name", "game.exe");
		Completion recovered = await host.CompleteAsync(Probe.Names, "name", "");

		Assert.Empty(failed.Values);
		Assert.Empty(limited.Values);
		Assert.Equal(["game.exe"], recovered.Values);
		Assert.Equal(2, host.Listings.Calls("name"));
	}

	[Fact]
	public async Task Complete_NewerSelectionEpoch_DropsTheValuesOfAnOlderEpochButNotEpochlessOnes()
	{
		await using ProbeHost host = ProbeHost.Create();
		host.Listings.SetAt("name", 7, "old.exe");
		host.Listings.SetAt("slot", 8, "slot-1");
		host.Listings.SetAt("code", null, "Player");

		await host.CompleteAsync(Probe.Names, "name", "");
		await host.CompleteAsync(Probe.Codes, "code", "");
		Completion slot = await host.CompleteAsync(Probe.Slots, "slot", "");
		host.Listings.SetAt("name", 8, "new.exe");
		Completion relisted = await host.CompleteAsync(Probe.Names, "name", "");
		Completion code = await host.CompleteAsync(Probe.Codes, "code", "");

		Assert.Equal(["slot-1"], slot.Values);
		// Still within the time-to-live, but epoch 8 replaced epoch 7: the names are listed again.
		Assert.Equal(["new.exe"], relisted.Values);
		Assert.Equal(2, host.Listings.Calls("name"));
		// Structures-like listings belong to no target and stay cached.
		Assert.Equal(["Player"], code.Values);
		Assert.Equal(1, host.Listings.Calls("code"));
	}

	[Fact]
	public async Task Complete_ListingFromAnOlderEpoch_IsNeverOffered()
	{
		await using ProbeHost host = ProbeHost.Create();
		host.Listings.SetAt("slot", 9, "slot-1");
		host.Listings.SetAt("name", 8, "stale.exe");

		await host.CompleteAsync(Probe.Slots, "slot", "");
		Completion stale = await host.CompleteAsync(Probe.Names, "name", "");

		Assert.Empty(stale.Values);
	}

	[Fact]
	public async Task Complete_SlowDispatchListing_AnswersWithinTheWaitAndCompletesInTheBackgroundOnce()
	{
		await using ProbeHost host = ProbeHost.Create(TimeSpan.FromMilliseconds(100));
		using ManualResetEventSlim release = new();
		host.Listings.Behave("name", _ =>
		{
			release.Wait(TimeSpan.FromSeconds(30), CancellationToken.None);
			return new McpCompletionValues(["game.exe"], 3);
		});

		Stopwatch elapsed = Stopwatch.StartNew();
		Completion first = await host.CompleteAsync(Probe.Names, "name", "");
		Completion concurrent = await host.CompleteAsync(Probe.NameIds, "name", "");
		elapsed.Stop();
		release.Set();
		Completion later = new();
		for (int attempt = 0; attempt < 100 && later.Values.Count == 0; attempt++)
		{
			later = await host.CompleteAsync(Probe.Names, "name", "");
		}

		Assert.Empty(first.Values);
		Assert.Empty(concurrent.Values);
		Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(10), $"Two waits took {elapsed.Elapsed}.");
		Assert.Equal(["game.exe"], later.Values);
		Assert.Equal(1, host.Listings.Calls("name"));
	}

	[Fact]
	public async Task Complete_DispatchListingsOfDifferentVariables_NeverRunTwoAtOnce()
	{
		await using ProbeHost host = ProbeHost.Create(TimeSpan.FromMilliseconds(100));
		using ManualResetEventSlim release = new();
		host.Listings.Behave("name", _ =>
		{
			release.Wait(TimeSpan.FromSeconds(30), CancellationToken.None);
			return new McpCompletionValues(["game.exe"]);
		});
		host.Listings.Set("code", "Player");
		host.Listings.Set("item", "alpha");

		Completion blocked = await host.CompleteAsync(Probe.Names, "name", "");
		Completion refused = await host.CompleteAsync(Probe.Codes, "code", "");
		Completion memory = await host.CompleteAsync(Probe.Items, "item", "");
		int codeListingsWhileBlocked = host.Listings.Calls("code");
		release.Set();
		Completion names = await PollAsync(host, Probe.Names, "name");
		Completion codes = await PollAsync(host, Probe.Codes, "code");

		Assert.Empty(blocked.Values);
		// The name listing holds the one dispatch completions may use; a memory listing needs none.
		Assert.Empty(refused.Values);
		Assert.Equal(0, codeListingsWhileBlocked);
		Assert.Equal(["alpha"], memory.Values);
		Assert.Equal(["game.exe"], names.Values);
		// A refused variable was not rate-limited: it listed as soon as the dispatch was free.
		Assert.Equal(["Player"], codes.Values);
		Assert.Equal((1, 1), (host.Listings.Calls("name"), host.Listings.Calls("code")));
	}

	[Fact]
	public async Task Install_EveryServerOptionsOfOneContainer_SharesOneHandler()
	{
		await using ProbeHost host = ProbeHost.Create();
		IOptionsFactory<McpServerOptions> factory =
			host.Transport.GetRequiredService<IOptionsFactory<McpServerOptions>>();

		// The stateless HTTP transport creates the options again for every request.
		McpServerOptions first = factory.Create(Options.DefaultName);
		McpServerOptions second = factory.Create(Options.DefaultName);

		Assert.NotNull(first.Capabilities?.Completions);
		Assert.NotNull(second.Capabilities?.Completions);
		McpResourceCompletions shared = host.Transport.GetRequiredService<McpResourceCompletions>();
		Assert.Same(shared, first.Handlers.CompleteHandler?.Target);
		Assert.Same(shared, second.Handlers.CompleteHandler?.Target);
	}

	[Fact]
	public async Task Complete_CallerCancellation_IsRethrownWhileListingFailuresAreNot()
	{
		await using ProbeHost host = ProbeHost.Create(TimeSpan.FromSeconds(30));
		using ManualResetEventSlim release = new();
		host.Listings.Behave("name", _ =>
		{
			release.Wait(TimeSpan.FromSeconds(30), CancellationToken.None);
			return new McpCompletionValues(["game.exe"]);
		});
		host.Listings.Behave("item", static _ => throw new InvalidOperationException("broken container"));
		using CancellationTokenSource cancelled = new();
		await cancelled.CancelAsync();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
			await host.Completions.CompleteAsync(Request(Probe.Names, "name", ""), cancelled.Token));
		Completion broken = await host.CompleteAsync(Probe.Items, "item", "");
		release.Set();

		Assert.Empty(broken.Values);
	}

	[Fact]
	public void ModuleSource_ListsDistinctModuleNamesWithTheSelectionEpochInOneDispatch()
	{
		ModuleSymbolTarget target = new()
		{
			SelectionEpoch = 42
		};
		target.AddModule("game.exe", 0x140000000, 0x5000);
		target.AddModule("GAME.EXE", 0x150000000, 0x1000);
		target.AddModule("user32.dll", 0x7FF800000000, 0x1000);
		ModuleLiveResources live = new(new ModuleTools(target.Dispatch), new ModuleExportTools(target.Dispatch),
			target.Dispatch);

		McpCompletionValues listed = live.ListCompletionValues("module", Token);
		target.Attached = false;

		Assert.Equal(["game.exe", "user32.dll"], listed.Values);
		Assert.Equal(42, listed.SelectionEpoch);
		Assert.Equal(1, target.Dispatcher.Calls);
		Assert.Equal(ToolErrorKind.NotAttached, Assert.Throws<CheatEngineToolException>(() =>
			live.ListCompletionValues("module", Token)).Error.Kind);
		Assert.Throws<ArgumentOutOfRangeException>(() => live.ListCompletionValues("offset", Token));
	}

	[Fact]
	public void StructureSource_ListsTheGlobalStructureNamesWithoutAnEpoch()
	{
		StructureToolHarness harness = new()
		{
			Lua = static _ => """
							  {"structures":[{"name":"Player","size":16,"elementCount":2},
							  {"name":"Enemy","size":8,"elementCount":1}],"total":2,"truncated":false}
							  """
		};
		StructureLiveResources live = new(harness.Structures);

		McpCompletionValues listed = live.ListCompletionValues("structure", Token);

		Assert.Equal(["Player", "Enemy"], listed.Values);
		Assert.Null(listed.SelectionEpoch);
		StructureLuaCall call = Assert.Single(harness.LuaCalls);
		Assert.True(call.Runs(StructureLuaScripts.List));
		Assert.Contains("1000", call.Arguments, StringComparison.Ordinal);
	}

	[Fact]
	public void ScannerSource_ListsMainAndTheRetainedNamedScannersWithoutADispatch()
	{
		ModuleSymbolTarget target = new();
		TargetResources resources = new();
		resources.Track(new RetainedProbe(resources, "scan", "zeta"));
		resources.Track(new RetainedProbe(resources, "scan", "alpha"));
		resources.Track(new RetainedProbe(resources, "patch", "not-a-scanner"));
		resources.Track(new RetainedProbe(resources, "scan", CheatEngineToolNames.PointerFindReferences));
		resources.Track(new RetainedProbe(resources, "scan", "released", TargetResourceState.Ended));
		using ScanTools scans = new(target.Dispatch, resources);
		ScanLiveResources live = new(scans, resources);

		McpCompletionValues listed = live.ListCompletionValues("scannerName", Token);

		Assert.Equal(["main", "alpha", "zeta"], listed.Values);
		Assert.Null(listed.SelectionEpoch);
		Assert.Equal(0, target.Dispatcher.Calls);
	}

	[Fact]
	public void ScannerSource_ListsTheNamedScannersScanToolsRetains_UntilTheyAreDeleted()
	{
		TargetResources resources = new();
		IValueScanner scanner = ClientTestDouble.Create<IValueScanner>(static (method, _) =>
			method.Name == nameof(IValueScanner.CreateSession)
				? NamedScanSession()
				: throw new NotSupportedException($"Unexpected value-scanner call {method.Name}."));
		ToolDispatch dispatch = ModuleSymbolTarget.CreateDispatch(
			ClientTestDouble.Client((nameof(ICheatEngineClient.ValueScans), scanner)));
		using ScanTools scans = new(dispatch, resources);
		ScanLiveResources live = new(scans, resources);

		// Created the way scan_first creates them, so a change of how ScanTools retains a scanner fails here.
		scans.First("beta", value: "10", cancellationToken: Token);
		scans.First("alpha", value: "20", cancellationToken: Token);
		McpCompletionValues created = live.ListCompletionValues("scannerName", Token);
		scans.Delete("beta", Token);
		McpCompletionValues deleted = live.ListCompletionValues("scannerName", Token);

		Assert.Equal(["main", "alpha", "beta"], created.Values);
		Assert.Equal(["main", "alpha"], deleted.Values);
	}

	[Fact]
	public async Task PointerSource_ListsTheStoredScanNamesWithoutADispatch()
	{
		await using PointerFixture fixture = new();
		fixture.Maps.CreateMap("run1", cancellationToken: Token);
		fixture.WaitForMap("run1");
		fixture.Scans.FindPaths("hp", "run1", "21020", maxOffset: 0x40, allowNegativeOffsets: true,
			cancellationToken: Token);
		fixture.WaitForScan("hp");
		PointerLiveResources live = new(fixture.Maps, fixture.Scans);
		int dispatches = fixture.Dispatches;

		McpCompletionValues listed = live.ListCompletionValues("scanName", Token);

		Assert.Equal(["hp"], listed.Values);
		Assert.Equal(dispatches, fixture.Dispatches);
	}

	[Fact]
	public async Task CompleteOverTheWire_ModuleTemplates_OfferTheAttachedProcessModules()
	{
		await using ModuleCompletionActivation activation = await ModuleCompletionActivation.StartAsync();
		ModelContextProtocol.Client.McpClient client = activation.Pipeline.Client;

		CompleteResult module = await client.CompleteAsync(
			new ResourceTemplateReference { Uri = Instance + "modules/{module}" }, "module", "SA",
			cancellationToken: Token);
		CompleteResult exports = await client.CompleteAsync(
			new ResourceTemplateReference { Uri = Instance + "modules/{module}/exports{?offset,limit}" }, "module",
			"", cancellationToken: Token);
		CompleteResult limit = await client.CompleteAsync(
			new ResourceTemplateReference { Uri = Instance + "modules{?offset,limit}" }, "limit", "1",
			cancellationToken: Token);

		Assert.NotNull(client.ServerCapabilities.Completions);
		Assert.Equal(["sample.dll"], module.Completion.Values);
		Assert.Equal(["sample.dll", "game.exe"], exports.Completion.Values);
		Assert.Empty(limit.Completion.Values);
		// Both templates share one cached listing: one dispatch for the module names.
		Assert.Equal(1, activation.Target.Calls("TryGetModules"));
	}

	[Fact]
	public async Task CompleteOverTheWire_AllowedValuesOfALiveTemplate_AreOfferedOnceBesideDynamicValues()
	{
		await using ProbeHost host = ProbeHost.Create();
		host.Listings.Set("item", "alpha", "beta");
		await using TestMcpPipeline pipeline = await TestMcpPipeline.StartAsync(host.Manifest,
			McpPrimitiveBinding.FromTargets(host.Targets));

		CompleteResult format = await pipeline.Client.CompleteAsync(
			new ResourceTemplateReference { Uri = Probe.ItemDetails }, "format", "D", cancellationToken: Token);
		CompleteResult item = await pipeline.Client.CompleteAsync(
			new ResourceTemplateReference { Uri = Probe.ItemDetails }, "item", "b", cancellationToken: Token);

		Assert.Equal(["detailed"], format.Completion.Values);
		Assert.Equal(["beta"], item.Completion.Values);
	}

	/// <summary>A named value-scan session whose first scan finds one match and whose release completes.</summary>
	private static IValueScanSession NamedScanSession()
	{
		ValueScanSessionState state = ValueScanSessionState.Created;
		bool released = false;
		return ClientTestDouble.Create<IValueScanSession>((method, _) => method.Name switch
		{
			"get_State" => state,
			"FirstScan" => Scan(),
			"GetResultCount" => 1UL,
			"Release" => Release(),
			"get_IsReleased" => released,
			"get_RequiresManualRecovery" => false,
			_ => throw new NotSupportedException($"Unexpected session call {method.Name}.")
		});

		object? Scan()
		{
			state = ValueScanSessionState.ResultsReady;
			return null;
		}

		LeaseReleaseOutcome Release()
		{
			released = true;
			return new LeaseReleaseOutcome(LeaseReleaseKind.Released, CheatEngineHostEffect.Completed);
		}
	}

	private static async Task<Completion> PollAsync(ProbeHost host, string template, string name)
	{
		// Each request waits a little for a running listing; a slow thread-pool start only takes more requests.
		Completion completion = new();
		for (int attempt = 0; attempt < 100 && completion.Values.Count == 0; attempt++)
		{
			completion = await host.CompleteAsync(template, name, "");
		}

		return completion;
	}

	private static CompleteRequestParams Request(string template, string name, string value)
	{
		return new CompleteRequestParams
		{
			Ref = new ResourceTemplateReference { Uri = template },
			Argument = new Argument { Name = name, Value = value }
		};
	}

	private static IEnumerable<string> VariablesOf(string template)
	{
		Assert.True(McpContractRules.TryParseTemplate(template, out IReadOnlyList<string> variables, out _));
		return variables;
	}

	private static CheatEngineMcpPrimitiveOptions ProbeManifest()
	{
		return CheatEngineMcpComposition.CreateManifest(CheatEngineMcpMode.Catalog,
			static builder => builder.AddResourceType<Probe>());
	}

	/// <summary>The completion handler over the probe containers of one activation, on a manual clock.</summary>
	private sealed class ProbeHost : IAsyncDisposable
	{
		private readonly ServiceProvider _root;
		private readonly AsyncServiceScope _scope;
		private readonly ServiceProvider _transport;

		private ProbeHost(ServiceProvider root, AsyncServiceScope scope, ServiceProvider transport,
			CheatEngineMcpPrimitiveOptions manifest, McpPrimitiveTargets targets, TimeSpan? wait)
		{
			_root = root;
			_scope = scope;
			_transport = transport;
			Manifest = manifest;
			Targets = targets;
			Listings = root.GetRequiredService<ProbeListings>();
			Completions = new McpResourceCompletions(transport.GetServices<McpServerResource>(), targets, Time,
				wait ?? TimeSpan.FromSeconds(10));
		}

		internal ManualTimeProvider Time
		{
			get;
		} = new();

		internal ProbeListings Listings
		{
			get;
		}

		internal CheatEngineMcpPrimitiveOptions Manifest
		{
			get;
		}

		internal McpPrimitiveTargets Targets
		{
			get;
		}

		internal McpResourceCompletions Completions
		{
			get;
		}

		internal IServiceProvider Transport => _transport;

		public async ValueTask DisposeAsync()
		{
			await _transport.DisposeAsync();
			await _scope.DisposeAsync();
			await _root.DisposeAsync();
		}

		internal static ProbeHost Create(TimeSpan? wait = null)
		{
			ServiceCollection activation = new();
			activation.AddSingleton<ProbeListings>();
			new CheatEngineMcpBuilder(activation, CheatEngineMcpMode.Backend).AddResourceType<Probe>();
			activation.AddOptions<CheatEngineMcpPrimitiveOptions>();
			ServiceProvider root = activation.BuildServiceProvider(new ServiceProviderOptions
			{
				ValidateOnBuild = true,
				ValidateScopes = true
			});
			AsyncServiceScope scope = root.CreateAsyncScope();
			CheatEngineMcpPrimitiveOptions manifest =
				root.GetRequiredService<IOptions<CheatEngineMcpPrimitiveOptions>>().Value;
			McpPrimitiveTargets targets = McpPrimitiveTargets.Resolve(scope.ServiceProvider, manifest);
			ServiceCollection transport = new();
			transport.AddLogging();
			transport.AddMcpServer().WithCheatEnginePrimitives(manifest, McpPrimitiveBinding.FromTargets(targets));
			return new ProbeHost(root, scope, transport.BuildServiceProvider(), manifest, targets, wait);
		}

		internal async Task<Completion> CompleteAsync(string template, string name, string value)
		{
			return (await Completions.CompleteAsync(Request(template, name, value), Token)).Completion;
		}
	}

	/// <summary>The scripted listings of the probe containers, counted per variable.</summary>
	public sealed class ProbeListings
	{
		private readonly ConcurrentDictionary<string, Func<CancellationToken, McpCompletionValues>> _behaviors =
			new(StringComparer.Ordinal);

		private readonly ConcurrentDictionary<string, int> _calls = new(StringComparer.Ordinal);

		/// <summary>What every probe read returns.</summary>
		internal string Json
		{
			get;
		} = "{}";

		internal int Calls(string variable)
		{
			return _calls.GetValueOrDefault(variable);
		}

		internal void Set(string variable, params string[] values)
		{
			Behave(variable, _ => new McpCompletionValues(values));
		}

		internal void SetAt(string variable, long? epoch, params string[] values)
		{
			Behave(variable, _ => new McpCompletionValues(values, epoch));
		}

		internal void Fail(string variable, Exception failure)
		{
			Behave(variable, _ => throw failure);
		}

		internal void Behave(string variable, Func<CancellationToken, McpCompletionValues> behavior)
		{
			_behaviors[variable] = behavior;
		}

		internal McpCompletionValues List(string variable, CancellationToken cancellationToken)
		{
			_calls.AddOrUpdate(variable, 1, static (_, count) => count + 1);
			return _behaviors.TryGetValue(variable, out Func<CancellationToken, McpCompletionValues>? behavior)
				? behavior(cancellationToken)
				: throw CheatEngineToolException.NotFound($"No listing was scripted for {variable}.");
		}
	}

	/// <summary>Live probe templates whose marked variables complete from <see cref="ProbeListings" />.</summary>
	[McpServerResourceType]
	public sealed class Probe(ProbeListings listings) : IMcpCompletionSource
	{
		internal const string Items = Instance + "probe-items/{item}";
		internal const string ItemDetails = Instance + "probe-items/{item}/details{?format}";
		internal const string Names = Instance + "probe-names/{name}";
		internal const string NameIds = Instance + "probe-names/{name}/ids/{id}";
		internal const string Slots = Instance + "probe-slots/{slot}";
		internal const string Codes = Instance + "probe-codes/{code}";

		[McpServerResource(UriTemplate = Items, Name = "instance_probe_item", Title = "Probe item",
			MimeType = McpResourceUris.JsonMimeType)]
		[McpSourceTool(typeof(RuntimeTools), CheatEngineToolNames.RuntimeGetInfo)]
		[Description("A probe item.")]
		public string Item([McpCompletion(McpCompletionCost.Memory)] string item)
		{
			return listings.Json;
		}

		[McpServerResource(UriTemplate = ItemDetails, Name = "instance_probe_details", Title = "Probe details",
			MimeType = McpResourceUris.JsonMimeType)]
		[McpSourceTool(typeof(RuntimeTools), CheatEngineToolNames.RuntimeGetInfo)]
		[Description("A probe item's details.")]
		public string Details([McpCompletion(McpCompletionCost.Memory)] string item,
			[AllowedValues("concise", "detailed")] string? format = null)
		{
			return listings.Json;
		}

		[McpServerResource(UriTemplate = Names, Name = "instance_probe_name", Title = "Probe name",
			MimeType = McpResourceUris.JsonMimeType)]
		[McpSourceTool(typeof(RuntimeTools), CheatEngineToolNames.RuntimeGetInfo)]
		[Description("A probe name.")]
		public string Name([McpCompletion(McpCompletionCost.Dispatch)] string name)
		{
			return listings.Json;
		}

		[McpServerResource(UriTemplate = NameIds, Name = "instance_probe_name_id", Title = "Probe name id",
			MimeType = McpResourceUris.JsonMimeType)]
		[McpSourceTool(typeof(RuntimeTools), CheatEngineToolNames.RuntimeGetInfo)]
		[Description("A probe name's id.")]
		public string NameId([McpCompletion(McpCompletionCost.Dispatch)] string name, string id)
		{
			return listings.Json;
		}

		[McpServerResource(UriTemplate = Slots, Name = "instance_probe_slot", Title = "Probe slot",
			MimeType = McpResourceUris.JsonMimeType)]
		[McpSourceTool(typeof(RuntimeTools), CheatEngineToolNames.RuntimeGetInfo)]
		[Description("A probe slot.")]
		public string Slot([McpCompletion(McpCompletionCost.Dispatch)] string slot)
		{
			return listings.Json;
		}

		[McpServerResource(UriTemplate = Codes, Name = "instance_probe_code", Title = "Probe code",
			MimeType = McpResourceUris.JsonMimeType)]
		[McpSourceTool(typeof(RuntimeTools), CheatEngineToolNames.RuntimeGetInfo)]
		[Description("A probe code.")]
		public string Code([McpCompletion(McpCompletionCost.Dispatch)] string code)
		{
			return listings.Json;
		}

		public McpCompletionValues ListCompletionValues(string variable, CancellationToken cancellationToken)
		{
			return listings.List(variable, cancellationToken);
		}
	}

	/// <summary>A Local document that wrongly asks an instance to complete its variable.</summary>
	[McpServerResourceType]
	public sealed class LocalMarked
	{
		[McpServerResource(UriTemplate = "cheatengine://docs/marked/{slug}", Name = "doc_marked",
			Title = "Marked document", MimeType = McpResourceUris.MarkdownMimeType)]
		[Description("A marked document.")]
		public static string Read([McpCompletion(McpCompletionCost.Memory)] string slug)
		{
			return slug;
		}
	}

	/// <summary>A live template that marks a query variable.</summary>
	[McpServerResourceType]
	public sealed class QueryMarked : IMcpCompletionSource
	{
		private readonly string _json = "{}";

		[McpServerResource(UriTemplate = Instance + "marked{?name}", Name = "instance_marked", Title = "Marked",
			MimeType = McpResourceUris.JsonMimeType)]
		[McpSourceTool(typeof(RuntimeTools), CheatEngineToolNames.RuntimeGetInfo)]
		[Description("A marked query.")]
		public string Read([McpCompletion(McpCompletionCost.Memory)] string? name = null)
		{
			return _json;
		}

		public McpCompletionValues ListCompletionValues(string variable, CancellationToken cancellationToken)
		{
			return new McpCompletionValues([]);
		}
	}

	/// <summary>A live template whose variable is both allowed-valued and instance-completed.</summary>
	[McpServerResourceType]
	public sealed class BothMarkers : IMcpCompletionSource
	{
		private readonly string _json = "{}";

		[McpServerResource(UriTemplate = Instance + "marked/{name}", Name = "instance_marked", Title = "Marked",
			MimeType = McpResourceUris.JsonMimeType)]
		[McpSourceTool(typeof(RuntimeTools), CheatEngineToolNames.RuntimeGetInfo)]
		[Description("A doubly marked variable.")]
		public string Read([McpCompletion(McpCompletionCost.Memory), AllowedValues("a", "b")] string name)
		{
			return _json;
		}

		public McpCompletionValues ListCompletionValues(string variable, CancellationToken cancellationToken)
		{
			return new McpCompletionValues([]);
		}
	}

	/// <summary>A live template that marks a variable its container cannot list.</summary>
	[McpServerResourceType]
	public sealed class NoSource
	{
		private readonly string _json = "{}";

		[McpServerResource(UriTemplate = Instance + "marked/{name}", Name = "instance_marked", Title = "Marked",
			MimeType = McpResourceUris.JsonMimeType)]
		[McpSourceTool(typeof(RuntimeTools), CheatEngineToolNames.RuntimeGetInfo)]
		[Description("A variable without a source.")]
		public string Read([McpCompletion(McpCompletionCost.Memory)] string name)
		{
			return _json;
		}
	}

	/// <summary>A retained resource with a chosen kind, name and state, never released.</summary>
	private sealed class RetainedProbe(
		TargetResources resources,
		string kind,
		string name,
		TargetResourceState state = TargetResourceState.Active) : ITargetResource
	{
		public TargetResourceDescriptor Descriptor
		{
			get;
		} = new(resources.NextId(kind), kind, TargetResourceCategory.ClientLease, state, DateTimeOffset.UnixEpoch,
			name);

		public bool IsEnded => Descriptor.State is TargetResourceState.Ended;

		public bool HoldsHostState => !IsEnded;

		public ResourceReleaseOutcome Release(CancellationToken cancellationToken)
		{
			throw new NotSupportedException("The probe is never released.");
		}
	}

	/// <summary>A backend activation serving the module tools and their live resources over two modules.</summary>
	private sealed class ModuleCompletionActivation : IAsyncDisposable
	{
		private readonly ServiceProvider _root;
		private readonly AsyncServiceScope _scope;

		private ModuleCompletionActivation(ModuleSymbolTarget target, ServiceProvider root, AsyncServiceScope scope,
			TestMcpPipeline pipeline)
		{
			Target = target;
			_root = root;
			_scope = scope;
			Pipeline = pipeline;
		}

		internal ModuleSymbolTarget Target
		{
			get;
		}

		internal TestMcpPipeline Pipeline
		{
			get;
		}

		public async ValueTask DisposeAsync()
		{
			await Pipeline.DisposeAsync();
			await _scope.DisposeAsync();
			await _root.DisposeAsync();
		}

		internal static async Task<ModuleCompletionActivation> StartAsync()
		{
			ModuleSymbolTarget target = ModuleToolTests.LoadedSample();
			target.AddModule("game.exe", 0x140000000, 0x5000);
			ServiceCollection activation = new();
			activation.AddSingleton(target.Client);
			activation.AddLogging();
			new CheatEngineMcpBuilder(activation, CheatEngineMcpMode.Backend).AddExecutionServices()
				.AddJsonTypeInfoResolver(ModuleJsonContext.Default).AddToolType<ModuleTools>()
				.AddToolType<ModuleExportTools>().AddResourceType<ModuleLiveResources>();
			activation.AddOptions<CheatEngineMcpPrimitiveOptions>();
			ServiceProvider root = activation.BuildServiceProvider(new ServiceProviderOptions
			{
				ValidateOnBuild = true,
				ValidateScopes = true
			});
			AsyncServiceScope scope = root.CreateAsyncScope();
			CheatEngineMcpPrimitiveOptions manifest =
				root.GetRequiredService<IOptions<CheatEngineMcpPrimitiveOptions>>().Value;
			McpPrimitiveTargets targets = McpPrimitiveTargets.Resolve(scope.ServiceProvider, manifest);
			TestMcpPipeline pipeline =
				await TestMcpPipeline.StartAsync(manifest, McpPrimitiveBinding.FromTargets(targets));
			return new ModuleCompletionActivation(target, root, scope, pipeline);
		}
	}
}
