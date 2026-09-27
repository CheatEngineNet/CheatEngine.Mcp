using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

using CheatEngine.Mcp.Core.Contract;

using ModelContextProtocol;

namespace CheatEngine.Mcp.Core.Composition;

/// <summary>Builds the one serializer configuration a composition uses for schemas, arguments and results.</summary>
public static class CheatEngineMcpJson
{
	/// <summary>
	///     Whether compositions build strict options unless a caller says otherwise. The legacy tools still need the
	///     reflection resolver; the strict phase flips this once they are gone.
	/// </summary>
	internal const bool StrictByDefault = false;

	/// <summary>
	///     Creates read-only options from <see cref="McpJsonUtilities.DefaultOptions" /> (web defaults, omitted nulls,
	///     numbers readable from strings): the Core contract context and the manifest's resolvers come first, and the
	///     SDK's catch-all reflection enum converter is removed so contract enums keep their <c>snake_case</c> converter.
	/// </summary>
	/// <param name="manifest">The composition, whose <see cref="CheatEngineMcpPrimitiveOptions.JsonResolvers" /> are used.</param>
	/// <param name="strict">
	///     <see langword="false" /> keeps the SDK's reflection resolver and replaces its enum converter with an equivalent
	///     that skips attributed enums, so the legacy tools' schemas and results are unchanged.
	///     <see langword="true" /> keeps only source-generated metadata: the resolver chain is flattened recursively and
	///     every <see cref="DefaultJsonTypeInfoResolver" /> is dropped, as under Native AOT.
	/// </param>
	/// <returns>The read-only options.</returns>
	public static JsonSerializerOptions CreateOptions(CheatEngineMcpPrimitiveOptions manifest, bool strict)
	{
		ArgumentNullException.ThrowIfNull(manifest);
		JsonSerializerOptions options = new(McpJsonUtilities.DefaultOptions);
		for (int index = options.Converters.Count - 1; index >= 0; index--)
		{
			if (options.Converters[index] is JsonStringEnumConverter)
			{
				options.Converters.RemoveAt(index);
			}
		}

		if (!strict && JsonSerializer.IsReflectionEnabledByDefault)
		{
			options.Converters.Add(CreateLegacyEnumConverter());
		}

		List<IJsonTypeInfoResolver> chain = [CoreJsonContext.Default];
		foreach (IJsonTypeInfoResolver resolver in manifest.JsonResolvers)
		{
			if (!chain.Contains(resolver))
			{
				chain.Add(resolver);
			}
		}

		foreach (IJsonTypeInfoResolver resolver in strict
			         ? Flatten(options.TypeInfoResolverChain).Where(static resolver =>
				         resolver is not DefaultJsonTypeInfoResolver)
			         : options.TypeInfoResolverChain)
		{
			chain.Add(resolver);
		}

		options.TypeInfoResolverChain.Clear();
		foreach (IJsonTypeInfoResolver resolver in chain)
		{
			options.TypeInfoResolverChain.Add(resolver);
		}

		options.MakeReadOnly();
		return options;
	}

	/// <summary>Expands nested resolver chains, which the options copy constructor flattens only one level deep.</summary>
	/// <param name="resolvers">A resolver chain.</param>
	/// <returns>Every leaf resolver in order.</returns>
	internal static IEnumerable<IJsonTypeInfoResolver> Flatten(IEnumerable<IJsonTypeInfoResolver> resolvers)
	{
		foreach (IJsonTypeInfoResolver resolver in resolvers)
		{
			if (resolver is IEnumerable<IJsonTypeInfoResolver> nested)
			{
				foreach (IJsonTypeInfoResolver leaf in Flatten(nested))
				{
					yield return leaf;
				}
			}
			else
			{
				yield return resolver;
			}
		}
	}

	[UnconditionalSuppressMessage("AotAnalysis", "IL3050:RequiresDynamicCode",
		Justification = "Only created when reflection-based serialization is enabled, as the SDK's own converter is.")]
	private static LegacyEnumConverterFactory CreateLegacyEnumConverter()
	{
		return new LegacyEnumConverterFactory();
	}
}
