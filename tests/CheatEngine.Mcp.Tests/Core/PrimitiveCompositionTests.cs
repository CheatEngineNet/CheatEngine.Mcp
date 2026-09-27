using System.ComponentModel;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tests.Support;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tests.Core;

public sealed class PrimitiveCompositionTests
{
	[Fact]
	public void Catalog_ToolResourceAndPromptTypes_ListsMetadataWithoutConstructingThem()
	{
		CheatEngineMcpPrimitiveOptions manifest = CheatEngineMcpComposition.CreateManifest(CheatEngineMcpMode.Catalog,
			static builder => builder.AddToolType<ThrowingTool>().AddResourceType<ThrowingResource>()
				.AddPromptType<ThrowingPrompt>().AddJsonTypeInfoResolver(TestJsonContext.Default));

		McpPrimitiveCatalog catalog = McpPrimitiveCatalog.Create(manifest);

		Assert.Equal(CheatEngineToolNames.RuntimeGetInfo, Assert.Single(catalog.Tools).Name);
		Assert.Equal("cheatengine://instance/probe", Assert.Single(catalog.Resources).Uri);
		Assert.Equal(McpPrimitiveRouting.Instance, Assert.Single(catalog.InstanceResources).Routing);
		Assert.Empty(catalog.ResourceTemplates);
		Assert.Equal("probe_prompt", Assert.Single(catalog.Prompts).Name);
	}

	[Fact]
	public void Manifest_RepeatedDeclarations_AreKeptOnceInOrder()
	{
		CheatEngineMcpPrimitiveOptions manifest = CheatEngineMcpComposition.CreateManifest(CheatEngineMcpMode.Backend,
			static builder => builder.AddToolType<ThrowingTool>().AddPromptType<ThrowingPrompt>()
				.AddToolType<ThrowingTool>());

		Assert.Equal([typeof(ThrowingTool), typeof(ThrowingPrompt)],
			manifest.Primitives.Select(static primitive => primitive.Type));
	}

