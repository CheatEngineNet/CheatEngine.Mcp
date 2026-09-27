using System.ComponentModel;

using CheatEngine.Client;
using CheatEngine.Client.Inspection;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.SDK.Engine.Values;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools.Symbol;

/// <summary>
///     The registered-symbol tools: <c>symbol_register</c>, <c>symbol_unregister</c> and
///     <c>symbol_list_registered</c>. Registrations are Client leases tracked as <c>symbol</c> resources.
/// </summary>
[McpServerToolType]
public sealed class SymbolRegistrationTools
{
	/// <summary>The longest symbol name accepted.</summary>
	internal const int MaximumNameLength = 256;

	/// <summary>The largest page of <c>symbol_list_registered</c>.</summary>
	internal const int MaximumLimit = 1000;

	private const string ResourceKind = "symbol";

	private readonly ToolDispatch _dispatch;
	private readonly SymbolRegistrations _registrations;
	private readonly TargetResources _resources;

	/// <summary>Creates the tools; the constructor does no Cheat Engine work.</summary>
	/// <param name="dispatch">The activation's dispatch facade.</param>
	/// <param name="resources">The activation's tracked target resources.</param>
	/// <param name="registrations">The activation's owned symbols.</param>
	public SymbolRegistrationTools(ToolDispatch dispatch, TargetResources resources,
		SymbolRegistrations registrations)
	{
		ArgumentNullException.ThrowIfNull(dispatch);
		ArgumentNullException.ThrowIfNull(resources);
		ArgumentNullException.ThrowIfNull(registrations);
		_dispatch = dispatch;
		_resources = resources;
		_registrations = registrations;
	}

