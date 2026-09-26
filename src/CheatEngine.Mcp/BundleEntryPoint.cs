namespace CheatEngine.Mcp;

/// <summary>Connects the dependency-free bundle loader to the SDK-generated bootstrap.</summary>
public static class BundleEntryPoint
{
	internal static string? PluginPath
	{
		get; private set;
	}
	internal static string? PluginDirectory => PluginPath is null ? null : Path.GetDirectoryName(PluginPath);

	/// <summary>Preserves the installed plugin path and forwards CE's opaque bootstrap arguments.</summary>
	public static int Initialize(nint arguments, int hostArgument, string pluginPath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(pluginPath);
		string fullPath = Path.GetFullPath(pluginPath);
		if (PluginPath is not null && !string.Equals(PluginPath, fullPath, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("The runtime is already bound to a different plugin file.");
		}
		PluginPath = fullPath;
		return global::CESDK.CESDK.CEPluginInitialize(arguments, hostArgument);
	}
}
