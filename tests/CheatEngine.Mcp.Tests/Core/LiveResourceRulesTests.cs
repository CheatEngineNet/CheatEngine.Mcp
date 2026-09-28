using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Tests.Support;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tests.Core;

/// <summary>
///     The Core support of live resources: the source-tool rule and its <c>_meta</c>, resource annotations, the read
///     gate filter, the resource error mapping and the URI variable parsing.
/// </summary>
public sealed class LiveResourceRulesTests
{
	private const string SourceMetaKey = McpSourceToolAttribute.MetaKey;

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Theory]
	[InlineData(typeof(UnsourcedLive), "must name the read-only tool it projects with [McpSourceTool]")]
	[InlineData(typeof(MutatingSource), "projects 'memory_write', which must be read-only and closed-world")]
	[InlineData(typeof(OpenWorldSource), "projects 'process_list', which must be read-only and closed-world")]
	[InlineData(typeof(GatedSource), "requires target_code_execution, kernel_access; a resource read must never need")]
	[InlineData(typeof(HostScanSource), "projects 'aob_find', whose dispatch class is host_scan")]
	[InlineData(typeof(UnknownSource), "'module_list', which SourceProbeTools does not declare exactly once")]
	[InlineData(typeof(UnfrozenSource), "'runtime_get_probe', which is not a backend name of the frozen v2 catalog")]
	[InlineData(typeof(SourcedDocument), "is served locally, so it must not declare a source tool")]
	[InlineData(typeof(GatedDocument), "where no feature gate exists, so it must not require a feature switch")]
	[InlineData(typeof(MemoryOverlap), "match the same URI")]
	[InlineData(typeof(EmptyAnnotations), "declares annotations without an audience, a priority or a lastModified")]
	[InlineData(typeof(OutOfRangePriority), "needs an annotation priority from 0 to 1")]
	[InlineData(typeof(LocalLastModified), "needs an ISO 8601 lastModified with an offset")]
	public void Validator_InvalidLiveResourceOrAnnotation_FailsStartup(Type container, string expected)
	{
		CheatEngineMcpPrimitiveOptions manifest = new();
		manifest.Add(new CheatEngineMcpPrimitive(CheatEngineMcpPrimitiveKind.Resource, container));

		OptionsValidationException exception =
			Assert.Throws<OptionsValidationException>(() => McpPrimitiveCatalog.Create(manifest));

		Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void Validator_ServerWithToolsButNotTheSourceTool_FailsStartup()
	{
		CheatEngineMcpPrimitiveOptions manifest = CheatEngineMcpComposition.CreateManifest(CheatEngineMcpMode.Catalog,
			static builder => builder.AddToolType<OtherServedTool>().AddResourceType<ValidLive>()
				.AddJsonTypeInfoResolver(TestJsonContext.Default));

		OptionsValidationException exception =
			Assert.Throws<OptionsValidationException>(() => McpPrimitiveCatalog.Create(manifest));

		Assert.Contains("projects 'runtime_get_info', which this server does not serve", exception.Message,
			StringComparison.Ordinal);
	}

	[Fact]
	public void Catalog_LiveResource_PublishesItsSourceToolAndTheQueryTemplateMatchesInOrderOnly()
	{
		McpPrimitiveCatalog catalog = McpPrimitiveCatalog.Create(CheatEngineMcpComposition.CreateManifest(
			CheatEngineMcpMode.Catalog, static builder => builder.AddToolType<ServedSourceTool>()
				.AddResourceType<ValidLive>().AddJsonTypeInfoResolver(TestJsonContext.Default)));

		McpCatalogResource live = Assert.Single(catalog.InstanceResources);
		Assert.Equal("cheatengine://instance/pages{?offset,limit}", live.Template.UriTemplate);
		Assert.Equal(CheatEngineToolNames.RuntimeGetInfo, live.Template.Meta![SourceMetaKey]!.GetValue<string>());
		Assert.True(live.IsTemplated);
		Assert.Null(live.Resource);
		Assert.True(live.IsMatch("cheatengine://instance/pages"));
		Assert.True(live.IsMatch("cheatengine://instance/pages?offset=1&limit=2"));
		Assert.True(live.IsMatch("cheatengine://instance/pages?limit=2"));
		Assert.False(live.IsMatch("cheatengine://instance/pages?limit=2&offset=1"));
		Assert.False(live.IsMatch("cheatengine://instance/pages?other=1"));
	}

	[Fact]
	public void ResourceSource_GatedTool_ResolvesItsSwitchesAnnotationsAndDispatchClass()
	{
		MethodInfo method = typeof(GatedSource).GetMethod(nameof(GatedSource.Read))!;

		McpResourceSource source = Assert.IsType<McpResourceSource>(McpResourceSource.Resolve(method));

		Assert.Equal(CheatEngineToolNames.KernelReadPhysical, source.ToolName);
		Assert.Equal(typeof(GatedProbeTool).GetMethod(nameof(GatedProbeTool.Read)), source.Method);
		Assert.Equal([McpFeature.TargetCodeExecution, McpFeature.KernelAccess], source.Requires);
		Assert.Equal((true, false, McpDispatchClass.Short), (source.ReadOnly, source.OpenWorld, source.DispatchClass));
		Assert.Null(McpResourceSource.Resolve(typeof(UnsourcedLive).GetMethod(nameof(UnsourcedLive.Read))!));
	}

	[Fact]
	public void Catalog_Annotations_AreListedOnResourcesAndTemplates()
	{
		McpPrimitiveCatalog catalog = McpPrimitiveCatalog.Create(CheatEngineMcpComposition.CreateManifest(
			CheatEngineMcpMode.Catalog, static builder => builder.AddResourceType<AnnotatedDocuments>()));

		Annotations document = Assert.Single(catalog.Resources).Annotations!;
		Assert.Equal([Role.Assistant, Role.User], document.Audience);
		Assert.Equal(1f, document.Priority);
		Assert.Equal(new DateTimeOffset(2026, 9, 27, 0, 0, 0, TimeSpan.Zero), document.LastModified);
		Annotations template = Assert.Single(catalog.ResourceTemplates).Annotations!;
		Assert.Equal([Role.Assistant], template.Audience);
		Assert.Equal(0.7f, template.Priority);
		Assert.Null(template.LastModified);
		// The concrete entry is listed from its own copy, never from the template.
		McpCatalogResource entry = catalog.LocalResources.Single(static resource => resource.Resource is not null);
		Assert.NotSame(entry.Resource!.Annotations, entry.Template.Annotations);
	}

	[Fact]
	public void LocalPrimitives_AddTo_PublishesTheAnnotations()
	{
		McpServerOptions options = new();
		using ServiceProvider services = new ServiceCollection().BuildServiceProvider();

		McpLocalPrimitives.AddTo(options, CheatEngineMcpComposition.CreateManifest(CheatEngineMcpMode.Catalog,
			static builder => builder.AddResourceType<AnnotatedDocuments>()), services);

		McpServerResource document = options.ResourceCollection!.Single(static resource => !resource.IsTemplated);
		Assert.Equal([Role.Assistant, Role.User], document.ProtocolResource!.Annotations!.Audience);
		McpServerResource template = options.ResourceCollection!.Single(static resource => resource.IsTemplated);
		Assert.Equal(0.7f, template.ProtocolResourceTemplate.Annotations!.Priority);
	}

	[Fact]
	public async Task ListResourcesAndTemplates_AnnotationsAndSourceTool_TravelOverTheWire()
	{
		CheatEngineMcpPrimitiveOptions manifest = CheatEngineMcpComposition.CreateManifest(CheatEngineMcpMode.Catalog,
			static builder => builder.AddToolType<ServedSourceTool>().AddResourceType<AnnotatedDocuments>()
				.AddResourceType<ValidLive>().AddJsonTypeInfoResolver(TestJsonContext.Default));
		await using TestMcpPipeline pipeline = await TestMcpPipeline.StartAsync(manifest, McpPrimitiveBinding.Catalog);

		IList<McpClientResource> resources = await pipeline.Client.ListResourcesAsync(cancellationToken: Token);
		IList<McpClientResourceTemplate> templates =
			await pipeline.Client.ListResourceTemplatesAsync(cancellationToken: Token);

		Annotations document = Assert.Single(resources).ProtocolResource.Annotations!;
		Assert.Equal([Role.Assistant, Role.User], document.Audience);
		Assert.Equal(1f, document.Priority);
		Assert.Equal(new DateTimeOffset(2026, 9, 27, 0, 0, 0, TimeSpan.Zero), document.LastModified);
		ResourceTemplate live = templates.Single(static template =>
			template.UriTemplate == "cheatengine://instance/pages{?offset,limit}").ProtocolResourceTemplate;
		Assert.Equal([Role.Assistant], live.Annotations!.Audience);
		Assert.Equal(0.3f, live.Annotations.Priority);
		Assert.Equal(CheatEngineToolNames.RuntimeGetInfo, live.Meta![SourceMetaKey]!.GetValue<string>());
	}

	[Fact]
	public async Task ReadResource_DisabledSwitch_IsRefusedBeforeTheBodyRuns()
	{
		await using GatedResourceActivation activation =
			await GatedResourceActivation.StartAsync(new McpFeatureOptions { EnableKernelAccess = false });

		McpProtocolException exception = await Assert.ThrowsAsync<McpProtocolException>(async () =>
			await activation.Pipeline.Client.ReadResourceAsync(GatedLive.Uri, cancellationToken: Token));

		Assert.Equal(McpErrorCode.InternalError, exception.ErrorCode);
		Assert.Equal("capability_disabled", exception.Data["kind"]);
		Assert.Equal("not_started", exception.Data["hostEffect"]);
		Assert.Contains("Mcp:EnableKernelAccess", Assert.IsType<string>(exception.Data["hint"]),
			StringComparison.Ordinal);
		Assert.Equal(0, activation.Resource.Reads);
	}

	[Fact]
	public async Task ReadResource_EnabledSwitch_ReadsTheResource()
	{
		await using GatedResourceActivation activation =
			await GatedResourceActivation.StartAsync(new McpFeatureOptions { EnableKernelAccess = true });

		ReadResourceResult result =
			await activation.Pipeline.Client.ReadResourceAsync(GatedLive.Uri, cancellationToken: Token);

		Assert.Equal("{}", Assert.IsType<TextResourceContents>(Assert.Single(result.Contents)).Text);
		Assert.Equal(1, activation.Resource.Reads);
	}

	[Fact]
	public async Task ReadResource_GatedResourceWithoutAGate_IsRefusedAsNotStarted()
	{
		CheatEngineMcpPrimitiveOptions manifest = CheatEngineMcpComposition.CreateManifest(CheatEngineMcpMode.Catalog,
			static builder => builder.AddResourceType<GatedLive>());
		await using TestMcpPipeline pipeline = await TestMcpPipeline.StartAsync(manifest, McpPrimitiveBinding.Catalog);

		McpProtocolException exception = await Assert.ThrowsAsync<McpProtocolException>(async () =>
			await pipeline.Client.ReadResourceAsync(GatedLive.Uri, cancellationToken: Token));

		Assert.Equal(McpErrorCode.InternalError, exception.ErrorCode);
		Assert.Equal(("internal", "not_started"), (exception.Data["kind"], exception.Data["hostEffect"]));
		Assert.Contains("has no feature gate", exception.Message, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("query", McpErrorCode.InvalidParams, "invalid_argument", "not_started")]
	[InlineData("limit", McpErrorCode.InternalError, "limit_exceeded", "not_started")]
	[InlineData("overflow", McpErrorCode.InternalError, "internal", "unknown")]
	[InlineData("format", McpErrorCode.InternalError, "internal", "unknown")]
	[InlineData("json", McpErrorCode.InternalError, "internal", "unknown")]
	[InlineData("fault", McpErrorCode.InternalError, "internal", "unknown")]
	public async Task ReadResource_Failures_AreJsonRpcErrorsWithTheContractData(string kind, McpErrorCode code,
		string contractKind, string hostEffect)
	{
		CheatEngineMcpPrimitiveOptions manifest = CheatEngineMcpComposition.CreateManifest(CheatEngineMcpMode.Catalog,
			static builder => builder.AddResourceType<FailingDocument>());
		await using TestMcpPipeline pipeline = await TestMcpPipeline.StartAsync(manifest, McpPrimitiveBinding.Catalog);

		McpProtocolException exception = await Assert.ThrowsAsync<McpProtocolException>(async () =>
			await pipeline.Client.ReadResourceAsync(FailingDocument.Prefix + kind, cancellationToken: Token));

		Assert.Equal(code, exception.ErrorCode);
		Assert.Equal((contractKind, hostEffect), (exception.Data["kind"], exception.Data["hostEffect"]));
		Assert.All(exception.Data.Values.Cast<object?>(), static value => Assert.True(value is string or bool));
	}

	[Theory]
	[InlineData("overflow")]
	[InlineData("format")]
	[InlineData("json")]
	[InlineData("fault")]
	public async Task ReadResource_BodyFault_IsInternalWithTheGenericMessageLikeAToolCall(string kind)
	{
		// Resource methods take only strings, so a conversion or JSON error comes from the body, never from the caller.
		CheatEngineMcpPrimitiveOptions manifest = CheatEngineMcpComposition.CreateManifest(CheatEngineMcpMode.Catalog,
			static builder => builder.AddResourceType<FailingDocument>());
		await using TestMcpPipeline pipeline = await TestMcpPipeline.StartAsync(manifest, McpPrimitiveBinding.Catalog);

		McpProtocolException exception = await Assert.ThrowsAsync<McpProtocolException>(async () =>
			await pipeline.Client.ReadResourceAsync(FailingDocument.Prefix + kind, cancellationToken: Token));

		Assert.EndsWith(CheatEngineToolFilters.UnexpectedMessage, exception.Message, StringComparison.Ordinal);
		Assert.Equal((false, CheatEngineToolException.InternalHint),
			(exception.Data["retryable"], exception.Data["hint"]));
		Assert.DoesNotContain(FailingDocument.Secret, exception.Message, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(null, null)]
	[InlineData("0", 0)]
	[InlineData("1", 1)]
	[InlineData("007", 7)]
	[InlineData("2000", 2000)]
	public void Query_Number_ParsesAbsentOrDecimalValuesWithinTheBounds(string? value, int? expected)
	{
		Assert.Equal(expected, McpResourceQuery.Number(value, "limit", 0, 2000));
	}

	[Theory]
	[InlineData("")]
	[InlineData(" ")]
	[InlineData(" 5")]
	[InlineData("-1")]
	[InlineData("+1")]
	[InlineData("1.0")]
	[InlineData("1e3")]
	[InlineData("0x10")]
	[InlineData("abc")]
	[InlineData("2001")]
	[InlineData("99999999999")]
	[InlineData("99999999999999999999999999999999")]
	public void Query_Number_RefusesAnythingElseAsInvalidArgument(string value)
	{
		CheatEngineToolException exception =
			Assert.Throws<CheatEngineToolException>(() => McpResourceQuery.Number(value, "limit", 0, 2000));

		Assert.Equal((ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal("limit: must be a decimal integer from 0 to 2000.", exception.Error.Message);
		Assert.Equal("Omit limit to use the default.", exception.Error.Hint);
		Assert.Equal("limit", exception.Error.Details!.Value.GetProperty("parameter").GetString());
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("-3")]
	[InlineData("abc")]
	public void Query_RequiredNumber_RefusesAMissingOrMalformedValue(string? value)
	{
		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			McpResourceQuery.RequiredNumber(value, "recordId", 0, int.MaxValue));

		Assert.Equal(ToolErrorKind.InvalidArgument, exception.Error.Kind);
		Assert.StartsWith("recordId: must be a decimal integer from 0 to", exception.Error.Message,
			StringComparison.Ordinal);
		Assert.Equal(42, McpResourceQuery.RequiredNumber("42", "recordId", 0, int.MaxValue));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	public void Query_Required_RefusesAMissingOrBlankPathValue(string? value)
	{
		CheatEngineToolException exception =
			Assert.Throws<CheatEngineToolException>(() => McpResourceQuery.Required(value, "module"));

		Assert.Equal(ToolErrorKind.InvalidArgument, exception.Error.Kind);
		Assert.Equal("module: is required in the resource URI.", exception.Error.Message);
		Assert.Equal("game.exe", McpResourceQuery.Required("game.exe", "module"));
	}

	[Fact]
	public void Query_CanonicalUri_EncodesPathValuesAndKeepsSuppliedQueryValuesInOrder()
	{
		const string path = "cheatengine://instance/memory/";

		Assert.Equal("%5Bgame.exe%2B10%5D%2B8%20a%2Fb", McpResourceQuery.Segment("[game.exe+10]+8 a/b"));
		Assert.Equal(path + "x", McpResourceQuery.WithQuery(path + "x", ("offset", null), ("limit", null)));
		Assert.Equal(path + "x?limit=5", McpResourceQuery.WithQuery(path + "x", ("offset", null), ("limit", 5)));
		Assert.Equal(path + "x?offset=0&limit=5",
			McpResourceQuery.WithQuery(path + "x", ("offset", 0), ("limit", 5)));
	}

	/// <summary>The read-only, closed-world, ungated and short tool the valid live probes project.</summary>
	[McpServerToolType]
	public sealed class ServedSourceTool
	{
		[McpServerTool(Name = CheatEngineToolNames.RuntimeGetInfo, Title = "Get source probe", ReadOnly = true,
			Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
		[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
		[Description("Returns a probe result.")]
		public static ContractProbeResult Read()
		{
			return new ContractProbeResult("read", [], false, 1, null);
		}
	}

	/// <summary>Tools the invalid live probes name; no catalog serves them.</summary>
	[McpServerToolType]
	public sealed class SourceProbeTools
	{
		[McpServerTool(Name = CheatEngineToolNames.MemoryWrite, Title = "Write source probe", ReadOnly = false,
			Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
		[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
		[Description("Pretends to write.")]
		public static ContractProbeResult Write()
		{
			return new ContractProbeResult("write", [], false, 1, null);
		}

		[McpServerTool(Name = CheatEngineToolNames.ProcessList, Title = "List source probes", ReadOnly = true,
			Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
		[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
		[Description("Pretends to read outside the instance.")]
		public static ContractProbeResult OpenWorld()
		{
			return new ContractProbeResult("open", [], false, 1, null);
		}

		[McpServerTool(Name = CheatEngineToolNames.AobFind, Title = "Find source probes", ReadOnly = true,
			Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
		[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.HostScan)]
		[Description("Pretends to scan the host.")]
		public static ContractProbeResult Scan()
		{
			return new ContractProbeResult("scan", [], false, 1, null);
		}

		[McpServerTool(Name = "runtime_get_probe", Title = "Get unfrozen probe", ReadOnly = true,
			Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
		[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
		[Description("A name outside the frozen catalog.")]
		public static ContractProbeResult Unfrozen()
		{
			return new ContractProbeResult("unfrozen", [], false, 1, null);
		}
	}

	/// <summary>A served tool that is not the probes' source tool.</summary>
	[McpServerToolType]
	public sealed class OtherServedTool
	{
		[McpServerTool(Name = CheatEngineToolNames.RuntimeGetOverview, Title = "Get other probe", ReadOnly = true,
			Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
		[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
		[Description("Returns a probe result.")]
		public static ContractProbeResult Read()
		{
			return new ContractProbeResult("other", [], false, 1, null);
		}
	}

	[McpServerResourceType]
	public sealed class ValidLive
	{
		[McpServerResource(UriTemplate = "cheatengine://instance/pages{?offset,limit}", Name = "instance_pages",
			Title = "Live pages", MimeType = "application/json")]
		[McpSourceTool(typeof(ServedSourceTool), CheatEngineToolNames.RuntimeGetInfo)]
		[McpResourceAnnotations(Role.Assistant, Priority = 0.3)]
		[Description("A page of probes.")]
		public string Read(string? offset = null, string? limit = null)
		{
			return $"{{\"offset\":\"{offset}\",\"limit\":\"{limit}\",\"type\":\"{GetType().Name}\"}}";
		}
	}

	[McpServerResourceType]
	public sealed class UnsourcedLive
	{
		[McpServerResource(UriTemplate = "cheatengine://instance/unsourced", Name = "instance_unsourced",
			Title = "Unsourced", MimeType = "application/json")]
		[Description("A live resource without a source tool.")]
		public string Read()
		{
			return GetType().Name;
		}
	}

	[McpServerResourceType]
	public sealed class MutatingSource
	{
		[McpServerResource(UriTemplate = "cheatengine://instance/mutating", Name = "instance_mutating",
			Title = "Mutating", MimeType = "application/json")]
		[McpSourceTool(typeof(SourceProbeTools), CheatEngineToolNames.MemoryWrite)]
		[Description("Projects a mutating tool.")]
		public string Read()
		{
			return GetType().Name;
		}
	}

	[McpServerResourceType]
	public sealed class OpenWorldSource
	{
		[McpServerResource(UriTemplate = "cheatengine://instance/open-world", Name = "instance_open_world",
			Title = "Open world", MimeType = "application/json")]
		[McpSourceTool(typeof(SourceProbeTools), CheatEngineToolNames.ProcessList)]
		[Description("Projects an open-world tool.")]
		public string Read()
		{
			return GetType().Name;
		}
	}

	[McpServerResourceType]
	public sealed class GatedSource
	{
		[McpServerResource(UriTemplate = "cheatengine://instance/gated", Name = "instance_gated", Title = "Gated",
			MimeType = "application/json")]
		[McpSourceTool(typeof(GatedProbeTool), CheatEngineToolNames.KernelReadPhysical)]
		[Description("Projects a gated tool.")]
		public string Read()
		{
			return GetType().Name;
		}
	}

	[McpServerResourceType]
	public sealed class HostScanSource
	{
		[McpServerResource(UriTemplate = "cheatengine://instance/host-scan", Name = "instance_host_scan",
			Title = "Host scan", MimeType = "application/json")]
		[McpSourceTool(typeof(SourceProbeTools), CheatEngineToolNames.AobFind)]
		[Description("Projects a host scan.")]
		public string Read()
		{
			return GetType().Name;
		}
	}

	[McpServerResourceType]
	public sealed class UnknownSource
	{
		[McpServerResource(UriTemplate = "cheatengine://instance/unknown", Name = "instance_unknown",
			Title = "Unknown", MimeType = "application/json")]
		[McpSourceTool(typeof(SourceProbeTools), CheatEngineToolNames.ModuleList)]
		[Description("Names a tool its type does not declare.")]
		public string Read()
		{
			return GetType().Name;
		}
	}

	[McpServerResourceType]
	public sealed class UnfrozenSource
	{
		[McpServerResource(UriTemplate = "cheatengine://instance/unfrozen", Name = "instance_unfrozen",
			Title = "Unfrozen", MimeType = "application/json")]
		[McpSourceTool(typeof(SourceProbeTools), "runtime_get_probe")]
		[Description("Names a tool outside the frozen catalog.")]
		public string Read()
		{
			return GetType().Name;
		}
	}

	[McpServerResourceType]
	public sealed class SourcedDocument
	{
		[McpServerResource(UriTemplate = "cheatengine://docs/sourced", Name = "doc_sourced", Title = "Sourced",
			MimeType = "text/markdown")]
		[McpSourceTool(typeof(ServedSourceTool), CheatEngineToolNames.RuntimeGetInfo)]
		[Description("A document that claims a source tool.")]
		public static string Read()
		{
			return "x";
		}
	}

	/// <summary><c>memory/{address}</c> matches <c>memory/regions</c>, so the region map cannot stay there.</summary>
	[McpServerResourceType]
	public sealed class MemoryOverlap
	{
		[McpServerResource(UriTemplate = "cheatengine://instance/memory/{address}{?size}", Name = "instance_memory",
			Title = "Memory", MimeType = "application/json")]
		[McpSourceTool(typeof(ServedSourceTool), CheatEngineToolNames.RuntimeGetInfo)]
		[Description("Bytes at an address.")]
		public string Bytes(string address, string? size = null)
		{
			return address + size + GetType().Name;
		}

		[McpServerResource(UriTemplate = "cheatengine://instance/memory/regions", Name = "instance_memory_regions",
			Title = "Regions", MimeType = "application/json")]
		[McpSourceTool(typeof(ServedSourceTool), CheatEngineToolNames.RuntimeGetInfo)]
		[Description("The region map.")]
		public string Regions()
		{
			return GetType().Name;
		}
	}

	[McpServerResourceType]
	public sealed class EmptyAnnotations
	{
		[McpServerResource(UriTemplate = "cheatengine://docs/empty-annotations", Name = "doc_empty_annotations",
			Title = "Empty annotations", MimeType = "text/markdown")]
		[McpResourceAnnotations]
		[Description("Annotations that set nothing.")]
		public static string Read()
		{
			return "x";
		}
	}

	[McpServerResourceType]
	public sealed class OutOfRangePriority
	{
		[McpServerResource(UriTemplate = "cheatengine://docs/priority", Name = "doc_priority", Title = "Priority",
			MimeType = "text/markdown")]
		[McpResourceAnnotations(Role.User, Priority = 1.5)]
		[Description("A priority above one.")]
		public static string Read()
		{
			return "x";
		}
	}

	[McpServerResourceType]
	public sealed class LocalLastModified
	{
		[McpServerResource(UriTemplate = "cheatengine://docs/last-modified", Name = "doc_last_modified",
			Title = "Last modified", MimeType = "text/markdown")]
		[McpResourceAnnotations(Role.User, LastModified = "2026-09-27T00:00:00")]
		[Description("A time without an offset.")]
		public static string Read()
		{
			return "x";
		}
	}

	[McpServerResourceType]
	public sealed class AnnotatedDocuments
	{
		[McpServerResource(UriTemplate = "cheatengine://docs/annotated", Name = "doc_annotated", Title = "Annotated",
			MimeType = "text/markdown")]
		[McpResourceAnnotations(Role.Assistant, Role.User, Priority = 1.0, LastModified = "2026-09-27T00:00:00Z")]
		[Description("An annotated document.")]
		public static string Document()
		{
			return "# Annotated";
		}

		[McpServerResource(UriTemplate = "cheatengine://docs/annotated/{name}", Name = "doc_annotated_named",
			Title = "Annotated template", MimeType = "text/markdown")]
		[McpResourceAnnotations(Role.Assistant, Priority = 0.7)]
		[Description("An annotated template.")]
		public static string Named(string name)
		{
			return name;
		}
	}

	[McpServerResourceType]
	public sealed class GatedDocument
	{
		[McpServerResource(UriTemplate = "cheatengine://docs/gated", Name = "doc_gated", Title = "Gated document",
			MimeType = "text/markdown")]
		[RequiresFeature(McpFeature.UnsafeLua)]
		[Description("A document behind a switch.")]
		public static string Read()
		{
			return "x";
		}
	}

	[McpServerResourceType]
	public sealed class FailingDocument
	{
		internal const string Prefix = "cheatengine://docs/failing/";
		internal const string Secret = "failing-document-secret";

		[McpServerResource(UriTemplate = Prefix + "{kind}", Name = "doc_failing", Title = "Failing document",
			MimeType = "text/markdown")]
		[Description("Raises the failure its kind names.")]
		public static string Read(string kind)
		{
			return kind switch
			{
				"overflow" => int.Parse("99999999999", CultureInfo.InvariantCulture)
					.ToString(CultureInfo.InvariantCulture),
				"format" => int.Parse("twelve", CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
				"json" => throw new JsonException(Secret),
				"fault" => throw new InvalidOperationException(Secret),
				"query" => McpResourceQuery.Number("abc", "limit", 1, 10)?.ToString(CultureInfo.InvariantCulture) ??
						   string.Empty,
				_ => throw CheatEngineToolException.LimitExceeded("module", "is too large.")
			};
		}
	}

	/// <summary>A live resource behind the kernel switch, counting the reads that reached its body.</summary>
	[McpServerResourceType]
	public sealed class GatedLive
	{
		internal const string Uri = "cheatengine://instance/gated";
		private int _reads;

		internal int Reads => Volatile.Read(ref _reads);

		[McpServerResource(UriTemplate = Uri, Name = "instance_gated", Title = "Gated live",
			MimeType = "application/json")]
		[McpSourceTool(typeof(ServedSourceTool), CheatEngineToolNames.RuntimeGetInfo)]
		[RequiresFeature(McpFeature.KernelAccess)]
		[Description("A live resource behind the kernel switch.")]
		public string Read()
		{
			Interlocked.Increment(ref _reads);
			return "{}";
		}
	}

	private sealed class GatedResourceActivation : IAsyncDisposable
	{
		private readonly ServiceProvider _root;
		private readonly AsyncServiceScope _scope;

		private GatedResourceActivation(ServiceProvider root, AsyncServiceScope scope, GatedLive resource,
			TestMcpPipeline pipeline)
		{
			_root = root;
			_scope = scope;
			Resource = resource;
			Pipeline = pipeline;
		}

		internal GatedLive Resource
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

		internal static async Task<GatedResourceActivation> StartAsync(McpFeatureOptions features)
		{
			ServiceCollection activation = new();
			activation.AddSingleton(ClientTestDouble.Client(new RecordingDispatcher().Dispatcher,
				CancellationToken.None));
			activation.AddLogging();
			activation.AddSingleton(Options.Create(features));
			new CheatEngineMcpBuilder(activation, CheatEngineMcpMode.Backend).AddExecutionServices()
				.AddResourceType<GatedLive>();
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
			Assert.NotNull(targets.Gate);
			TestMcpPipeline pipeline =
				await TestMcpPipeline.StartAsync(manifest, McpPrimitiveBinding.FromTargets(targets));
			return new GatedResourceActivation(root, scope, (GatedLive) targets.Get(typeof(GatedLive)), pipeline);
		}
	}
}
