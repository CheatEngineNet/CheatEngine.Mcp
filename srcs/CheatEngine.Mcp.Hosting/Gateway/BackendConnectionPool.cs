using System.Security.Cryptography;
using System.Text;

using CheatEngine.Mcp.Hosting.Discovery;

using Microsoft.Extensions.Logging;

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace CheatEngine.Mcp.Hosting.Gateway;

/// <summary>
///     Reuses one backend <see cref="McpClient" /> per verified registry record, so a routed call costs one POST instead
///     of a handshake. The backends are stateless, so a reused client carries no session state; the router still
///     verifies the backend identity before every call.
/// </summary>
/// <remarks>
///     A connection is keyed by instance, activation, endpoint and the SHA-256 of the token, so a changed record never
///     reuses an old client. It is evicted on any failure, timeout or changed record, after <see cref="IdleLifetime" />
///     without use, or as the least recently used beyond <see cref="Capacity" />. An evicted connection closes once its
///     last call returned. Nothing is ever re-sent: a failed call is reported, and only the next call connects again.
/// </remarks>
internal sealed partial class BackendConnectionPool : IAsyncDisposable, IDisposable
{
	/// <summary>The protocol version of every backend connection; the upstream negotiation is independent.</summary>
	internal const string BackendProtocolVersion = "2025-06-18";

	/// <summary>The default number of pooled connections.</summary>
	internal const int DefaultCapacity = 32;

	/// <summary>How long connecting and the <c>initialize</c> handshake may take.</summary>
	internal static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

	/// <summary>The default unused lifetime of a pooled connection.</summary>
	internal static readonly TimeSpan DefaultIdleLifetime = TimeSpan.FromMinutes(5);

	private readonly List<Task> _closing = [];
	private readonly Dictionary<BackendConnectionKey, BackendConnection> _connections = [];
	private readonly Lock _gate = new();
	private readonly ILogger _logger;
	private readonly ILoggerFactory _loggers;
	private readonly TimeProvider _time;
	private bool _disposed;

	/// <summary>Creates the gateway's pool with the production limits.</summary>
	/// <param name="time">The clock of the idle lifetime.</param>
	/// <param name="loggers">The gateway's loggers, shared with the backend clients.</param>
	public BackendConnectionPool(TimeProvider time, ILoggerFactory loggers)
		: this(time, loggers, DefaultCapacity, DefaultIdleLifetime)
	{
	}

	/// <summary>Creates a pool with explicit limits.</summary>
	/// <param name="time">The clock of the idle lifetime.</param>
	/// <param name="loggers">The loggers shared with the backend clients.</param>
	/// <param name="capacity">The number of pooled connections.</param>
	/// <param name="idleLifetime">How long an unused connection stays pooled.</param>
	internal BackendConnectionPool(TimeProvider time, ILoggerFactory loggers, int capacity, TimeSpan idleLifetime)
	{
		ArgumentNullException.ThrowIfNull(time);
		ArgumentNullException.ThrowIfNull(loggers);
		ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
		ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(idleLifetime, TimeSpan.Zero);
		_time = time;
		_loggers = loggers;
		_logger = loggers.CreateLogger(typeof(BackendConnectionPool).FullName!);
		Capacity = capacity;
		IdleLifetime = idleLifetime;
	}

	/// <summary>The number of pooled connections.</summary>
	internal int Capacity
	{
		get;
	}

	/// <summary>How long an unused connection stays pooled.</summary>
	internal TimeSpan IdleLifetime
	{
		get;
	}

	/// <summary>The number of connections currently pooled.</summary>
	internal int Count
	{
		get
		{
			lock (_gate)
			{
				return _connections.Count;
			}
		}
	}

	public async ValueTask DisposeAsync()
	{
		Task[] pending;
		lock (_gate)
		{
			CloseAllLocked();
			pending = [.. _closing];
		}

		try
		{
			await Task.WhenAll(pending).ConfigureAwait(false);
		}
		catch (Exception exception)
		{
			LogCloseFailed(exception);
		}
	}

	public void Dispose()
	{
		// Closing only starts here; a synchronous owner must not wait for backend round trips.
		lock (_gate)
		{
			CloseAllLocked();
		}
	}

