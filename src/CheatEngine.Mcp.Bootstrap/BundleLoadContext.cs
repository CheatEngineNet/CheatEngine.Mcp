using System.Reflection;
using System.Runtime.Loader;

namespace CheatEngine.Mcp.Bootstrap;

internal sealed class BundleLoadContext(string runtimePath) : AssemblyLoadContext(isCollectible: false)
{
	private readonly AssemblyDependencyResolver _resolver = new(runtimePath);
	private readonly string _directory = Path.GetDirectoryName(runtimePath)!;

	protected override Assembly? Load(AssemblyName assemblyName)
	{
		string? path = _resolver.ResolveAssemblyToPath(assemblyName);
		return path is null ? null : LoadFromAssemblyPath(path);
	}

	protected override nint LoadUnmanagedDll(string unmanagedDllName)
	{
		string? path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
		// SDK ships this bridge as Content, so it is absent from the dependency manifest.
		if (path is null && unmanagedDllName is "cheatengine-sdk-lua-bridge" or "cheatengine-sdk-lua-bridge.dll")
		{
			path = Path.Combine(_directory, "cheatengine-sdk-lua-bridge.dll");
		}
		return path is null ? nint.Zero : LoadUnmanagedDllFromPath(path);
	}
}
