using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using System.Text;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tests.Support;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tests.Core;

/// <summary>
///     Resources and prompts: Local versus Instance routing, the metadata marker, the URI grammar and the startup rules.
/// </summary>
public sealed class PrimitiveRoutingTests
{
	[Fact]
	public void Catalog_StaticMembers_AreLocal_InstanceMembers_AreInstance()
	{
		McpPrimitiveCatalog catalog = McpPrimitiveCatalog.Create(Manifest(static builder => builder
			.AddResourceType<MixedResources>().AddPromptType<LocalPrompts>()));

		Assert.Equal(["cheatengine://docs/probe", "cheatengine://docs/probes/{name}"],
			catalog.LocalResources.Select(static entry => entry.Template.UriTemplate));
		Assert.Equal(["cheatengine://instance/probes/{name}"],
			catalog.InstanceResources.Select(static entry => entry.Template.UriTemplate));
		Assert.Equal(["cheatengine://docs/probe"], catalog.Resources.Select(static resource => resource.Uri));
		Assert.Equal(["cheatengine://docs/probes/{name}", "cheatengine://instance/probes/{name}"],
			catalog.ResourceTemplates.Select(static template => template.UriTemplate));
		Assert.True(catalog.InstanceResources[0].IsMatch("cheatengine://instance/probes/one"));
		Assert.False(catalog.InstanceResources[0].IsMatch("cheatengine://instance/other/one"));
		Assert.Equal("probe_prompt", Assert.Single(catalog.Prompts).Name);
	}

	[Fact]
	public void LocalConcreteResource_Listing_PublishesItsUtf8Size()
	{
		McpPrimitiveCatalog catalog =
			McpPrimitiveCatalog.Create(Manifest(static builder => builder.AddResourceType<MixedResources>()));

		Assert.Equal(Encoding.UTF8.GetByteCount(MixedResources.Text), Assert.Single(catalog.Resources).Size);
	}

	[Fact]
	public void Metadata_PreservesSdkItemsAndAddsOneOrigin()
	{
		using ServiceProvider provider = Serve(Manifest(static builder => builder.AddResourceType<MixedResources>()
			.AddPromptType<LocalPrompts>()));

		foreach (IMcpServerPrimitive primitive in provider.GetServices<McpServerResource>()
					 .Concat<IMcpServerPrimitive>(provider.GetServices<McpServerPrompt>()))
		{
			MethodInfo method = Assert.IsAssignableFrom<MethodInfo>(primitive.Metadata[0]);
			McpPrimitiveOrigin origin = Assert.Single(primitive.Metadata.OfType<McpPrimitiveOrigin>());
			Assert.Equal(method.IsStatic ? McpPrimitiveRouting.Local : McpPrimitiveRouting.Instance, origin.Routing);
			Assert.Contains(primitive.Metadata, static item => item is DescriptionAttribute);
			Assert.Same(origin, McpPrimitiveOrigin.Of(primitive));
		}
	}

	[Fact]
	public void Manifest_StaticOnlyContainer_IsNotRegisteredForActivation()
	{
		ServiceCollection services = new();
		new CheatEngineMcpBuilder(services, CheatEngineMcpMode.Backend).AddResourceType<StaticOnlyResources>()
			.AddResourceType<MixedResources>().AddPromptType<LocalPrompts>();

		Assert.DoesNotContain(services, static descriptor => descriptor.ServiceType == typeof(StaticOnlyResources));
		Assert.DoesNotContain(services, static descriptor => descriptor.ServiceType == typeof(LocalPrompts));
		Assert.Contains(services, static descriptor => descriptor.ServiceType == typeof(MixedResources));
	}

