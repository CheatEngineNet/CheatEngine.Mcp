using System.ComponentModel;

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
				.AddPromptType<ThrowingPrompt>());

		McpPrimitiveCatalog catalog = McpPrimitiveCatalog.Create(manifest);

		Assert.Equal("composition_probe", Assert.Single(catalog.Tools).Name);
		Assert.Equal("cheatengine://probe", Assert.Single(catalog.Resources).UriTemplate);
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
			static builder => builder.AddToolType<ThrowingTool>().AddToolType<DuplicateTool>());

		OptionsValidationException exception =
			Assert.Throws<OptionsValidationException>(() => McpPrimitiveCatalog.Create(manifest));
		Assert.Contains("composition_probe", exception.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void ActivationTargets_AreBorrowedAndNeverOwnedByTheMcpHost()
	{
		CountingTool.Reset();
		ServiceCollection activation = new();
		new CheatEngineMcpBuilder(activation, CheatEngineMcpMode.Backend).AddToolType<CountingTool>();
		activation.AddOptions<CheatEngineMcpPrimitiveOptions>();
		using (ServiceProvider root =
		       activation.BuildServiceProvider(new ServiceProviderOptions
		       {
			       ValidateOnBuild = true, ValidateScopes = true
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
					       ValidateOnBuild = true, ValidateScopes = true
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

		[McpServerTool(Name = "composition_probe")]
		[Description("Probe tool.")]
		public string Probe([Description("A value.")] string value)
		{
			return _marker + value;
		}
	}

	[McpServerToolType]
	public sealed class DuplicateTool
	{
		[McpServerTool(Name = "composition_probe")]
		[Description("Duplicate probe tool.")]
		public static string Probe()
		{
			return "duplicate";
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

		[McpServerResource(UriTemplate = "cheatengine://probe", Name = "probe", MimeType = "text/plain")]
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

		[McpServerPrompt(Name = "probe_prompt")]
		[Description("Probe prompt.")]
		public string Prompt([Description("A topic.")] string topic)
		{
			return _marker + topic;
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

		[McpServerTool(Name = "counting_first")]
		[Description("First counting tool.")]
		public string First()
		{
			return _marker + "first";
		}

		[McpServerTool(Name = "counting_second")]
		[Description("Second counting tool.")]
		public string Second()
		{
			return _marker + "second";
		}
	}
}