	/// <summary>Registers a symbol owned by this activation.</summary>
	/// <param name="name">The symbol name.</param>
	/// <param name="address">The address expression it names.</param>
	/// <param name="doNotSave">Whether saved tables omit it.</param>
	/// <param name="cancellationToken">The request's token.</param>
	/// <returns>The registered symbol.</returns>
	[McpServerTool(Name = CheatEngineToolNames.SymbolRegister, Title = "Register a symbol", ReadOnly = false,
		Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Register a named symbol, such as playerBase, for an address, so later address expressions, records and scripts can use the name. The address expression is resolved once, now. The symbol is owned by this plugin activation: at most 128 at a time; symbol_unregister, runtime_release_resources or disabling the plugin removes it. A name that already resolves in Cheat Engine (another symbol, a module or a hexadecimal number) is refused. doNotSave (default true) keeps it out of saved cheat tables.")]
	public RegisteredSymbol Register(
		[Description("The symbol name, 1 to 256 characters; use a distinctive name such as playerBase.")]
		string name,
		[Description("The address it names, as an address expression such as [game.exe+1A2B]+10 or 7FF6A1B2C3D0.")]
		string address,
		[Description("Keep the symbol out of saved cheat tables.")]
		bool doNotSave = true,
		CancellationToken cancellationToken = default)
	{
		string symbolName = RequireName(name);
		string expression = SymbolTools.RequireExpression(address, "address");
		switch (_registrations.TryReserve(symbolName))
		{
			case ReservationRefusal.AlreadyOwned:
				throw CheatEngineToolException.InvalidArgument("name",
					"is already registered by this activation; unregister it first or choose another name.");
			case ReservationRefusal.Full:
				throw CheatEngineToolException.LimitExceeded("name",
					$"this activation already owns {SymbolRegistrations.MaximumOwned} symbols; unregister some first.");
		}

		bool committed = false;
		try
		{
			ICheatEngineClient client = _dispatch.Client;
			RegisteredSymbol registered = _dispatch.Run(CheatEngineToolNames.SymbolRegister, token =>
			{
				Address target = SymbolTools.ResolveOrRefuse(client, expression, "address", token);
				ISymbolRegistrationLease lease = client.Inspection.RegisterSymbol(
					new SymbolRegistration(symbolName, target, doNotSave), token);
				ITargetResource resource;
				try
				{
					resource = _resources.Track(lease, ResourceKind, () => _registrations.Remove(lease), lease.Name,
						HexFormat.Address(lease.Address));
				}
				catch
				{
					// Nothing may own a registration that is not tracked: undo it at once.
					lease.Release();
					throw;
				}

				_registrations.Commit(new OwnedSymbol(lease.Name, lease, resource, doNotSave));
				committed = true;
				return new RegisteredSymbol(lease.Name, HexFormat.Address(lease.Address), doNotSave,
					resource.Descriptor.Id);
			}, cancellationToken);
			return registered;
		}
		finally
		{
			if (!committed)
			{
				_registrations.CancelReservation(symbolName);
			}
		}
	}

	/// <summary>Unregisters a symbol this activation registered.</summary>
	/// <param name="name">The symbol name.</param>
	/// <param name="cancellationToken">The request's token.</param>
	/// <returns>What the release did.</returns>
	[McpServerTool(Name = CheatEngineToolNames.SymbolUnregister, Title = "Unregister a symbol", ReadOnly = false,
		Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Unregister a symbol that this plugin activation registered with symbol_register. A name this activation does not own is not_found, including symbols of tables and scripts. An incomplete release is a partial_effect error whose details carry the release outcome: retryable means nothing happened yet and the call can be repeated; otherwise manual recovery is needed.")]
	public SymbolReleaseResult Unregister(
		[Description("The symbol name, as symbol_register returned it (case-insensitive).")]
		string name,
		CancellationToken cancellationToken = default)
	{
		string symbolName = RequireName(name);
		if (!_registrations.TryGet(symbolName, out OwnedSymbol owned))
		{
			throw CheatEngineToolException.NotFound($"This activation owns no symbol named {symbolName}.",
				"List the owned symbols with symbol_list_registered.");
		}

		return _dispatch.Run(CheatEngineToolNames.SymbolUnregister, token =>
		{
			ResourceReleaseOutcome outcome = owned.Resource.Release(token);
			if (!outcome.IsRetryable)
			{
				_resources.Forget(owned.Resource);
				_registrations.Remove(owned.Lease);
			}

			SymbolReleaseResult result = new(owned.Name, HexFormat.Address(owned.Lease.Address), outcome);
			if (!outcome.IsComplete)
			{
				throw CheatEngineToolException.PartialEffect(
					$"The symbol {owned.Name} was not fully unregistered ({outcome.Kind}).", outcome.HostEffect, result,
					SymbolJsonContext.Default.SymbolReleaseResult, outcome.IsRetryable,
					outcome.IsRetryable
						? "Repeat symbol_unregister later."
						: "Check the symbol with symbol_resolve and remove it manually; it is no longer tracked.");
			}

			return result;
		}, cancellationToken);
	}

	/// <summary>Pages Cheat Engine's registered symbols.</summary>
	/// <param name="nameContains">A case-insensitive substring of the symbol name.</param>
	/// <param name="offset">The index of the first symbol to return.</param>
	/// <param name="limit">The most symbols to return.</param>
	/// <param name="cancellationToken">The request's token.</param>
	/// <returns>The page.</returns>
	[McpServerTool(Name = CheatEngineToolNames.SymbolListRegistered, Title = "List registered symbols",
		ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Page every symbol registered in Cheat Engine (by tables, Auto Assembler scripts, Lua and this plugin), marking with ownedByMcp the ones this activation registered and can unregister. Allocation symbols carry their size. Filter with nameContains, then page with offset and limit (at most 1000); at most 8192 symbols are copied.")]
	public RegisteredSymbolList ListRegistered(
		[Description("A case-insensitive substring of the symbol name; at most 256 characters.")]
		string? nameContains = null,
		[Description("The index of the first symbol to return.")]
		int offset = 0,
		[Description("The most symbols to return, 1 to 1000.")]
		int limit = 200,
		CancellationToken cancellationToken = default)
	{
		if (nameContains is { Length: > MaximumNameLength })
		{
			throw CheatEngineToolException.InvalidArgument("nameContains",
				$"must be at most {MaximumNameLength} characters.");
		}

		_ = Paging.Slice(Array.Empty<LuaRegisteredSymbol>(), offset, limit, MaximumLimit);
		LuaRegisteredSymbols copied = _dispatch.RunLua(CheatEngineToolNames.SymbolListRegistered,
			SymbolScripts.ListRegistered, SymbolLuaJsonContext.Default.LuaRegisteredSymbols, cancellationToken,
			SymbolScripts.MaximumRegisteredSymbols);
		Dictionary<string, OwnedSymbol> owned = _registrations.Snapshot();
		LuaRegisteredSymbol[] matching = string.IsNullOrEmpty(nameContains)
			? copied.Symbols
			:
			[
				.. copied.Symbols.Where(symbol =>
					symbol.Name.Contains(nameContains, StringComparison.OrdinalIgnoreCase))
			];
		PageSlice<LuaRegisteredSymbol> page = Paging.Slice(matching, offset, limit, MaximumLimit, copied.Truncated);
		RegisteredSymbolEntry[] entries =
		[
			.. page.Items.Select(symbol =>
			{
				OwnedSymbol? mine = owned.GetValueOrDefault(symbol.Name);
				return new RegisteredSymbolEntry(symbol.Name, mine is not null, symbol.Address,
					symbol.DoNotSave == true ? true : null, symbol.AllocSize, symbol.ProcessId,
					mine?.Resource.Descriptor.Id);
			})
		];
		return new RegisteredSymbolList(page.Total, page.Truncated, entries, page.NextOffset);
	}

	private static string RequireName(string? name)
	{
		string trimmed = name?.Trim() ?? string.Empty;
		if (trimmed.Length is 0 or > MaximumNameLength || trimmed.Any(char.IsControl) ||
			!string.Equals(trimmed, name, StringComparison.Ordinal))
		{
			throw CheatEngineToolException.InvalidArgument("name",
				$"must be 1 to {MaximumNameLength} characters, without surrounding spaces or control characters.");
		}

		return trimmed;
	}
}