	[Fact]
	public void LocalPrimitives_AddTo_ServesOnlyStaticMembersWithTheErrorFilters()
	{
		CheatEngineMcpPrimitiveOptions manifest = Manifest(static builder => builder
			.AddResourceType<MixedResources>().AddPromptType<LocalPrompts>());
		McpServerOptions options = new();
		using ServiceProvider services = new ServiceCollection().BuildServiceProvider();

		McpLocalPrimitives.AddTo(options, manifest, services);

		Assert.Equal(["cheatengine://docs/probe", "cheatengine://docs/probes/{name}"],
			options.ResourceCollection!.Select(static resource => resource.ProtocolResourceTemplate.UriTemplate)
				.Order(StringComparer.Ordinal));
		Assert.Equal("probe_prompt", Assert.Single(options.PromptCollection!).ProtocolPrompt.Name);
		Assert.Single(options.Filters.Request.ReadResourceFilters);
		Assert.Single(options.Filters.Request.GetPromptFilters);
		Assert.Single(options.Filters.Request.CompleteFilters);
	}

	[Fact]
	public void PromptArguments_EveryHost_ListTheDisplayNameAsTitle()
	{
		CheatEngineMcpPrimitiveOptions manifest = Manifest(static builder => builder.AddPromptType<LocalPrompts>());
		McpPrimitiveCatalog catalog = McpPrimitiveCatalog.Create(manifest);
		using ServiceProvider backend = Serve(manifest);
		McpServerOptions gateway = new();
		using ServiceProvider services = new ServiceCollection().BuildServiceProvider();

		McpLocalPrimitives.AddTo(gateway, manifest, services);

		PromptArgument listed = Assert.Single(Assert.Single(catalog.Prompts).Arguments!);
		Assert.Equal(("topic", "Topic", "A topic."), (listed.Name, listed.Title, listed.Description));
		Assert.Equal("Topic",
			Assert.Single(Assert.Single(backend.GetServices<McpServerPrompt>()).ProtocolPrompt.Arguments!).Title);
		Assert.Equal("Topic", Assert.Single(Assert.Single(gateway.PromptCollection!).ProtocolPrompt.Arguments!).Title);
	}

	[Theory]
	[InlineData(typeof(StaticOutsideDocs), "must be under cheatengine://docs/")]
	[InlineData(typeof(InstanceOutsideInstance), "must be under cheatengine://instance/")]
	[InlineData(typeof(GatewayReserved), "which the gateway reserves")]
	[InlineData(typeof(UpperCaseSegment), "neither kebab-case nor one {variable}")]
	[InlineData(typeof(PascalVariable), "not camelCase")]
	[InlineData(typeof(RoutingVariable), "only the gateway uses for routing")]
	[InlineData(typeof(DefaultMime), "must declare the MIME type text/markdown explicitly")]
	[InlineData(typeof(NumericParameter), "only strings are allowed")]
	[InlineData(typeof(UnboundVariable), "which no string parameter receives")]
	[InlineData(typeof(Untitled), "has no title")]
	[InlineData(typeof(Overlapping), "match the same URI")]
	public void Validator_InvalidResource_FailsStartup(Type container, string expected)
	{
		CheatEngineMcpPrimitiveOptions manifest = new();
		manifest.Add(new CheatEngineMcpPrimitive(CheatEngineMcpPrimitiveKind.Resource, container));

		OptionsValidationException exception =
			Assert.Throws<OptionsValidationException>(() => McpPrimitiveCatalog.Create(manifest));

		Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(typeof(InstancePrompt), "must be a static method")]
	[InlineData(typeof(UndescribedArgument), "has no description")]
	[InlineData(typeof(ToolNamedPrompt), "has the name of a tool")]
	[InlineData(typeof(CamelNamedPrompt), "must match")]
	[InlineData(typeof(RoutingArgumentPrompt), "only the gateway uses for routing")]
	[InlineData(typeof(NumericArgumentPrompt), "only strings are allowed")]
	[InlineData(typeof(UntitledArgument), "argument 'topic' has no title; declare [Display(Name = ...)]")]
	[InlineData(typeof(LowercaseArgumentTitle), "argument 'topic' needs a sentence-case title")]
	[InlineData(typeof(RepeatedArgumentTitle), "argument 'other' repeats the title 'Topic' of 'topic'")]
	public void Validator_InvalidPrompt_FailsStartup(Type container, string expected)
	{
		CheatEngineMcpPrimitiveOptions manifest = new();
		manifest.Add(new CheatEngineMcpPrimitive(CheatEngineMcpPrimitiveKind.Prompt, container));

		OptionsValidationException exception =
			Assert.Throws<OptionsValidationException>(() => McpPrimitiveCatalog.Create(manifest));

		Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("cheatengine://docs/value-scans", true, "")]
	[InlineData("cheatengine://docs/workflows/{workflow}", true, "workflow")]
	[InlineData("cheatengine://instance/modules{?offset,limit}", true, "offset,limit")]
	[InlineData("cheatengine://instance/memory/{address}{?size}", true, "address,size")]
	[InlineData("cheatengine://instance/modules/{module}/exports", true, "module")]
	[InlineData("cheatengine://docs/Value-Scans", false, "")]
	[InlineData("cheatengine://docs/value_scans", false, "")]
	[InlineData("cheatengine://docs/x{y}", false, "")]
	[InlineData("cheatengine://docs", false, "")]
	[InlineData("cheatengine://docs/{a}/{a}", false, "")]
	[InlineData("cheatengine://instance/modules{?offset}/x", false, "")]
	[InlineData("resource://docs/value-scans", false, "")]
	public void Grammar_Template_ParsesOnlyKebabSegmentsAndCamelVariables(string template, bool valid,
		string variables)
	{
		Assert.Equal(valid, McpContractRules.TryParseTemplate(template, out IReadOnlyList<string> parsed, out _));
		if (valid)
		{
			Assert.Equal(variables, string.Join(',', parsed));
		}
	}

