using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace CheatEngine.Mcp.Plugin;

/// <summary>Resolves Costura's managed resources in CE's context so each plugin retains its own SDK state.</summary>
internal static class McpPluginDependencyResolver
{
	[ModuleInitializer]
	[SuppressMessage("Usage", "CA2255",
		Justification = "CE must resolve the bundled SDK before it loads the generated managed entry point.")]
	internal static void Initialize()
	{
		Assembly plugin = typeof(McpPluginDependencyResolver).Assembly;
		AssemblyLoadContext context = AssemblyLoadContext.GetLoadContext(plugin)!;
		context.Resolving += Resolve;
		context.ResolvingUnmanagedDll += ResolveNativeBridge;
		// Populate this context before .NET can reuse an SDK assembly already loaded in the default context.
		foreach (string resource in plugin.GetManifestResourceNames())
		{
			if (resource.StartsWith("costura.", StringComparison.Ordinal) &&
				resource.EndsWith(".dll.compressed", StringComparison.Ordinal))
			{
				AssemblyName name = new(resource["costura.".Length..^".dll.compressed".Length]);
				if (!context.Assemblies.Any(assembly =>
						string.Equals(assembly.GetName().Name, name.Name, StringComparison.OrdinalIgnoreCase)))
				{
					_ = Resolve(context, name);
				}
			}
		}
	}

	private static nint ResolveNativeBridge(Assembly assembly, string name)
	{
		// Costura preloads this DLL. The SDK requests its extensionless name from an in-memory assembly.
		return name == "cheatengine-sdk-lua-bridge" &&
			   NativeLibrary.TryLoad("cheatengine-sdk-lua-bridge.dll", out nint module)
			? module
			: nint.Zero;
	}

	[UnconditionalSuppressMessage("Trimming", "IL2026",
		Justification = "This framework-dependent CE plugin is never trimmed; Costura embeds complete managed assemblies.")]
	[UnconditionalSuppressMessage("AOT", "IL3050",
		Justification = "The managed CE plugin requires dynamic loading and is never published as Native AOT.")]
	private static Assembly? Resolve(AssemblyLoadContext context, AssemblyName name)
	{
		Assembly plugin = typeof(McpPluginDependencyResolver).Assembly;
		string resource = $"costura.{name.Name?.ToLowerInvariant()}.dll.compressed";
		using Stream? embedded = plugin.GetManifestResourceStream(resource);
		if (embedded is null)
		{
			return null;
		}

		using DeflateStream decompressed = new(embedded, CompressionMode.Decompress);
		using MemoryStream assembly = new();
		decompressed.CopyTo(assembly);
		assembly.Position = 0;
		return context.LoadFromStream(assembly);
	}
}
