using System.ComponentModel;

using CheatEngine.Client;
using CheatEngine.Client.Inspection;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.SDK.Engine.Values;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools;

/// <summary>Owns symbols registered by this MCP server for the current Client activation.</summary>
[McpServerToolType]
public sealed class SymbolRegistryTool : IDisposable
{
	private const int MaximumSymbolCount = 128;
	private const int MaximumSymbolNameLength = 256;
	private readonly ICheatEngineClient _client;
	private readonly Dictionary<string, ISymbolRegistrationLease> _symbols = new(StringComparer.OrdinalIgnoreCase);
	private readonly object _symbolsLock = new();
	private readonly TargetResources? _targetResources;

	public SymbolRegistryTool(ICheatEngineClient client, TargetResources? targetResources = null)
	{
		_client = client;
		_targetResources = targetResources;
	}

	public void Dispose()
	{
		KeyValuePair<string, ISymbolRegistrationLease>[] symbols;
		lock (_symbolsLock)
		{
			symbols = [.. _symbols];
		}

		foreach ((string name, ISymbolRegistrationLease symbol) in symbols)
		{
			LeaseReleaseOutcome outcome = symbol.Release();
			if (!outcome.IsRetryable)
			{
				_targetResources?.Forget(symbol);
				lock (_symbolsLock)
				{
					_symbols.Remove(name);
				}
			}
		}
	}

	[McpServerTool(Name = "register_symbol")]
	[Description("Register a Client-owned target symbol. The server releases it on disable.")]
	public object RegisterSymbol(
		[Description("Session-wide symbol name. Use a distinctive name to avoid collisions.")]
		string name,
		[Description("Target address as hexadecimal.")]
		string address,
		[Description("Omit the symbol when Cheat Engine saves a table.")]
		bool doNotSave = true)
	{
		return ToolExecution.Run(_client, () =>
		{
			if (string.IsNullOrWhiteSpace(name))
			{
				return ToolExecution.Error("name is required.");
			}

			if (name.Length > MaximumSymbolNameLength)
			{
				return ToolExecution.Error($"name must not exceed {MaximumSymbolNameLength} characters.");
			}

			lock (_symbolsLock)
			{
				if (_symbols.Count >= MaximumSymbolCount)
				{
					return ToolExecution.Error($"At most {MaximumSymbolCount} symbols may be owned by this server.");
				}
			}

			Address target = ToolExecution.Address(_client, address);
			lock (_symbolsLock)
			{
				if (_symbols.ContainsKey(name))
				{
					return ToolExecution.Error("This server already owns a symbol with that name.");
				}
			}

			ISymbolRegistrationLease lease =
				_client.Inspection.RegisterSymbol(new SymbolRegistration(name, target, doNotSave));
			lock (_symbolsLock)
			{
				_symbols.Add(lease.Name, lease);
			}

			_targetResources?.Track(lease, "symbol", () =>
			{
				lock (_symbolsLock)
				{
					_symbols.Remove(lease.Name);
				}
			}, lease.Name, HexFormat.Address(lease.Address));

			return new { success = true, name = lease.Name, address = $"0x{lease.Address.Value:X}" };
		});
	}

	[McpServerTool(Name = "unregister_symbol")]
	[Description("Release a symbol that this MCP server registered in the current activation.")]
	public object UnregisterSymbol([Description("Name returned by register_symbol.")] string name)
	{
		return ToolExecution.Run(_client, () =>
		{
			ISymbolRegistrationLease? lease;
			lock (_symbolsLock)
			{
				if (!_symbols.TryGetValue(name, out lease))
				{
					return ToolExecution.Error("The symbol is not owned by this server.");
				}
			}

			LeaseReleaseOutcome release = lease.Release();
			if (!release.IsRetryable)
			{
				_targetResources?.Forget(lease);
				lock (_symbolsLock)
				{
					_symbols.Remove(name);
				}
			}

			return new
			{
				success = release.IsComplete,
				name,
				address = $"0x{lease.Address.Value:X}",
				release = release.Kind.ToString(),
				hostEffect = release.HostEffect.ToString(),
				retryable = release.IsRetryable,
				requiresManualRecovery = release.RequiresManualRecovery
			};
		});
	}

	[McpServerTool(Name = "enum_registered_symbols")]
	[Description("List symbols currently owned by this MCP server.")]
	public object EnumerateRegisteredSymbols()
	{
		return ToolExecution.Run(_client, () =>
		{
			object[] symbols;
			lock (_symbolsLock)
			{
				symbols =
				[
					.. _symbols.Values.Select(static lease =>
						(object) new { name = lease.Name, address = $"0x{lease.Address.Value:X}" })
				];
			}

			return new { success = true, symbols };
		});
	}
}
