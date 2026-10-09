using System.ComponentModel;
using System.Reflection;

using CheatEngine.Mcp.Core.Composition;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;

using Microsoft.Extensions.Options;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tests.Core;

#pragma warning disable CA1822 // Instance method shape is part of the reflection contract under test.

/// <summary>Live resources may replace a long explicit source operation with one bounded prepared projection.</summary>
public sealed class PreparedProjectionContractTests
{
	[Fact]
	public void ValidPreparedProjection_AllowsANonShortSourceAndPublishesResolvedProvenance()
	{
		McpPrimitiveCatalog catalog = McpPrimitiveCatalog.Create(Manifest<ValidResource>());
		MethodInfo resource = typeof(ValidResource).GetMethod(nameof(ValidResource.Read))!;
		McpResourceSource source = Assert.IsType<McpResourceSource>(McpResourceSource.Resolve(resource));

		Assert.Equal(nameof(ValidTools.ReadPrepared), source.PreparedProjection!.Name);
		Assert.Equal(McpDispatchClass.Short, source.PreparedDispatchClass);
		Assert.Empty(source.PreparedRequires);
		McpCatalogResource live = Assert.Single(catalog.InstanceResources);
		Assert.Equal(nameof(ValidTools.ReadPrepared),
			live.Template.Meta![McpSourceToolAttribute.PreparedProjectionMetaKey]!.GetValue<string>());
	}

	[Theory]
	[InlineData(typeof(MissingProjectionResource), "does not declare exactly once as a public instance method")]
	[InlineData(typeof(OverloadedProjectionResource), "does not declare exactly once as a public instance method")]
	[InlineData(typeof(ToolProjectionResource), "must not be an MCP tool")]
	[InlineData(typeof(WrongResultProjectionResource), "return type must exactly match 'String'")]
	[InlineData(typeof(NonShortProjectionResource), "it must declare short")]
	[InlineData(typeof(GatedProjectionResource), "a prepared resource read must never need a feature switch")]
	[InlineData(typeof(MissingSourceClassResource), "whose dispatch class is missing; it must be one of")]
	[InlineData(typeof(UnknownSourceClassResource), "whose dispatch class is unreviewed; it must be one of")]
	public void InvalidPreparedProjection_FailsTheResourceContract(Type resource, string expected)
	{
		OptionsValidationException exception = Assert.Throws<OptionsValidationException>(() =>
			McpPrimitiveCatalog.Create(Manifest(resource)));

		Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
	}

	private static CheatEngineMcpPrimitiveOptions Manifest<TResource>()
	{
		return Manifest(typeof(TResource));
	}

	private static CheatEngineMcpPrimitiveOptions Manifest(Type resource)
	{
		CheatEngineMcpPrimitiveOptions manifest = new();
		manifest.Add(new CheatEngineMcpPrimitive(CheatEngineMcpPrimitiveKind.Resource, resource));
		return manifest;
	}

