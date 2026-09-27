using System.ComponentModel;
using System.Globalization;

using CheatEngine.Client;
using CheatEngine.Client.Inspection;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Core.Files;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Values;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools.Symbol;

/// <summary>
///     The symbol lookup tools: <c>symbol_resolve</c>, <c>symbol_get_module_preference</c>,
///     <c>symbol_set_module_preference</c>, <c>symbol_reload</c> and <c>symbol_add_module</c>.
/// </summary>
[McpServerToolType]
public sealed class SymbolTools
{
	/// <summary>The most expressions one <c>symbol_resolve</c> call resolves.</summary>
	internal const int MaximumExpressions = 256;

	/// <summary>The longest expression accepted.</summary>
	internal const int MaximumExpressionLength = 1024;

	/// <summary>The most modules one <c>symbol_set_module_preference</c> call names.</summary>
	internal const int MaximumPreferenceModules = 64;

	/// <summary>The longest module name accepted.</summary>
	internal const int MaximumModuleLength = 256;

	private readonly ToolDispatch _dispatch;
	private readonly McpFilePaths _paths;

	/// <summary>Creates the tools; the constructor does no Cheat Engine work.</summary>
	/// <param name="dispatch">The activation's dispatch facade.</param>
	/// <param name="paths">The activation's host-file policy.</param>
	public SymbolTools(ToolDispatch dispatch, McpFilePaths paths)
	{
		ArgumentNullException.ThrowIfNull(dispatch);
		ArgumentNullException.ThrowIfNull(paths);
		_dispatch = dispatch;
		_paths = paths;
	}