	[Fact]
	public void ResourceUris_GatewayForm_IsAPrefixSwapBothWays()
	{
		const string instanceId = "ce-1234-0123456789abcdef0123456789abcdef";

		string gateway = McpResourceUris.ToGateway("cheatengine://instance/modules/game.exe", instanceId);

		Assert.Equal($"cheatengine://instances/{instanceId}/modules/game.exe", gateway);
		Assert.True(McpResourceUris.TryParseGateway(gateway, out string? parsedId, out string? backend));
		Assert.Equal(instanceId, parsedId);
		Assert.Equal("cheatengine://instance/modules/game.exe", backend);
		Assert.Equal("cheatengine://instances/{instanceId}/modules{?offset,limit}",
			McpResourceUris.ToGateway("cheatengine://instance/modules{?offset,limit}", "{instanceId}"));
		Assert.Equal(gateway, McpResourceUris.RewriteContentUri("cheatengine://instance/modules/game.exe", instanceId));
		Assert.Equal("cheatengine://docs/safety", McpResourceUris.RewriteContentUri("cheatengine://docs/safety",
			instanceId));
	}

	[Fact]
	public void ResourceUris_GatewayInstances_IsOnlyTheAdvertisedUri()
	{
		Assert.True(McpResourceUris.IsGatewayInstances(McpResourceUris.GatewayInstances));
		Assert.False(McpResourceUris.IsGatewayInstances(McpResourceUris.GatewayInstancesPrefix));
	}

	[Theory]
	[InlineData("cheatengine://instances")]
	[InlineData("cheatengine://instances/ce-1-0123456789abcdef0123456789abcdef")]
	[InlineData("cheatengine://instances/ce-1-0123456789abcdef0123456789abcdef/")]
	[InlineData("cheatengine://instances/ce-1-0123456789ABCDEF0123456789ABCDEF/runtime")]
	[InlineData("cheatengine://instances/ce--0123456789abcdef0123456789abcdef/runtime")]
	[InlineData("cheatengine://instances/ce-12345678901-0123456789abcdef0123456789abcdef/runtime")]
	[InlineData("cheatengine://instances/other/runtime")]
	[InlineData("cheatengine://instance/runtime")]
	public void ResourceUris_MalformedGatewayUri_IsNotParsed(string uri)
	{
		Assert.False(McpResourceUris.TryParseGateway(uri, out _, out _));
	}