	/// <summary>
	///     Leases the pooled client of a verified record, connecting it first when needed. The caller must dispose the
	///     lease, and evict it through <see cref="BackendLease.Evict" /> on any failure.
	/// </summary>
	/// <param name="instance">The record whose identity the caller has just verified.</param>
	/// <param name="cancellationToken">The caller's cancellation.</param>
	/// <returns>The lease.</returns>
	/// <exception cref="InstanceUnavailableException">The client could not connect; nothing was sent to a tool.</exception>
	internal async Task<BackendLease> RentAsync(InstanceDescriptor instance, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(instance);
		BackendConnectionKey key = BackendConnectionKey.From(instance);
		BackendConnection connection;
		lock (_gate)
		{
			ObjectDisposedException.ThrowIf(_disposed, this);
			long now = _time.GetTimestamp();
			foreach (BackendConnection pooled in _connections.Values.ToArray())
			{
				if (pooled.Key != key && string.Equals(pooled.Key.InstanceId, key.InstanceId, StringComparison.Ordinal))
				{
					RemoveLocked(pooled, "changed record");
				}
				else if (pooled.Leases == 0 && _time.GetElapsedTime(pooled.LastUsed, now) >= IdleLifetime)
				{
					RemoveLocked(pooled, "idle");
				}
			}

			if (!_connections.TryGetValue(key, out connection!))
			{
				connection = new BackendConnection(key, instance, _loggers);
				_connections.Add(key, connection);
				LogOpened(_logger, key.InstanceId);
				while (_connections.Count > Capacity)
				{
					RemoveLocked(_connections.Values.Where(candidate => !ReferenceEquals(candidate, connection))
						.MinBy(static candidate => candidate.LastUsed)!, "capacity");
				}
			}

			connection.LastUsed = now;
			connection.Leases++;
		}

		try
		{
			McpClient client = await connection.Connected.WaitAsync(cancellationToken).ConfigureAwait(false);
			return new BackendLease(this, connection, client);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			// The shared handshake keeps running for the next caller; only this caller's lease ends.
			Release(connection);
			throw;
		}
		catch (Exception exception)
		{
			Evict(connection, "connect failed");
			Release(connection);
			throw new InstanceUnavailableException(DescribeConnectFailure(exception), exception);
		}
	}

	/// <summary>Evicts every pooled connection of an instance, for example after a failed identity check.</summary>
	/// <param name="instanceId">The instance.</param>
	/// <param name="reason">Why, for the log.</param>
	internal void Evict(string instanceId, string reason)
	{
		lock (_gate)
		{
			foreach (BackendConnection connection in _connections.Values
				         .Where(candidate =>
					         string.Equals(candidate.Key.InstanceId, instanceId, StringComparison.Ordinal))
				         .ToArray())
			{
				RemoveLocked(connection, reason);
			}
		}
	}

	private void Evict(BackendConnection connection, string reason)
	{
		lock (_gate)
		{
			RemoveLocked(connection, reason);
		}
	}

	private void Release(BackendConnection connection)
	{
		lock (_gate)
		{
			connection.Leases--;
			connection.LastUsed = _time.GetTimestamp();
			if (connection.Evicted && connection.Leases == 0)
			{
				CloseLocked(connection);
			}
		}
	}

	private void RemoveLocked(BackendConnection connection, string reason)
	{
		if (connection.Evicted)
		{
			return;
		}

		connection.Evicted = true;
		if (_connections.TryGetValue(connection.Key, out BackendConnection? pooled) &&
		    ReferenceEquals(pooled, connection))
		{
			_connections.Remove(connection.Key);
		}

		LogEvicted(_logger, connection.Key.InstanceId, reason);
		if (connection.Leases == 0)
		{
			CloseLocked(connection);
		}
	}

