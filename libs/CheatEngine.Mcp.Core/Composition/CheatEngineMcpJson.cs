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
	///     Creates read-only options from <see cref="McpJsonUtilities.DefaultOptions" /> (web defaults, omitted nulls,
	///     numbers readable from strings). The Core contract context and the manifest's resolvers come first, the SDK's
	///     catch-all enum converter is removed and no reflection metadata resolver remains.
	/// </summary>
	/// <param name="manifest">The composition, whose <see cref="CheatEngineMcpPrimitiveOptions.JsonResolvers" /> are used.</param>
	/// <returns>The read-only options.</returns>
	public static JsonSerializerOptions CreateOptions(CheatEngineMcpPrimitiveOptions manifest)
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

		List<IJsonTypeInfoResolver> chain = [CoreJsonContext.Default];
		foreach (IJsonTypeInfoResolver resolver in manifest.JsonResolvers)
		{
			if (!chain.Contains(resolver))
			{
				chain.Add(resolver);
			}
		}

		foreach (IJsonTypeInfoResolver resolver in Flatten(options.TypeInfoResolverChain).Where(static resolver =>
					 resolver is not DefaultJsonTypeInfoResolver))
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
}