	[Theory]
	[InlineData(false, "See [safety](safety.md) and [x](../Workflows/find-writer.md#steps).",
		"See [safety](cheatengine://docs/safety) and [x](cheatengine://docs/workflows/find-writer#steps).")]
	[InlineData(true, "See [a](../Documents/debugger.md), [b](nop-patch.md) and [c](./trace-logic.md).",
		"See [a](cheatengine://docs/debugger), [b](cheatengine://docs/workflows/nop-patch) and " +
		"[c](cheatengine://docs/workflows/trace-logic).")]
	[InlineData(false, "Keep [web](https://example.com/a.md), [up](../README.md) and [deep](a/b/c.md).",
		"Keep [web](https://example.com/a.md), [up](../README.md) and [deep](a/b/c.md).")]
	[InlineData(true, "Keep [old](../workflows/nop-patch.md) and [out](../../README.md).",
		"Keep [old](../workflows/nop-patch.md) and [out](../../README.md).")]
	[InlineData(false, "Keep [root](/guide.md) and [cdn](//example.com/guide.md).",
		"Keep [root](/guide.md) and [cdn](//example.com/guide.md).")]
	public void KnowledgeText_RelativeLinks_BecomeDocsUris(bool inWorkflows, string markdown, string expected)
	{
		Assert.Equal(expected, McpKnowledgeText.RewriteLinks(markdown, inWorkflows));
	}

	private static CheatEngineMcpPrimitiveOptions Manifest(Action<ICheatEngineMcpBuilder> compose)
	{
		return CheatEngineMcpComposition.CreateManifest(CheatEngineMcpMode.Catalog, compose);
	}

	private static ServiceProvider Serve(CheatEngineMcpPrimitiveOptions manifest)
	{
		ServiceCollection services = new();
		services.AddLogging();
		services.AddMcpServer().WithCheatEnginePrimitives(manifest, McpPrimitiveBinding.Catalog);
		return services.BuildServiceProvider();
	}

	[McpServerResourceType]
	public sealed class MixedResources
	{
		internal const string Text = "# Probe\n\nNon-ASCII text: é ü.";

		[McpServerResource(UriTemplate = "cheatengine://docs/probe", Name = "doc_probe", Title = "Probe document",
			MimeType = "text/markdown")]
		[Description("A static document.")]
		public static string Document()
		{
			return Text;
		}

		[McpServerResource(UriTemplate = "cheatengine://docs/probes/{name}", Name = "doc_probes",
			Title = "Probe documents", MimeType = "text/markdown")]
		[Description("A static template.")]
		public static string Documents(string name)
		{
			return name;
		}

		[McpServerResource(UriTemplate = "cheatengine://instance/probes/{name}", Name = "instance_probes",
			Title = "Live probes", MimeType = "application/json")]
		[McpSourceTool(typeof(RoutingProbeTool), CheatEngineToolNames.RuntimeGetInfo)]
		[Description("A live template.")]
		public string Live(string name)
		{
			return $"{{\"name\":\"{name}\",\"type\":\"{GetType().Name}\"}}";
		}
	}