	[McpServerToolType]
	public class ValidTools
	{
		[McpServerTool(Name = CheatEngineToolNames.RuntimeGetInfo, Title = "Read explicit", ReadOnly = true,
			Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
		[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.HostScan)]
		[Description("Reads the complete explicit result.")]
		public string ReadExplicit()
		{
			return "explicit";
		}

		[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
		public string ReadPrepared()
		{
			return "prepared";
		}
	}

	[McpServerToolType]
	public sealed class MissingProjectionTools
	{
		[McpServerTool(Name = CheatEngineToolNames.RuntimeGetInfo, Title = "Read missing", ReadOnly = true,
			Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
		[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.HostScan)]
		[Description("Reads the explicit result.")]
		public string ReadExplicit() => "explicit";
	}

	[McpServerToolType]
	public sealed class OverloadedProjectionTools
	{
		[McpServerTool(Name = CheatEngineToolNames.RuntimeGetInfo, Title = "Read overloaded", ReadOnly = true,
			Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
		[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.HostScan)]
		[Description("Reads the explicit result.")]
		public string ReadExplicit() => "explicit";

		[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
		public string Prepared()
		{
			return "a";
		}

		[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
		public string Prepared(CancellationToken cancellationToken)
		{
			return cancellationToken.CanBeCanceled ? "b" : "c";
		}
	}

	[McpServerToolType]
	public sealed class ToolProjectionTools
	{
		[McpServerTool(Name = CheatEngineToolNames.RuntimeGetInfo, Title = "Read tool", ReadOnly = true,
			Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
		[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.HostScan)]
		[Description("Reads the explicit result.")]
		public string ReadExplicit() => "explicit";

		[McpServerTool(Name = CheatEngineToolNames.RuntimeGetOverview, Title = "Prepared tool", ReadOnly = true,
			Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
		[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
		[Description("Incorrectly publishes the prepared method.")]
		public string Prepared()
		{
			return "prepared";
		}
	}

	[McpServerToolType]
	public sealed class WrongResultProjectionTools
	{
		[McpServerTool(Name = CheatEngineToolNames.RuntimeGetInfo, Title = "Read result", ReadOnly = true,
			Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
		[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.HostScan)]
		[Description("Reads the explicit result.")]
		public string ReadExplicit() => "explicit";

		[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
		public int Prepared()
		{
			return 1;
		}
	}

	[McpServerToolType]
	public sealed class NonShortProjectionTools
	{
		[McpServerTool(Name = CheatEngineToolNames.RuntimeGetInfo, Title = "Read nonshort", ReadOnly = true,
			Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
		[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.HostScan)]
		[Description("Reads the explicit result.")]
		public string ReadExplicit() => "explicit";

		[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.HostScan)]
		public string Prepared()
		{
			return "prepared";
		}
	}

	[McpServerToolType]
	public sealed class GatedProjectionTools
	{
		[McpServerTool(Name = CheatEngineToolNames.RuntimeGetInfo, Title = "Read gated", ReadOnly = true,
			Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
		[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.HostScan)]
		[Description("Reads the explicit result.")]
		public string ReadExplicit() => "explicit";

		[RequiresFeature(McpFeature.UnsafeLua)]
		[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
		public string Prepared()
		{
			return "prepared";
		}
	}

	[McpServerToolType]
	public sealed class MissingSourceClassTools
	{
		[McpServerTool(Name = CheatEngineToolNames.RuntimeGetInfo, Title = "Read missing class", ReadOnly = true,
			Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
		[Description("Reads the explicit result.")]
		public string ReadExplicit() => "explicit";

		[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
		public string Prepared() => "prepared";
	}

	[McpServerToolType]
	public sealed class UnknownSourceClassTools
	{
		[McpServerTool(Name = CheatEngineToolNames.RuntimeGetInfo, Title = "Read unknown class", ReadOnly = true,
			Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
		[McpMeta(McpDispatchClass.MetaKey, "unreviewed")]
		[Description("Reads the explicit result.")]
		public string ReadExplicit() => "explicit";

		[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
		public string Prepared() => "prepared";
	}

	[McpServerResourceType]
	public sealed class ValidResource
	{
		[McpServerResource(UriTemplate = "cheatengine://instance/prepared-valid", Name = "instance_prepared_valid",
			Title = "Prepared valid", MimeType = "application/json")]
		[McpSourceTool(typeof(ValidTools), CheatEngineToolNames.RuntimeGetInfo,
			PreparedProjection = nameof(ValidTools.ReadPrepared))]
		[Description("Reads the prepared result.")]
		public string Read()
		{
			return "prepared";
		}
	}

	[McpServerResourceType]
	public sealed class MissingProjectionResource
	{
		[McpServerResource(UriTemplate = "cheatengine://instance/prepared-missing", Name = "instance_prepared_missing",
			Title = "Prepared missing", MimeType = "application/json")]
		[McpSourceTool(typeof(MissingProjectionTools), CheatEngineToolNames.RuntimeGetInfo,
			PreparedProjection = "Missing")]
		[Description("Names no prepared method.")]
		public string Read() => "x";
	}

	[McpServerResourceType]
	public sealed class OverloadedProjectionResource
	{
		[McpServerResource(UriTemplate = "cheatengine://instance/prepared-overloaded", Name = "instance_prepared_overloaded",
			Title = "Prepared overloaded", MimeType = "application/json")]
		[McpSourceTool(typeof(OverloadedProjectionTools), CheatEngineToolNames.RuntimeGetInfo,
			PreparedProjection = nameof(OverloadedProjectionTools.Prepared))]
		[Description("Names an overloaded prepared method.")]
		public string Read() => "x";
	}

	[McpServerResourceType]
	public sealed class ToolProjectionResource
	{
		[McpServerResource(UriTemplate = "cheatengine://instance/prepared-tool", Name = "instance_prepared_tool",
			Title = "Prepared tool", MimeType = "application/json")]
		[McpSourceTool(typeof(ToolProjectionTools), CheatEngineToolNames.RuntimeGetInfo,
			PreparedProjection = nameof(ToolProjectionTools.Prepared))]
		[Description("Names a published tool as prepared.")]
		public string Read() => "x";
	}

	[McpServerResourceType]
	public sealed class WrongResultProjectionResource
	{
		[McpServerResource(UriTemplate = "cheatengine://instance/prepared-result", Name = "instance_prepared_result",
			Title = "Prepared result", MimeType = "application/json")]
		[McpSourceTool(typeof(WrongResultProjectionTools), CheatEngineToolNames.RuntimeGetInfo,
			PreparedProjection = nameof(WrongResultProjectionTools.Prepared))]
		[Description("Names a prepared method with another result type.")]
		public string Read() => "x";
	}

	[McpServerResourceType]
	public sealed class NonShortProjectionResource
	{
		[McpServerResource(UriTemplate = "cheatengine://instance/prepared-nonshort", Name = "instance_prepared_nonshort",
			Title = "Prepared nonshort", MimeType = "application/json")]
		[McpSourceTool(typeof(NonShortProjectionTools), CheatEngineToolNames.RuntimeGetInfo,
			PreparedProjection = nameof(NonShortProjectionTools.Prepared))]
		[Description("Names a nonshort prepared method.")]
		public string Read() => "x";
	}

	[McpServerResourceType]
	public sealed class GatedProjectionResource
	{
		[McpServerResource(UriTemplate = "cheatengine://instance/prepared-gated", Name = "instance_prepared_gated",
			Title = "Prepared gated", MimeType = "application/json")]
		[McpSourceTool(typeof(GatedProjectionTools), CheatEngineToolNames.RuntimeGetInfo,
			PreparedProjection = nameof(GatedProjectionTools.Prepared))]
		[Description("Names a feature-gated prepared method.")]
		public string Read() => "x";
	}

	[McpServerResourceType]
	public sealed class MissingSourceClassResource
	{
		[McpServerResource(UriTemplate = "cheatengine://instance/prepared-missing-class",
			Name = "instance_prepared_missing_class", Title = "Prepared missing class", MimeType = "application/json")]
		[McpSourceTool(typeof(MissingSourceClassTools), CheatEngineToolNames.RuntimeGetInfo,
			PreparedProjection = nameof(MissingSourceClassTools.Prepared))]
		[Description("Names a source with no dispatch class.")]
		public string Read() => "x";
	}

	[McpServerResourceType]
	public sealed class UnknownSourceClassResource
	{
		[McpServerResource(UriTemplate = "cheatengine://instance/prepared-unknown-class",
			Name = "instance_prepared_unknown_class", Title = "Prepared unknown class", MimeType = "application/json")]
		[McpSourceTool(typeof(UnknownSourceClassTools), CheatEngineToolNames.RuntimeGetInfo,
			PreparedProjection = nameof(UnknownSourceClassTools.Prepared))]
		[Description("Names a source with an unreviewed dispatch class.")]
		public string Read() => "x";
	}
}
