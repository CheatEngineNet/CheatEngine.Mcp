using CheatEngine.Client.Inspection;

namespace CheatEngine.Mcp.Tools.Symbol;

/// <summary>
///     The symbols this activation registered, by case-insensitive name. Each one is a Client lease that
///     <see cref="TargetResources" /> also tracks as a <c>symbol</c> resource, so <c>runtime_release_resources</c> and a
///     target change release it; the Client drains any that remain when the plugin is disabled.
/// </summary>
/// <remarks>
///     The list only guards names and the count: a name is reserved before its registration is dispatched and committed
///     or cancelled after it, and no lock is held during a dispatch or a Client call.
/// </remarks>
public sealed class SymbolRegistrations
{
	/// <summary>The most symbols one activation may own at a time.</summary>
	public const int MaximumOwned = 128;

	private readonly Lock _lock = new();
	private readonly Dictionary<string, OwnedSymbol> _owned = new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _reserved = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>How many symbols are owned or being registered.</summary>
	public int Count
	{
		get
		{
			lock (_lock)
			{
				Prune();
				return _owned.Count + _reserved.Count;
			}
		}
	}

	/// <summary>Reserves a name before its registration is dispatched.</summary>
	/// <param name="name">The symbol name.</param>
	/// <returns><see langword="null" /> when reserved, else why not.</returns>
	internal ReservationRefusal? TryReserve(string name)
	{
		lock (_lock)
		{
			Prune();
			if (_owned.ContainsKey(name) || _reserved.Contains(name))
			{
				return ReservationRefusal.AlreadyOwned;
			}

			if (_owned.Count + _reserved.Count >= MaximumOwned)
			{
				return ReservationRefusal.Full;
			}

			_reserved.Add(name);
			return null;
		}
	}

	/// <summary>Releases a reservation whose registration failed.</summary>
	/// <param name="name">The reserved name.</param>
	internal void CancelReservation(string name)
	{
		lock (_lock)
		{
			_reserved.Remove(name);
		}
	}

	/// <summary>Turns a reservation into an owned symbol.</summary>
	/// <param name="symbol">The registered symbol.</param>
	internal void Commit(OwnedSymbol symbol)
	{
		ArgumentNullException.ThrowIfNull(symbol);
		lock (_lock)
		{
			_reserved.Remove(symbol.Name);
			_owned[symbol.Name] = symbol;
		}
	}

	/// <summary>Finds an owned symbol whose lease is still active or awaits recovery.</summary>
	/// <param name="name">The symbol name, in any case.</param>
	/// <param name="symbol">The symbol when this method returns <see langword="true" />.</param>
	/// <returns><see langword="true" /> when this activation owns the name.</returns>
	internal bool TryGet(string name, out OwnedSymbol symbol)
	{
		lock (_lock)
		{
			Prune();
			return _owned.TryGetValue(name, out symbol!);
		}
	}

	/// <summary>Forgets an owned symbol, unless its name was registered again with another lease since.</summary>
	/// <param name="lease">The released lease.</param>
	internal void Remove(ISymbolRegistrationLease lease)
	{
		ArgumentNullException.ThrowIfNull(lease);
		lock (_lock)
		{
			string? name = _owned.FirstOrDefault(pair => ReferenceEquals(pair.Value.Lease, lease)).Key;
			if (name is not null)
			{
				_owned.Remove(name);
			}
		}
	}

	/// <summary>Copies the owned symbols.</summary>
	/// <returns>The owned symbols by name.</returns>
	internal Dictionary<string, OwnedSymbol> Snapshot()
	{
		lock (_lock)
		{
			Prune();
			return new Dictionary<string, OwnedSymbol>(_owned, StringComparer.OrdinalIgnoreCase);
		}
	}

	/// <summary>Drops leases the Client already released without leaving anything to recover.</summary>
	private void Prune()
	{
		foreach (string name in _owned.Where(static pair =>
					 pair.Value.Lease.IsReleased && !pair.Value.Lease.RequiresManualRecovery).Select(static pair =>
					 pair.Key).ToArray())
		{
			_owned.Remove(name);
		}
	}
}

/// <summary>A symbol this activation registered.</summary>
/// <param name="Name">The symbol name as registered.</param>
/// <param name="Lease">The Client lease that owns it.</param>
/// <param name="Resource">The resource that tracks the lease.</param>
/// <param name="DoNotSave">Whether saved tables omit it.</param>
internal sealed record OwnedSymbol(string Name, ISymbolRegistrationLease Lease, ITargetResource Resource,
	bool DoNotSave);

/// <summary>Why a name could not be reserved.</summary>
internal enum ReservationRefusal
{
	/// <summary>This activation already owns, or is registering, the name.</summary>
	AlreadyOwned,

	/// <summary>This activation already owns <see cref="SymbolRegistrations.MaximumOwned" /> symbols.</summary>
	Full
}