	/// <summary>Resolves expressions to addresses, names and symbol records, each in band.</summary>
	/// <param name="expressions">The expressions.</param>
	/// <param name="shallow">Whether to use Cheat Engine's shallow resolution.</param>
	/// <param name="cancellationToken">The request's token.</param>
	/// <returns>One entry per expression.</returns>
	[McpServerTool(Name = CheatEngineToolNames.SymbolResolve, Title = "Resolve addresses and names", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Resolve up to 256 Cheat Engine expressions in one call. Each expression may be an address (7FF6A1B2C3D0), a module offset (game.exe+1A2B), an export (kernel32.CreateFileW), a registered symbol or pointer arithmetic ([game.exe+10]+8). Each entry returns the address, Cheat Engine's best name for it (module+offset or symbol) and the symbol record when the expression names one. An expression that does not resolve carries its own error; check every entry. shallow skips the slower lookups, such as symbols still loading.")]
	public SymbolResolveResult Resolve(
		[Description("1 to 256 expressions, each 1 to 1024 characters, such as game.exe+1A2B or 7FF6A1B2C3D0.")]
		string[] expressions,
		[Description("Use Cheat Engine's shallow resolution, which skips the slower symbol lookups.")]
		bool shallow = false,
		CancellationToken cancellationToken = default)
	{
		if (expressions is not { Length: > 0 })
		{
			throw CheatEngineToolException.InvalidArgument("expressions", "must list at least one expression.");
		}

		if (expressions.Length > MaximumExpressions)
		{
			throw CheatEngineToolException.LimitExceeded("expressions",
				$"accepts at most {MaximumExpressions} expressions.");
		}

		for (int index = 0; index < expressions.Length; index++)
		{
			string? expression = expressions[index];
			if (string.IsNullOrWhiteSpace(expression) || expression.Length > MaximumExpressionLength ||
				expression.Any(char.IsControl))
			{
				throw CheatEngineToolException.InvalidArgument(
					$"expressions[{index.ToString(CultureInfo.InvariantCulture)}]",
					$"must be 1 to {MaximumExpressionLength} characters without control characters.");
			}
		}

		ICheatEngineClient client = _dispatch.Client;
		AddressResolutionMode mode = shallow ? AddressResolutionMode.Shallow : AddressResolutionMode.Default;
		return _dispatch.Run(CheatEngineToolNames.SymbolResolve, token =>
		{
			SymbolResolution[] items = new SymbolResolution[expressions.Length];
			for (int index = 0; index < items.Length; index++)
			{
				items[index] = ResolveOne(client, expressions[index], mode, token);
			}

			return new SymbolResolveResult(items);
		}, cancellationToken);
	}

	/// <summary>Reads the module precedence of symbol lookup.</summary>
	/// <param name="cancellationToken">The request's token.</param>
	/// <returns>The precedence list.</returns>
	[McpServerTool(Name = CheatEngineToolNames.SymbolGetModulePreference, Title = "Get module preference",
		ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Read Cheat Engine's module precedence for symbol lookup: when a symbol name exists in several modules, the first listed module wins. Names are extensionless; at most 1024 are returned.")]
	public ModulePreference GetModulePreference(CancellationToken cancellationToken = default)
	{
		return _dispatch.RunLua(CheatEngineToolNames.SymbolGetModulePreference, SymbolScripts.GetModulePreference,
			SymbolJsonContext.Default.ModulePreference, cancellationToken);
	}

	/// <summary>Puts modules first in the precedence of symbol lookup, or replaces the list.</summary>
	/// <param name="modules">The modules, first first.</param>
	/// <param name="replace">Whether to replace the whole list.</param>
	/// <param name="cancellationToken">The request's token.</param>
	/// <returns>The list read back.</returns>
	[McpServerTool(Name = CheatEngineToolNames.SymbolSetModulePreference, Title = "Set module preference",
		ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Change Cheat Engine's module precedence for symbol lookup. By default the given modules move to the front in the given order and the rest of the current list follows; replace=true makes them the whole list, dropping the others. Names are extensionless (game, not game.exe). Returns the list read back. It affects every later lookup in this Cheat Engine, tables included.")]
	public ModulePreference SetModulePreference(
		[Description("1 to 64 extensionless module names, first first, such as [\"game\", \"engine\"].")]
		string[] modules,
		[Description("Replace the whole list instead of moving these modules to the front.")]
		bool replace = false,
		CancellationToken cancellationToken = default)
	{
		if (modules is not { Length: > 0 })
		{
			throw CheatEngineToolException.InvalidArgument("modules", "must list at least one module.");
		}

		if (modules.Length > MaximumPreferenceModules)
		{
			throw CheatEngineToolException.LimitExceeded("modules",
				$"accepts at most {MaximumPreferenceModules} modules.");
		}

		HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
		for (int index = 0; index < modules.Length; index++)
		{
			string? module = modules[index];
			string parameter = $"modules[{index.ToString(CultureInfo.InvariantCulture)}]";
			if (string.IsNullOrWhiteSpace(module) || module.Length > MaximumModuleLength ||
				module.Contains('.', StringComparison.Ordinal) || module.Any(char.IsControl))
			{
				throw CheatEngineToolException.InvalidArgument(parameter,
					$"must be an extensionless module name of 1 to {MaximumModuleLength} characters, such as game.");
			}

			if (!seen.Add(module))
			{
				throw CheatEngineToolException.InvalidArgument(parameter, "repeats an earlier module.");
			}
		}

		return _dispatch.RunLua(CheatEngineToolNames.SymbolSetModulePreference, SymbolScripts.SetModulePreference,
			SymbolJsonContext.Default.ModulePreference, cancellationToken, modules, replace);
	}

	/// <summary>Starts a symbol reload without waiting.</summary>
	/// <param name="scope">What to reload.</param>
	/// <param name="module">The .NET module, for the dotnet scope only.</param>
	/// <param name="cancellationToken">The request's token.</param>
	/// <returns>Whether symbols are loaded.</returns>
	[McpServerTool(Name = CheatEngineToolNames.SymbolReload, Title = "Reload symbols", ReadOnly = false,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Start a symbol reload and return at once; it never waits. new_modules loads the symbols of modules loaded since the last scan, all reinitializes the whole symbol handler, dotnet reinitializes the .NET symbols (after code was JIT-compiled), optionally of one module. done tells whether Cheat Engine reports every symbol loaded; while it is false, poll runtime_get_overview (symbolsLoaded) instead of waiting.")]
	public SymbolLoadState Reload(
		[Description("new_modules, all or dotnet.")]
		SymbolReloadScope scope = SymbolReloadScope.NewModules,
		[Description("The .NET module to reinitialize, such as Assembly-CSharp.dll; only with scope dotnet.")]
		string? module = null,
		CancellationToken cancellationToken = default)
	{
		string wire = scope switch
		{
			SymbolReloadScope.NewModules => "new_modules",
			SymbolReloadScope.All => "all",
			SymbolReloadScope.Dotnet => "dotnet",
			_ => throw CheatEngineToolException.InvalidArgument("scope", "must be new_modules, all or dotnet.")
		};
		if (module is not null)
		{
			if (scope is not SymbolReloadScope.Dotnet)
			{
				throw CheatEngineToolException.InvalidArgument("module", "applies only to scope dotnet.");
			}

			if (string.IsNullOrWhiteSpace(module) || module.Length > MaximumModuleLength || module.Any(char.IsControl))
			{
				throw CheatEngineToolException.InvalidArgument("module",
					$"must be a module name of 1 to {MaximumModuleLength} characters.");
			}
		}

		LuaSymbolReload reload = _dispatch.RunLua(CheatEngineToolNames.SymbolReload, SymbolScripts.Reload,
			SymbolLuaJsonContext.Default.LuaSymbolReload, cancellationToken, wire, module);
		return new SymbolLoadState(reload.Done, scope);
	}

	/// <summary>Loads the symbols of a file, such as a PDB, at a base address.</summary>
	/// <param name="path">The symbol file.</param>
	/// <param name="baseAddress">The base address.</param>
	/// <param name="enumStructures">Whether to enumerate PDB structures.</param>
	/// <param name="cancellationToken">The request's token.</param>
	/// <returns>The loaded file and base.</returns>
	[McpServerTool(Name = CheatEngineToolNames.SymbolAddModule, Title = "Load symbols from a file", ReadOnly = false,
		Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.BlockingNative)]
	[Description(
		"Load the symbols of a file, such as a game-shipped PDB or a DLL, as a module at baseAddress (Cheat Engine's symbolHandlerAddModule). enumStructures also enumerates PDB structures for structure_get_pdb_layout. path must be an absolute path on a local fixed drive; UNC, device and linked paths, alternate streams and the MCP directories are refused, and the file stays locked against changes during the load. One native call: a large PDB blocks Cheat Engine for several seconds (blocking_native).")]
	public SymbolModuleLoad AddModule(
		[Description("The absolute path of the symbol file on a local fixed drive, such as C:\\Games\\Game\\game.pdb.")]
		string path,
		[Description(
			"The base address the symbols apply to, as an address expression such as game.exe or 7FF6A1B20000.")]
		string baseAddress,
		[Description("Also enumerate PDB structures, which structure_get_pdb_layout reads; slower.")]
		bool enumStructures = false,
		CancellationToken cancellationToken = default)
	{
		string expression = RequireExpression(baseAddress, "baseAddress");
		using HeldFile file = _paths.OpenRead(path, CheatEngineToolNames.SymbolAddModule, 0);
		ICheatEngineClient client = _dispatch.Client;
		string fullPath = file.FullPath;
		ulong loadedAt = _dispatch.Run(CheatEngineToolNames.SymbolAddModule, token =>
		{
			Address target = ResolveOrRefuse(client, expression, "baseAddress", token);
			// The load is the only effect, so the request's token still applies to its admission.
			_ = _dispatch.ExecuteLua(CheatEngineToolNames.SymbolAddModule, SymbolScripts.AddModule,
				SymbolLuaJsonContext.Default.LuaSymbolModuleLoad, token, fullPath, target.ToUInt64(), enumStructures);
			return target.ToUInt64();
		}, cancellationToken);
		return new SymbolModuleLoad(fullPath, HexFormat.Address(loadedAt), enumStructures);
	}

	/// <summary>Enables Windows and/or kernel symbol loading in Cheat Engine.</summary>
	[McpServerTool(Name = CheatEngineToolNames.SymbolEnableSources, Title = "Enable system symbol sources",
		ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.BlockingNative)]
	[Description(
		"Enable Windows PDB symbols and/or kernel export symbols in Cheat Engine. Windows symbols can download PDBs from an external symbol service and block for a long time on first use. Kernel symbols require Mcp:EnableKernelAccess. Returns which sources were requested and whether external network access may have occurred.")]
	public SymbolSourcesEnabled EnableSources(
		[Description("Enable Windows PDB symbols; may download files from an external service.")]
		bool windows,
		[Description("Enable kernel symbols; requires the kernel access switch.")]
		bool kernel,
		CancellationToken cancellationToken = default)
	{
		if (!windows && !kernel)
		{
			throw CheatEngineToolException.InvalidArgument("windows", "enable at least one symbol source.");
		}

		if (kernel)
		{
			_dispatch.Features.Require(McpFeature.KernelAccess, CheatEngineToolNames.SymbolEnableSources);
		}

		return _dispatch.RunLua(CheatEngineToolNames.SymbolEnableSources, SymbolScripts.EnableSources,
			SymbolJsonContext.Default.SymbolSourcesEnabled, cancellationToken, windows, kernel);
	}

	/// <summary>Checks an address expression argument before any dispatch.</summary>
	/// <param name="value">The expression.</param>
	/// <param name="parameter">The parameter name.</param>
	/// <returns>The trimmed expression.</returns>
	internal static string RequireExpression(string? value, string parameter)
	{
		string trimmed = value?.Trim() ?? string.Empty;
		if (trimmed.Length is 0 or > MaximumExpressionLength || trimmed.Any(char.IsControl))
		{
			throw CheatEngineToolException.InvalidArgument(parameter,
				$"must be an address expression of 1 to {MaximumExpressionLength} characters.");
		}

		return trimmed;
	}

	/// <summary>Resolves an address expression inside a dispatch; an expression that does not resolve is not_found.</summary>
	/// <param name="client">The activation's Client.</param>
	/// <param name="expression">The checked expression.</param>
	/// <param name="parameter">The parameter that carried it.</param>
	/// <param name="token">The dispatch body's token.</param>
	/// <returns>The address.</returns>
	internal static Address ResolveOrRefuse(ICheatEngineClient client, string expression, string parameter,
		CancellationToken token)
	{
		if (client.Inspection.TryResolveAddress(new SymbolExpression(expression), AddressResolutionMode.Default,
				out Address address, out CheatEngineFailure failure, token))
		{
			return address;
		}

		if (failure.Kind is CheatEngineFailureKind.NotFound)
		{
			throw CheatEngineToolException.NotFound($"{parameter} does not resolve to an address.",
				"Check the expression with symbol_resolve.");
		}

		throw CheatEngineToolException.FromFailure(failure, client.Stopping.IsCancellationRequested);
	}

	private static SymbolResolution ResolveOne(ICheatEngineClient client, string expression,
		AddressResolutionMode mode, CancellationToken token)
	{
		IInspectionClient inspection = client.Inspection;
		if (!inspection.TryResolveAddress(new SymbolExpression(expression), mode, out Address address,
				out CheatEngineFailure failure, token))
		{
			return new SymbolResolution(expression, Error: InBand(client, failure));
		}

		if (mode is AddressResolutionMode.Shallow)
		{
			return new SymbolResolution(expression, HexFormat.Address(address));
		}

		string? name = inspection.TryResolveName(address, out string? resolved, out CheatEngineFailure nameFailure,
			token)
			? resolved
			: Ignore(client, nameFailure);
		SymbolDetails? symbol = inspection.TryGetSymbol(new SymbolExpression(expression), out SymbolInfo info,
			out CheatEngineFailure symbolFailure, token)
			? new SymbolDetails(info.ModuleName, info.SearchKey, HexFormat.Address(info.Address),
				(long) Math.Min(info.Size.Value, long.MaxValue))
			: Ignore<SymbolDetails>(client, symbolFailure);
		return new SymbolResolution(expression, HexFormat.Address(address), name, symbol);
	}

	/// <summary>
	///     Reports a per-expression failure in band, but raises the failures that concern the whole dispatch: no target,
	///     a changed target, a stopping activation or a cancellation.
	/// </summary>
	private static SymbolItemError InBand(ICheatEngineClient client, CheatEngineFailure failure)
	{
		ThrowIfSystemic(client, failure);
		ToolError mapped = ToolFailureMapping.Map(failure, false);
		return new SymbolItemError(mapped.Kind, mapped.Message);
	}

	private static string? Ignore(ICheatEngineClient client, CheatEngineFailure failure)
	{
		ThrowIfSystemic(client, failure);
		return null;
	}

	private static T? Ignore<T>(ICheatEngineClient client, CheatEngineFailure failure) where T : class
	{
		ThrowIfSystemic(client, failure);
		return null;
	}

	private static void ThrowIfSystemic(ICheatEngineClient client, CheatEngineFailure failure)
	{
		if (failure.Kind is CheatEngineFailureKind.TargetNotAttached or CheatEngineFailureKind.TargetChanged
			or CheatEngineFailureKind.TargetIdentityUnavailable or CheatEngineFailureKind.ActivationExpired
			or CheatEngineFailureKind.Cancelled or CheatEngineFailureKind.InvalidState
			or CheatEngineFailureKind.RuntimeChanged)
		{
			throw CheatEngineToolException.FromFailure(failure, client.Stopping.IsCancellationRequested);
		}
	}
}
