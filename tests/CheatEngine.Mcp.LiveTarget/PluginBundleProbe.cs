using System.IO.Compression;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace CheatEngine.Mcp.LiveTarget;

/// <summary>Cold-loads the installed plugin as Cheat Engine does, without accepting adjacent managed sidecars.</summary>
internal static class PluginBundleProbe
{
	private const string CosturaPrefix = "costura.";
	private const string CompressedDllSuffix = ".dll.compressed";
	private const string DllSuffix = ".dll";
	private const string NativeLuaBridge = "cheatengine-sdk-lua-bridge";
	private const string NativeLuaBridgeExport = "cheatengine_sdk_lua_protected";

	internal static int Run(string pluginPath)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(pluginPath) || !Path.IsPathFullyQualified(pluginPath))
			{
				throw new ArgumentException("The plugin probe requires an absolute plugin path.", nameof(pluginPath));
			}

			pluginPath = Path.GetFullPath(pluginPath);
			if (!File.Exists(pluginPath))
			{
				throw new FileNotFoundException("The plugin probe could not find the installed plugin.", pluginPath);
			}

			PluginLoadContext firstContext = new(pluginPath, "first");
			LoadedPlugin first = LoadPlugin(firstContext, pluginPath);
			SeedDefaultContext(first.Plugin, "CheatEngine.SDK.Hosting", "CheatEngine.SDK.Abi");

			PluginLoadContext secondContext = new(pluginPath, "second");
			LoadedPlugin second = LoadPlugin(secondContext, pluginPath);
			VerifyContextIsolation(first, second);
			Console.WriteLine("PLUGIN_BUNDLE_PROBE_OK");
			return 0;
		}
		catch (Exception exception)
		{
			Console.Error.WriteLine("PLUGIN_BUNDLE_PROBE_FAILED");
			Console.Error.WriteLine(exception);
			return 1;
		}
	}

	private static LoadedPlugin LoadPlugin(PluginLoadContext context, string pluginPath)
	{
		Assembly plugin = context.LoadFromAssemblyPath(pluginPath);
		RuntimeHelpers.RunModuleConstructor(plugin.ManifestModule.ModuleHandle);
		VerifyPluginLocation(plugin, pluginPath);
		string[] embeddedAssemblies = CosturaAssemblyNames(plugin).ToArray();
		if (embeddedAssemblies.Length == 0)
		{
			throw new InvalidOperationException("The plugin does not contain any Costura managed assembly resources.");
		}

		foreach (string assemblyName in embeddedAssemblies)
		{
			context.LoadFromAssemblyName(new AssemblyName(assemblyName));
		}
		LoadReferencedAssemblies(context);
		VerifyEmbeddedAssemblyIdentities(context, embeddedAssemblies);
		VerifyTypeResolution(context);
		VerifySharedFrameworkIdentity(plugin);
		VerifySdkEntryPoint(plugin);
		VerifyNativeLuaBridge(context);
		VerifyProductInformationalVersions(plugin, context);
		return new LoadedPlugin(plugin, context, embeddedAssemblies);
	}

	private static IEnumerable<string> CosturaAssemblyNames(Assembly plugin)
	{
		return plugin.GetManifestResourceNames()
			.Where(static resource => resource.StartsWith(CosturaPrefix, StringComparison.OrdinalIgnoreCase))
			.Select(static resource => resource.EndsWith(CompressedDllSuffix, StringComparison.OrdinalIgnoreCase)
				? resource[CosturaPrefix.Length..^CompressedDllSuffix.Length]
				: resource.EndsWith(DllSuffix, StringComparison.OrdinalIgnoreCase)
					? resource[CosturaPrefix.Length..^DllSuffix.Length]
					: null)
			.Where(static name => !string.IsNullOrWhiteSpace(name))
			.Select(static name => name!)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.Order(StringComparer.OrdinalIgnoreCase);
	}

	private static void VerifyPluginLocation(Assembly plugin, string pluginPath)
	{
		if (!string.Equals(plugin.Location, pluginPath, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException($"The plugin loaded from '{plugin.Location}', not its installed path '{pluginPath}'.");
		}
	}

	private static void SeedDefaultContext(Assembly plugin, params string[] assemblyNames)
	{
		foreach (string assemblyName in assemblyNames)
		{
			string resourceName = $"{CosturaPrefix}{assemblyName.ToLowerInvariant()}{CompressedDllSuffix}";
			using Stream resource = plugin.GetManifestResourceStream(resourceName)
				?? throw new FileNotFoundException($"The plugin does not embed '{assemblyName}'.", resourceName);
			using DeflateStream decompressed = new(resource, CompressionMode.Decompress);
			using MemoryStream bytes = new();
			decompressed.CopyTo(bytes);
			bytes.Position = 0;
			Assembly loaded = AssemblyLoadContext.Default.LoadFromStream(bytes);
			if (AssemblyLoadContext.GetLoadContext(loaded) != AssemblyLoadContext.Default)
			{
				throw new InvalidOperationException($"Could not seed '{assemblyName}' into the default context.");
			}
		}
	}

	private static void LoadReferencedAssemblies(PluginLoadContext context)
	{
		HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase);
		for (int index = 0; index < context.Assemblies.Count(); index++)
		{
			Assembly assembly = context.Assemblies.ElementAt(index);
			if (!visited.Add(assembly.FullName!))
			{
				continue;
			}

			foreach (AssemblyName reference in assembly.GetReferencedAssemblies())
			{
				context.LoadFromAssemblyName(reference);
			}
		}
	}

	private static void VerifyEmbeddedAssemblyIdentities(PluginLoadContext context, IEnumerable<string> embeddedAssemblies)
	{
		Assembly[] loaded = context.Assemblies.ToArray();
		foreach (string expectedName in embeddedAssemblies)
		{
			Assembly assembly = loaded.SingleOrDefault(candidate => string.Equals(candidate.GetName().Name, expectedName,
				StringComparison.OrdinalIgnoreCase))
				?? throw new FileLoadException($"Costura resource '{expectedName}' did not load into the plugin context.");
			if (AssemblyLoadContext.GetLoadContext(assembly) != context)
			{
				throw new InvalidOperationException($"Costura resource '{expectedName}' escaped the plugin load context.");
			}
		}
	}

	private static void VerifyTypeResolution(PluginLoadContext context)
	{
		foreach (Assembly assembly in context.Assemblies)
		{
			try
			{
				_ = assembly.GetTypes();
			}
			catch (ReflectionTypeLoadException exception)
			{
				string failures = string.Join(Environment.NewLine, exception.LoaderExceptions
					.Where(static failure => failure is not null)
					.Select(static failure => failure!.ToString()));
				throw new TypeLoadException($"Could not resolve types from '{assembly.FullName}'.{Environment.NewLine}{failures}", exception);
			}
		}
	}

	private static void VerifyContextIsolation(LoadedPlugin first, LoadedPlugin second)
	{
		Assembly[] firstAssemblies = first.Context.Assemblies.ToArray();
		Assembly[] defaultAssemblies = AssemblyLoadContext.Default.Assemblies.ToArray();
		foreach (string name in second.EmbeddedAssemblies)
		{
			Assembly secondAssembly = FindAssembly(second.Context.Assemblies, name, "second plugin context");
			Assembly firstAssembly = FindAssembly(firstAssemblies, name, "first plugin context");
			if (ReferenceEquals(secondAssembly, firstAssembly) || AssemblyLoadContext.GetLoadContext(secondAssembly) != second.Context)
			{
				throw new InvalidOperationException($"Embedded assembly '{name}' was shared between the two plugin contexts.");
			}

			Assembly? defaultAssembly = defaultAssemblies.SingleOrDefault(candidate =>
				string.Equals(candidate.GetName().Name, name, StringComparison.OrdinalIgnoreCase));
			if (defaultAssembly is not null && ReferenceEquals(secondAssembly, defaultAssembly))
			{
				throw new InvalidOperationException($"Embedded assembly '{name}' fell back to the default context.");
			}
		}
	}

	private static Assembly FindAssembly(IEnumerable<Assembly> assemblies, string name, string context)
	{
		return assemblies.SingleOrDefault(candidate => string.Equals(candidate.GetName().Name, name,
			StringComparison.OrdinalIgnoreCase))
			?? throw new FileLoadException($"Costura resource '{name}' did not load into the {context}.");
	}

	private static void VerifyProductInformationalVersions(Assembly plugin, PluginLoadContext context)
	{
		string version = plugin.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
			?? throw new InvalidOperationException("The plugin has no informational version.");
		foreach (Assembly product in context.Assemblies.Where(static assembly =>
			assembly.GetName().Name!.StartsWith("CheatEngine.Mcp.", StringComparison.Ordinal)))
		{
			string productVersion = product.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
				?? throw new InvalidOperationException($"Product assembly '{product.GetName().Name}' has no informational version.");
			if (!string.Equals(productVersion, version, StringComparison.Ordinal))
			{
				throw new InvalidOperationException($"Product assembly '{product.GetName().Name}' is version '{productVersion}', not '{version}'.");
			}
		}
	}

	private static void VerifySharedFrameworkIdentity(Assembly plugin)
	{
		Assembly sharedAssembly = Assembly.Load("Microsoft.Extensions.DependencyInjection.Abstractions");
		Type sharedServices = sharedAssembly.GetType("Microsoft.Extensions.DependencyInjection.IServiceCollection", throwOnError: true)!;
		Type extension = plugin.GetType("CheatEngine.Mcp.Plugin.McpPluginServiceCollectionExtensions", throwOnError: true)!;
		MethodInfo method = extension.GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
			.Single(candidate => candidate.Name == "AddCheatEngineMcpServices");
		Type pluginServices = method.GetParameters()[0].ParameterType;
		if (pluginServices != sharedServices)
		{
			throw new InvalidOperationException("IServiceCollection has different identities in the plugin and shared framework.");
		}
		if (AssemblyLoadContext.GetLoadContext(sharedServices.Assembly) != AssemblyLoadContext.Default)
		{
			throw new InvalidOperationException("IServiceCollection did not resolve from the default shared-framework context.");
		}
	}

	private static void VerifySdkEntryPoint(Assembly plugin)
	{
		Type entryType = plugin.GetType("CESDK.CESDK", throwOnError: true)!;
		MethodInfo method = entryType.GetMethod("CEPluginInitialize", BindingFlags.Public | BindingFlags.Static)
			?? throw new MissingMethodException(entryType.FullName, "CEPluginInitialize");
		Type[] parameters = method.GetParameters().Select(static parameter => parameter.ParameterType).ToArray();
		if (method.ReturnType != typeof(int) || !parameters.SequenceEqual([typeof(nint), typeof(int)]))
		{
			throw new InvalidOperationException("CESDK.CEPluginInitialize does not match the required (nint, int) => int callback ABI.");
		}
	}

	private static void VerifyNativeLuaBridge(PluginLoadContext context)
	{
		Assembly interop = context.Assemblies.SingleOrDefault(static assembly => assembly.GetName().Name == "CheatEngine.SDK.Lua.Interop")
			?? throw new FileNotFoundException("The Costura payload did not load CheatEngine.SDK.Lua.Interop.");
		Type protectedApi = interop.GetType("CheatEngine.SDK.Lua.Interop.Protected.LuaProtectedApi", throwOnError: true)!;
		nint module = nint.Zero;
		try
		{
			module = NativeLibrary.Load(NativeLuaBridge, protectedApi.Assembly, DllImportSearchPath.AssemblyDirectory);
			if (!NativeLibrary.TryGetExport(module, NativeLuaBridgeExport, out _))
			{
				throw new EntryPointNotFoundException($"The native Lua bridge lacks '{NativeLuaBridgeExport}'.");
			}
		}
		finally
		{
			if (module != nint.Zero)
			{
				NativeLibrary.Free(module);
			}
		}
	}

	private sealed record LoadedPlugin(Assembly Plugin, PluginLoadContext Context, string[] EmbeddedAssemblies);

	private sealed class PluginLoadContext : AssemblyLoadContext
	{
		private readonly AssemblyDependencyResolver _resolver;

		internal PluginLoadContext(string pluginPath, string name)
			: base($"CheatEngine.Mcp.PluginBundleProbe.{name}", isCollectible: true)
		{
			_resolver = new AssemblyDependencyResolver(pluginPath);
		}

		protected override Assembly? Load(AssemblyName assemblyName)
		{
			string? path = _resolver.ResolveAssemblyToPath(assemblyName);
			return path is null ? null : LoadFromAssemblyPath(path);
		}
	}
}
