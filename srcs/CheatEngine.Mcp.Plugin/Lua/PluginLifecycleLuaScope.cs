using CheatEngine.Client;

namespace CheatEngine.Mcp.Plugin.Lua;

/// <summary>Tracks one activation's synchronous fixed-Lua deactivation windows without process-wide mutable state.</summary>
internal sealed class PluginLifecycleLuaScope
{
	private ActiveScope? _active;

	internal IDisposable BeginResourceCleanup(ICheatEngineClient client)
	{
		return Begin(client, ScopeKind.ResourceCleanup,
			"MCP resource cleanup requires the stopping activation's Cheat Engine main thread.");
	}

	internal IDisposable BeginDeactivationNotification(ICheatEngineClient client)
	{
		return Begin(client, ScopeKind.DeactivationNotification,
			"MCP lifecycle notification requires the stopping activation's Cheat Engine main thread.");
	}

	internal bool IsResourceCleanup(ICheatEngineClient client)
	{
		return IsCurrent(client, ScopeKind.ResourceCleanup);
	}

	internal bool IsDeactivationNotification(ICheatEngineClient client)
	{
		return IsCurrent(client, ScopeKind.DeactivationNotification);
	}

	private Scope Begin(ICheatEngineClient client, ScopeKind kind, string error)
	{
		ArgumentNullException.ThrowIfNull(client);
		if (!client.Stopping.IsCancellationRequested || !client.Dispatcher.IsMainThread)
		{
			throw new InvalidOperationException(error);
		}

		ActiveScope? previous = Volatile.Read(ref _active);
		ActiveScope scope = new(client, kind, Environment.CurrentManagedThreadId, previous);
		Volatile.Write(ref _active, scope);
		return new Scope(this, scope);
	}

	private bool IsCurrent(ICheatEngineClient client, ScopeKind kind)
	{
		ActiveScope? scope = Volatile.Read(ref _active);
		return scope is not null && scope.Kind == kind && ReferenceEquals(scope.Client, client)
			&& scope.ThreadId == Environment.CurrentManagedThreadId
			&& client.Stopping.IsCancellationRequested && client.Dispatcher.IsMainThread;
	}

	private void Exit(ActiveScope scope)
	{
		if (!ReferenceEquals(Volatile.Read(ref _active), scope))
		{
			throw new InvalidOperationException("MCP lifecycle Lua scopes must be disposed in reverse order.");
		}

		Volatile.Write(ref _active, scope.Previous);
	}

	private enum ScopeKind
	{
		ResourceCleanup,
		DeactivationNotification
	}

	private sealed record ActiveScope(ICheatEngineClient Client, ScopeKind Kind, int ThreadId, ActiveScope? Previous);

	private sealed class Scope(PluginLifecycleLuaScope owner, ActiveScope scope) : IDisposable
	{
		private int _disposed;

		public void Dispose()
		{
			if (Interlocked.Exchange(ref _disposed, 1) == 0)
			{
				owner.Exit(scope);
			}
		}
	}
}
