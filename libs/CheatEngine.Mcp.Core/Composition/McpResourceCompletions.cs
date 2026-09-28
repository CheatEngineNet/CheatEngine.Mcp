using System.Collections.Frozen;
using System.Reflection;

using Microsoft.Extensions.DependencyInjection;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Core.Composition;

/// <summary>
///     A live composition's <c>completion/complete</c> handler: it offers the current values of the live template
///     variables marked with <see cref="McpCompletionAttribute" />, as their container lists them
///     (<see cref="IMcpCompletionSource" />). The SDK then appends a variable's <c>[AllowedValues]</c>, if it has any.
/// </summary>
/// <remarks>
///     <para>
///         A completion fires on every keystroke, so it never fails and never waits long: any failure, including a
///         refused or slow dispatch, offers no values, and a request waits at most <see cref="MaximumWait" /> for a
///         listing, which then completes in the background for the next keystroke.
///     </para>
///     <para>
///         A <see cref="McpCompletionCost.Memory" /> listing runs for every request, on the request thread. A
///         <see cref="McpCompletionCost.Dispatch" /> listing runs off it, is shared by every template of its
///         container, served from memory for <see cref="TimeToLive" /> and started at most once per
///         <see cref="RefreshInterval" />; a failed listing offers nothing until one succeeds. At most one dispatched
///         listing runs at a time across the whole server, so completions never hold more than one of the
///         activation's dispatch slots, and a request for another variable meanwhile offers nothing. The values
///         belong to the target-selection epoch they were read in: once any listing observed a newer epoch, the values
///         of an older epoch are dropped.
///     </para>
///     <para>
///         One handler serves a whole server container (<see cref="Create" />): the backend's stateless HTTP
///         transport configures new server options for every request, and the cache, the rate limit and the running
///         listing must outlive each of them.
///     </para>
///     <para>Values match by prefix ignoring case (<see cref="McpCompletions.Match" />).</para>
/// </remarks>
internal sealed class McpResourceCompletions
{
	/// <summary>How long a dispatched listing is offered without dispatching again.</summary>
	internal static readonly TimeSpan TimeToLive = TimeSpan.FromSeconds(5);

	/// <summary>The shortest interval between two dispatched listings of one variable, failed ones included.</summary>
	internal static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(1);

	/// <summary>The longest a request waits for a dispatched listing before it offers nothing.</summary>
	internal static readonly TimeSpan MaximumWait = TimeSpan.FromMilliseconds(750);

	private readonly Lock _gate = new();
	private readonly Listing[] _listings;
	private readonly FrozenDictionary<string, FrozenDictionary<string, Listing>> _templates;
	private readonly TimeProvider _time;
	private readonly TimeSpan _wait;

	// The dispatched listing that is running, if any; guarded by _gate.
	private Listing? _dispatching;

	// The newest target-selection epoch any listing observed; guarded by _gate.
	private long? _epoch;

	/// <summary>Finds the marked variables of the live templates whose container lists values.</summary>
	/// <param name="resources">The server's resources.</param>
	/// <param name="targets">The activation's containers.</param>
	/// <param name="time">The clock of the cache.</param>
	/// <param name="wait">How long a request waits for a dispatched listing (<see cref="MaximumWait" />).</param>
	internal McpResourceCompletions(IEnumerable<McpServerResource> resources, McpPrimitiveTargets targets,
		TimeProvider time, TimeSpan? wait = null)
	{
		ArgumentNullException.ThrowIfNull(resources);
		ArgumentNullException.ThrowIfNull(targets);
		ArgumentNullException.ThrowIfNull(time);
		_time = time;
		_wait = wait ?? MaximumWait;
		// One listing per container and variable, shared by its templates, such as modules/{module} and its exports.
		Dictionary<(Type Container, string Variable), Listing> listings = [];
		Dictionary<string, FrozenDictionary<string, Listing>> templates = new(StringComparer.Ordinal);
		foreach (McpServerResource resource in resources)
		{
			if (McpPrimitiveOrigin.MethodOf(resource) is not { IsStatic: false } method ||
				(method.ReflectedType ?? method.DeclaringType) is not { } container ||
				!targets.TryGet(container, out object? target) || target is not IMcpCompletionSource source)
			{
				continue;
			}

			Dictionary<string, Listing> variables = new(StringComparer.Ordinal);
			foreach (ParameterInfo parameter in method.GetParameters())
			{
				if (parameter.Name is not { } name ||
					parameter.GetCustomAttribute<McpCompletionAttribute>() is not { } marker)
				{
					continue;
				}

				if (!listings.TryGetValue((container, name), out Listing? listing))
				{
					listing = new Listing(source, name, marker.Cost);
					listings.Add((container, name), listing);
				}

				variables[name] = listing;
			}

			if (variables.Count > 0)
			{
				templates[resource.ProtocolResourceTemplate.UriTemplate] =
					variables.ToFrozenDictionary(StringComparer.Ordinal);
			}
		}

		_listings = [.. listings.Values];
		_templates = templates.ToFrozenDictionary(StringComparer.Ordinal);
	}

	/// <summary>
	///     Creates the one handler of a live composition's server container, over the container's resources and its
	///     <see cref="TimeProvider" />, if it registers one.
	/// </summary>
	/// <param name="services">The transport container, whose resources are the composition's.</param>
	/// <param name="targets">The activation's containers.</param>
	/// <returns>The handler every server options instance of the container installs.</returns>
	internal static McpResourceCompletions Create(IServiceProvider services, McpPrimitiveTargets targets)
	{
		ArgumentNullException.ThrowIfNull(services);
		return new McpResourceCompletions(services.GetServices<McpServerResource>(), targets,
			services.GetService<TimeProvider>() ?? TimeProvider.System);
	}