	[Fact]
	public void Catalog_DuplicateToolNames_FailsInsteadOfDroppingOne()
	{
		CheatEngineMcpPrimitiveOptions manifest = CheatEngineMcpComposition.CreateManifest(CheatEngineMcpMode.Catalog,
			static builder => builder.AddToolType<ThrowingTool>().AddToolType<DuplicateTool>()
				.AddJsonTypeInfoResolver(TestJsonContext.Default));

		OptionsValidationException exception =
			Assert.Throws<OptionsValidationException>(() => McpPrimitiveCatalog.Create(manifest));
		Assert.Contains(CheatEngineToolNames.RuntimeGetInfo, exception.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void ActivationTargets_AreBorrowedAndNeverOwnedByTheMcpHost()
	{
		CountingTool.Reset();
		ServiceCollection activation = new();
		new CheatEngineMcpBuilder(activation, CheatEngineMcpMode.Backend).AddToolType<CountingTool>()
			.AddJsonTypeInfoResolver(TestJsonContext.Default);
		activation.AddOptions<CheatEngineMcpPrimitiveOptions>();
		using (ServiceProvider root =
			   activation.BuildServiceProvider(new ServiceProviderOptions
			   {
				   ValidateOnBuild = true,
				   ValidateScopes = true
			   }))
		{
			CheatEngineMcpPrimitiveOptions manifest =
				root.GetRequiredService<IOptions<CheatEngineMcpPrimitiveOptions>>().Value;
			using (IServiceScope scope = root.CreateScope())
			{
				McpPrimitiveTargets targets = McpPrimitiveTargets.Resolve(scope.ServiceProvider, manifest);
				Assert.Equal(1, CountingTool.Created);
				ServiceCollection transport = new();
				transport.AddLogging();
				transport.AddMcpServer().WithCheatEnginePrimitives(manifest, McpPrimitiveBinding.FromTargets(targets));
				using (ServiceProvider host =
					   transport.BuildServiceProvider(new ServiceProviderOptions
					   {
						   ValidateOnBuild = true,
						   ValidateScopes = true
					   }))
				{
					Assert.Equal(2, host.GetServices<McpServerTool>().Count());
					Assert.Null(host.GetService<CountingTool>());
				}

				Assert.Equal(1, CountingTool.Created);
				Assert.Equal(0, CountingTool.Disposed);
			}

			Assert.Equal(1, CountingTool.Disposed);
		}
	}

	[McpServerToolType]
	public sealed class ThrowingTool
	{
		private readonly string _marker = "probe:";

		public ThrowingTool()
		{
			throw new InvalidOperationException("Catalog composition must not construct primitive types.");
		}

		[McpServerTool(Name = CheatEngineToolNames.RuntimeGetInfo, Title = "Get composition probe", ReadOnly = true,
			Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
		[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
		[Description("Returns a probe result without constructing the catalog primitive.")]
		public ContractProbeResult Probe([Description("A value.")] string value)
		{
			return new ContractProbeResult(_marker + value, [], false, 1, null);
		}
	}

	[McpServerToolType]
	public sealed class DuplicateTool
	{
		[McpServerTool(Name = CheatEngineToolNames.RuntimeGetInfo, Title = "Get duplicate probe", ReadOnly = true,
			Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
		[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
		[Description("Returns a duplicate v2 probe result.")]
		public static ContractProbeResult Probe()
		{
			return new ContractProbeResult("duplicate", [], false, 1, null);
		}
	}

	[McpServerResourceType]
	public sealed class ThrowingResource
	{
		private readonly string _marker = "probe";

		public ThrowingResource()
		{
			throw new InvalidOperationException("Catalog composition must not construct primitive types.");
		}

		[McpServerResource(UriTemplate = "cheatengine://instance/probe", Name = "probe", Title = "Probe",
			MimeType = "application/json")]
		[Description("Probe resource.")]
		public string Read()
		{
			return _marker;
		}
	}

	[McpServerPromptType]
	public sealed class ThrowingPrompt
	{
		private readonly string _marker = "probe:";

		public ThrowingPrompt()
		{
			throw new InvalidOperationException("Catalog composition must not construct primitive types.");
		}

		// Prompts are static (Local): the catalog never needs, and never builds, an instance for them.
		[McpServerPrompt(Name = "probe_prompt", Title = "Probe prompt")]
		[Description("Probe prompt.")]
		public static string Prompt([Description("A topic.")] string topic)
		{
			return "probe:" + topic;
		}

		internal string Marker()
		{
			return _marker;
		}
	}

	[McpServerToolType]
	public sealed class CountingTool : IDisposable
	{
		private static int created;
		private static int disposed;
		private readonly string _marker = "counting:";

		public CountingTool()
		{
			Interlocked.Increment(ref created);
		}

		internal static int Created => Volatile.Read(ref created);
		internal static int Disposed => Volatile.Read(ref disposed);

		public void Dispose()
		{
			Interlocked.Increment(ref disposed);
		}

		internal static void Reset()
		{
			Volatile.Write(ref created, 0);
			Volatile.Write(ref disposed, 0);
		}

		[McpServerTool(Name = CheatEngineToolNames.RuntimeGetInfo, Title = "Get first counting probe", ReadOnly = true,
			Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
		[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
		[Description("Returns the first counting probe result.")]
		public ContractProbeResult First()
		{
			return new ContractProbeResult(_marker + "first", [], false, 1, null);
		}

		[McpServerTool(Name = CheatEngineToolNames.RuntimeGetOverview, Title = "Get second counting probe",
			ReadOnly = true,
			Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
		[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
		[Description("Returns the second counting probe result.")]
		public ContractProbeResult Second()
		{
			return new ContractProbeResult(_marker + "second", [], false, 1, null);
		}
	}
}