	/// <summary>The read-only, short tool the live probe projects; the probe catalogs never serve it.</summary>
	[McpServerToolType]
	public sealed class RoutingProbeTool
	{
		[McpServerTool(Name = CheatEngineToolNames.RuntimeGetInfo, Title = "Get routing probe", ReadOnly = true,
			Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
		[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
		[Description("Returns a probe result.")]
		public static ContractProbeResult Read()
		{
			return new ContractProbeResult("routing", [], false, 1, null);
		}
	}

	[McpServerResourceType]
	public sealed class StaticOnlyResources
	{
		[McpServerResource(UriTemplate = "cheatengine://docs/static-only", Name = "doc_static_only",
			Title = "Static only", MimeType = "text/markdown")]
		[Description("A static document.")]
		public static string Document()
		{
			return "static";
		}
	}

	[McpServerPromptType]
	public sealed class LocalPrompts
	{
		[McpServerPrompt(Name = "probe_prompt", Title = "Probe prompt")]
		[Description("A static prompt.")]
		public static string Prompt([Description("A topic.")][Display(Name = "Topic")] string topic,
			McpServer? server = null)
		{
			return topic + (server is null ? string.Empty : ".");
		}
	}

	[McpServerResourceType]
	public sealed class StaticOutsideDocs
	{
		[McpServerResource(UriTemplate = "cheatengine://instance/static", Name = "wrong_space", Title = "Wrong",
			MimeType = "application/json")]
		[Description("Static but in the live space.")]
		public static string Read()
		{
			return "{}";
		}
	}

	[McpServerResourceType]
	public sealed class InstanceOutsideInstance
	{
		[McpServerResource(UriTemplate = "cheatengine://docs/live", Name = "wrong_space", Title = "Wrong",
			MimeType = "text/markdown")]
		[Description("Live but in the docs space.")]
		public string Read()
		{
			return GetType().Name;
		}
	}

	[McpServerResourceType]
	public sealed class GatewayReserved
	{
		[McpServerResource(UriTemplate = "cheatengine://instances", Name = "reserved", Title = "Reserved",
			MimeType = "application/json")]
		[Description("The gateway's own URI.")]
		public string Read()
		{
			return GetType().Name;
		}
	}

	[McpServerResourceType]
	public sealed class UpperCaseSegment
	{
		[McpServerResource(UriTemplate = "cheatengine://docs/Value-Scans", Name = "upper", Title = "Upper",
			MimeType = "text/markdown")]
		[Description("A segment in capitals.")]
		public static string Read()
		{
			return "x";
		}
	}

	[McpServerResourceType]
	public sealed class PascalVariable
	{
		[McpServerResource(UriTemplate = "cheatengine://docs/probes/{Name}", Name = "pascal", Title = "Pascal",
			MimeType = "text/markdown")]
		[Description("A PascalCase variable.")]
		public static string Read(string name)
		{
			return name;
		}
	}

	[McpServerResourceType]
	public sealed class RoutingVariable
	{
		[McpServerResource(UriTemplate = "cheatengine://instance/probes/{instanceId}", Name = "routing",
			Title = "Routing", MimeType = "application/json")]
		[Description("The gateway's routing variable.")]
		public string Read(string instanceId)
		{
			return instanceId + GetType().Name;
		}
	}

	[McpServerResourceType]
	public sealed class DefaultMime
	{
		[McpServerResource(UriTemplate = "cheatengine://docs/no-mime", Name = "no_mime", Title = "No MIME")]
		[Description("No MIME type.")]
		public static string Read()
		{
			return "x";
		}
	}

	[McpServerResourceType]
	public sealed class NumericParameter
	{
		[McpServerResource(UriTemplate = "cheatengine://docs/probes/{count}", Name = "numeric", Title = "Numeric",
			MimeType = "text/markdown")]
		[Description("A numeric variable.")]
		public static string Read(int count)
		{
			return count.ToString(CultureInfo.InvariantCulture);
		}
	}

	[McpServerResourceType]
	public sealed class UnboundVariable
	{
		[McpServerResource(UriTemplate = "cheatengine://docs/probes/{name}", Name = "unbound", Title = "Unbound",
			MimeType = "text/markdown")]
		[Description("A variable without a parameter.")]
		public static string Read()
		{
			return "x";
		}
	}

	[McpServerResourceType]
	public sealed class Untitled
	{
		[McpServerResource(UriTemplate = "cheatengine://docs/untitled", Name = "untitled",
			MimeType = "text/markdown")]
		[Description("No title.")]
		public static string Read()
		{
			return "x";
		}
	}

	[McpServerResourceType]
	public sealed class Overlapping
	{
		[McpServerResource(UriTemplate = "cheatengine://docs/{slug}", Name = "any_doc", Title = "Any",
			MimeType = "text/markdown")]
		[Description("Every document.")]
		public static string Any(string slug)
		{
			return slug;
		}

		[McpServerResource(UriTemplate = "cheatengine://docs/safety", Name = "safety_doc", Title = "Safety",
			MimeType = "text/markdown")]
		[Description("One document the template also matches.")]
		public static string Safety()
		{
			return "x";
		}
	}

	[McpServerPromptType]
	public sealed class InstancePrompt
	{
		[McpServerPrompt(Name = "instance_prompt", Title = "Instance prompt")]
		[Description("An instance method.")]
		public string Prompt([Description("A topic.")][Display(Name = "Topic")] string topic)
		{
			return topic + GetType().Name;
		}
	}

	[McpServerPromptType]
	public sealed class UndescribedArgument
	{
		[McpServerPrompt(Name = "undescribed", Title = "Undescribed")]
		[Description("An argument without a description.")]
		public static string Prompt([Display(Name = "Topic")] string topic)
		{
			return topic;
		}
	}

	[McpServerPromptType]
	public sealed class ToolNamedPrompt
	{
		[McpServerPrompt(Name = "memory_read", Title = "Tool named")]
		[Description("A frozen tool name.")]
		public static string Prompt([Description("A topic.")][Display(Name = "Topic")] string topic)
		{
			return topic;
		}
	}

	[McpServerPromptType]
	public sealed class CamelNamedPrompt
	{
		[McpServerPrompt(Name = "findWriter", Title = "Camel named")]
		[Description("A camelCase name.")]
		public static string Prompt([Description("A topic.")][Display(Name = "Topic")] string topic)
		{
			return topic;
		}
	}

	[McpServerPromptType]
	public sealed class RoutingArgumentPrompt
	{
		[McpServerPrompt(Name = "routing_prompt", Title = "Routing prompt")]
		[Description("The gateway's routing argument.")]
		public static string Prompt([Description("An instance.")] string instanceId)
		{
			return instanceId;
		}
	}

	[McpServerPromptType]
	public sealed class UntitledArgument
	{
		[McpServerPrompt(Name = "untitled_argument", Title = "Untitled argument")]
		[Description("An argument without a title.")]
		public static string Prompt([Description("A topic.")] string topic)
		{
			return topic;
		}
	}

	[McpServerPromptType]
	public sealed class LowercaseArgumentTitle
	{
		[McpServerPrompt(Name = "lowercase_argument_title", Title = "Lowercase argument title")]
		[Description("An argument titled in lower case.")]
		public static string Prompt([Description("A topic.")][Display(Name = "topic")] string topic)
		{
			return topic;
		}
	}

	[McpServerPromptType]
	public sealed class RepeatedArgumentTitle
	{
		[McpServerPrompt(Name = "repeated_argument_title", Title = "Repeated argument title")]
		[Description("Two arguments with one title.")]
		public static string Prompt([Description("A topic.")][Display(Name = "Topic")] string topic,
			[Description("Another topic.")][Display(Name = "Topic")] string other)
		{
			return topic + other;
		}
	}

	[McpServerPromptType]
	public sealed class NumericArgumentPrompt
	{
		[McpServerPrompt(Name = "numeric_prompt", Title = "Numeric prompt")]
		[Description("A numeric argument.")]
		public static string Prompt([Description("A count.")] int count)
		{
			return count.ToString(CultureInfo.InvariantCulture);
		}
	}
}