	/// <summary>
	///     Installs this handler, and the completions capability, on one server options instance, unless the host
	///     already installed a handler of its own.
	/// </summary>
	/// <param name="options">The server options being configured.</param>
	internal void Install(McpServerOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		options.Handlers.CompleteHandler ??= CompleteAsync;
		options.Capabilities ??= new ServerCapabilities();
		options.Capabilities.Completions ??= new CompletionsCapability();
	}

	/// <summary>Answers one <c>completion/complete</c> request.</summary>
	/// <param name="context">The request.</param>
	/// <param name="cancellationToken">The request's cancellation, which is rethrown as is.</param>
	/// <returns>The matching values; no values for anything this handler does not complete.</returns>
	internal ValueTask<CompleteResult> CompleteAsync(RequestContext<CompleteRequestParams> context,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(context);
		return CompleteAsync(context.Params, cancellationToken);
	}

	/// <summary>Answers one <c>completion/complete</c> request from its parameters.</summary>
	/// <param name="request">The request parameters.</param>
	/// <param name="cancellationToken">The request's cancellation, which is rethrown as is.</param>
	/// <returns>The matching values; no values for anything this handler does not complete.</returns>
	internal async ValueTask<CompleteResult> CompleteAsync(CompleteRequestParams? request,
		CancellationToken cancellationToken)
	{
		if (request is not { Ref: ResourceTemplateReference { Uri: { } template }, Argument: { } argument } ||
			!_templates.TryGetValue(template, out FrozenDictionary<string, Listing>? variables) ||
			!variables.TryGetValue(argument.Name, out Listing? listing))
		{
			return new CompleteResult();
		}

		IReadOnlyList<string> values = listing.Cost is McpCompletionCost.Memory
			? ListNow(listing, cancellationToken)
			: await ListCachedAsync(listing, cancellationToken).ConfigureAwait(false);
		return new CompleteResult { Completion = McpCompletions.Match(values, argument.Value) };
	}

	private static IReadOnlyList<string> ListNow(Listing listing, CancellationToken cancellationToken)
	{
		try
		{
			return listing.Source.ListCompletionValues(listing.Variable, cancellationToken).Values;
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception)
		{
			// A completion never fails: a listing that cannot be read offers nothing.
			return [];
		}
	}

	private async Task<IReadOnlyList<string>> ListCachedAsync(Listing listing, CancellationToken cancellationToken)
	{
		Task<IReadOnlyList<string>> pending;
		lock (_gate)
		{
			long now = _time.GetTimestamp();
			if (listing.Values is { } fresh && _time.GetElapsedTime(listing.ListedAt, now) < TimeToLive)
			{
				return fresh;
			}

			if (listing.Refresh is { IsCompleted: false } running)
			{
				pending = running;
			}
			else if (listing.StartedAt is { } started && _time.GetElapsedTime(started, now) < RefreshInterval)
			{
				// The last listing started less than a second ago and failed: no dispatch per keystroke.
				return [];
			}
			else if (_dispatching is not null)
			{
				// Another variable's listing holds a dispatch slot: completions never take a second one.
				return [];
			}
			else
			{
				listing.StartedAt = now;
				_dispatching = listing;
				// The listing runs off the request thread with no request token, so a caller that stops waiting leaves
				// it to complete for the next keystroke; its dispatch still observes the activation's stopping.
				pending = Task.Run(() => Refresh(listing), CancellationToken.None);
				listing.Refresh = pending;
			}
		}

		try
		{
			return await pending.WaitAsync(_wait, _time, cancellationToken).ConfigureAwait(false);
		}
		catch (TimeoutException)
		{
			return [];
		}
	}

	private IReadOnlyList<string> Refresh(Listing listing)
	{
		McpCompletionValues? listed;
		try
		{
			listed = listing.Source.ListCompletionValues(listing.Variable, CancellationToken.None);
		}
		catch (Exception)
		{
			// Not attached, busy, stopping or a host failure: nothing is offered until a listing succeeds.
			listed = null;
		}

		lock (_gate)
		{
			if (ReferenceEquals(_dispatching, listing))
			{
				_dispatching = null;
			}

			if (listed is null || listed.SelectionEpoch < _epoch)
			{
				// A failure, or a listing that started before the target changed.
				listing.Values = null;
				return [];
			}

			if (listed.SelectionEpoch is { } observed && observed != _epoch)
			{
				_epoch = observed;
				foreach (Listing other in _listings)
				{
					if (other.Epoch is { } held && held != observed)
					{
						// The target changed: the next request lists again at once.
						other.Values = null;
						other.StartedAt = null;
					}
				}
			}

			IReadOnlyList<string> values =
			[
				.. (listed.Values ?? []).Where(static value => !string.IsNullOrEmpty(value))
					.Distinct(StringComparer.Ordinal)
			];
			listing.Values = values;
			listing.Epoch = listed.SelectionEpoch;
			listing.ListedAt = _time.GetTimestamp();
			return values;
		}
	}

	/// <summary>One completed variable of one container and its cached values, guarded by the handler's gate.</summary>
	private sealed class Listing(IMcpCompletionSource source, string variable, McpCompletionCost cost)
	{
		internal IMcpCompletionSource Source
		{
			get;
		} = source;

		internal string Variable
		{
			get;
		} = variable;

		internal McpCompletionCost Cost
		{
			get;
		} = cost;

		internal IReadOnlyList<string>? Values
		{
			get;
			set;
		}

		internal long? Epoch
		{
			get;
			set;
		}

		internal long ListedAt
		{
			get;
			set;
		}

		internal long? StartedAt
		{
			get;
			set;
		}

		internal Task<IReadOnlyList<string>>? Refresh
		{
			get;
			set;
		}
	}
}
