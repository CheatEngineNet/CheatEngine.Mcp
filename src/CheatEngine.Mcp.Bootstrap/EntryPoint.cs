using System.Diagnostics;
using System.Reflection;

using CheatEngine.Mcp.Bootstrap;

namespace CESDK;

/// <summary>The exact managed bootstrap ABI requested by Cheat Engine's CLR host.</summary>
public static class CESDK
{
	private static readonly Lazy<Func<nint, int, string, int>> Initialize = new(LoadRuntime);

	public static int CEPluginInitialize(nint arguments, int hostArgument)
	{
		try
		{
			return Initialize.Value(arguments, hostArgument, typeof(CESDK).Assembly.Location);
		}
		catch (Exception exception)
		{
			// No Client/SDK or logging dependency may be loaded before extraction succeeds.
			Trace.TraceError("CheatEngine.Mcp bootstrap failed: {0}", exception);
			return 0;
		}
	}

	private static Func<nint, int, string, int> LoadRuntime()
	{
		using Stream payload = typeof(CESDK).Assembly.GetManifestResourceStream("CheatEngine.Mcp.Payload")
			?? throw new InvalidOperationException("The plugin payload is missing.");
		string? cacheOverride = Environment.GetEnvironmentVariable("MCP_BUNDLE_CACHE_DIRECTORY");
		string cacheRoot = cacheOverride ?? Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CheatEngine.Mcp", "cache");
		string directory = BundleCache.Extract(payload, cacheRoot);
		string runtimePath = Path.Combine(directory, "CheatEngine.Mcp.Runtime.dll");
		BundleLoadContext context = new(runtimePath);
		Assembly runtime = context.LoadFromAssemblyPath(runtimePath);
		Type entryPoint = runtime.GetType("CheatEngine.Mcp.BundleEntryPoint", throwOnError: true)!;
		MethodInfo initialize = entryPoint.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static)
			?? throw new MissingMethodException(entryPoint.FullName, "Initialize");
		// The cached delegate roots the non-collectible context and SDK callback lifetime.
		return initialize.CreateDelegate<Func<nint, int, string, int>>();
	}
}