	private void CloseAllLocked()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		foreach (BackendConnection connection in _connections.Values.ToArray())
		{
			RemoveLocked(connection, "gateway stopping");
		}
	}

	private void CloseLocked(BackendConnection connection)
	{
		_closing.RemoveAll(static task => task.IsCompleted);
		_closing.Add(CloseQuietlyAsync(connection));
	}

	private async Task CloseQuietlyAsync(BackendConnection connection)
	{
		try
		{
			await connection.DisposeAsync().ConfigureAwait(false);
		}
		catch (Exception exception)
		{
			// Closing a broken connection may fail again; the pool has already forgotten it.
			LogCloseFailed(exception);
		}
	}

	private static string DescribeConnectFailure(Exception exception)
	{
		return exception switch
		{
			HttpRequestException http => BackendHttp.Describe(http),
			OperationCanceledException or TimeoutException =>
				$"The instance did not complete the MCP handshake within {ConnectTimeout.TotalSeconds:0} s.",
			_ => "The instance did not complete the MCP handshake."
		};
	}

	[LoggerMessage(Level = LogLevel.Debug, Message = "Opened a backend connection to instance {InstanceId}.")]
	private static partial void LogOpened(ILogger logger, string instanceId);

	[LoggerMessage(Level = LogLevel.Debug,
		Message = "Evicted the backend connection to instance {InstanceId}: {Reason}.")]
	private static partial void LogEvicted(ILogger logger, string instanceId, string reason);

	private void LogCloseFailed(Exception exception)
	{
		// Only the type: a transport message is never logged next to a token-bearing connection.
		if (_logger.IsEnabled(LogLevel.Debug))
		{
			string exceptionType = exception.GetType().Name;
			LogCloseFailed(_logger, exceptionType);
		}
	}

	[LoggerMessage(Level = LogLevel.Debug, Message = "Closing a backend connection failed with {ExceptionType}.")]
	private static partial void LogCloseFailed(ILogger logger, string exceptionType);

	/// <summary>One pooled backend client and its transport; the token lives only in the transport's headers.</summary>
	internal sealed class BackendConnection : IAsyncDisposable
	{
		private readonly CancellationTokenSource _closing = new();
		private readonly Lazy<Task<McpClient>> _connect;
		private readonly HttpClientTransport _transport;

		internal BackendConnection(BackendConnectionKey key, InstanceDescriptor instance, ILoggerFactory loggers)
		{
			Key = key;
			_transport = new HttpClientTransport(new HttpClientTransportOptions
			{
				Endpoint = new Uri(instance.Endpoint),
				Name = "CheatEngine.Mcp.Gateway",
				TransportMode = HttpTransportMode.StreamableHttp,
				ConnectionTimeout = ConnectTimeout,
				// A stateless backend has no stream to resume or listen on; a lost call is reported, never replayed.
				MaxReconnectionAttempts = 0,
				EnableStandaloneGetStream = false,
				AdditionalHeaders = new Dictionary<string, string>(StringComparer.Ordinal)
				{
					["Authorization"] = $"Bearer {instance.AccessToken}"
				}
			}, BackendHttp.CreateClient(), loggers, true);
			_connect = new Lazy<Task<McpClient>>(() => ConnectAsync(loggers),
				LazyThreadSafetyMode.ExecutionAndPublication);
		}

		internal BackendConnectionKey Key
		{
			get;
		}

		/// <summary>The shared handshake; every lease awaits the same client.</summary>
		internal Task<McpClient> Connected => _connect.Value;

		internal int Leases
		{
			get;
			set;
		}

		internal long LastUsed
		{
			get;
			set;
		}

		internal bool Evicted
		{
			get;
			set;
		}

		public async ValueTask DisposeAsync()
		{
			await _closing.CancelAsync().ConfigureAwait(false);
			try
			{
				if (_connect.IsValueCreated)
				{
					McpClient? client = null;
					try
					{
						client = await _connect.Value.ConfigureAwait(false);
					}
					catch (Exception)
					{
						// A failed handshake left no client to close.
					}

					if (client is not null)
					{
						await client.DisposeAsync().ConfigureAwait(false);
					}
				}
			}
			finally
			{
				await _transport.DisposeAsync().ConfigureAwait(false);
				_closing.Dispose();
			}
		}

		private async Task<McpClient> ConnectAsync(ILoggerFactory loggers)
		{
			using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(_closing.Token);
			timeout.CancelAfter(ConnectTimeout);
			return await McpClient.CreateAsync(_transport,
				new McpClientOptions
				{
					ClientInfo = new Implementation { Name = "CheatEngine.Mcp.Gateway", Version = "2.0.0" },
					Capabilities = new ClientCapabilities(),
					ProtocolVersion = BackendProtocolVersion,
					InitializationTimeout = ConnectTimeout
				}, loggers, timeout.Token).ConfigureAwait(false);
		}
	}

	/// <summary>One routed call's use of a pooled client.</summary>
	internal sealed class BackendLease : IDisposable
	{
		private readonly BackendConnection _connection;
		private readonly BackendConnectionPool _pool;
		private int _released;

		internal BackendLease(BackendConnectionPool pool, BackendConnection connection, McpClient client)
		{
			_pool = pool;
			_connection = connection;
			Client = client;
		}

		internal McpClient Client
		{
			get;
		}

		public void Dispose()
		{
			if (Interlocked.Exchange(ref _released, 1) == 0)
			{
				_pool.Release(_connection);
			}
		}

		/// <summary>Removes the client from the pool; it closes once this lease is disposed.</summary>
		/// <param name="reason">Why, for the log.</param>
		internal void Evict(string reason)
		{
			_pool.Evict(_connection, reason);
		}
	}
}

/// <summary>
///     Identifies a pooled connection by everything a registry record binds. The token appears only as its SHA-256, so
///     the key can be logged or compared without exposing it.
/// </summary>
/// <param name="InstanceId">The routing identifier.</param>
/// <param name="ActivationId">The activation that published the record.</param>
/// <param name="Endpoint">The loopback endpoint.</param>
/// <param name="TokenHash">The uppercase hexadecimal SHA-256 of the access token.</param>
internal readonly record struct BackendConnectionKey(
	string InstanceId,
	Guid ActivationId,
	string Endpoint,
	string TokenHash)
{
	/// <summary>The key of a registry record.</summary>
	/// <param name="instance">The record.</param>
	/// <returns>Its key.</returns>
	internal static BackendConnectionKey From(InstanceDescriptor instance)
	{
		ArgumentNullException.ThrowIfNull(instance);
		return new BackendConnectionKey(instance.InstanceId, instance.ActivationId, instance.Endpoint,
			Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(instance.AccessToken))));
	}
}
