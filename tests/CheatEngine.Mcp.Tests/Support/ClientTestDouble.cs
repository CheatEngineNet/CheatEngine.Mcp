using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.ExceptionServices;

using CheatEngine.Client;
using CheatEngine.Client.Dispatching;

namespace CheatEngine.Mcp.Tests.Support;

internal static class ClientTestDouble
{
	private static readonly ICheatEngineDispatcher InlineDispatcher =
		Create<ICheatEngineDispatcher>((method, arguments) =>
		{
			if (method.Name == "get_IsMainThread")
			{
				return true;
			}

			if (method.Name.StartsWith("Invoke", StringComparison.Ordinal) && arguments?[0] is Delegate callback)
			{
				return callback.DynamicInvoke();
			}

			throw new NotSupportedException($"No dispatcher behavior was configured for {method.Name}.");
		});

	public static T Create<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
	{
		ArgumentNullException.ThrowIfNull(handler);
		T instance = DispatchProxy.Create<T, Proxy>();
		((Proxy) (object) instance).Handler = handler;
		return instance;
	}

	public static ICheatEngineClient Client(params (string PropertyName, object Value)[] properties)
	{
		return Client(InlineDispatcher, CancellationToken.None, properties);
	}

	/// <summary>A Client double with a chosen dispatcher and stopping token.</summary>
	public static ICheatEngineClient Client(ICheatEngineDispatcher dispatcher, CancellationToken stopping,
		params (string PropertyName, object Value)[] properties)
	{
		ArgumentNullException.ThrowIfNull(dispatcher);
		Dictionary<string, object> values = properties.ToDictionary(pair => pair.PropertyName, pair => pair.Value,
			StringComparer.Ordinal);
		values[nameof(ICheatEngineClient.Dispatcher)] = dispatcher;
		return Create<ICheatEngineClient>((method, _) => method.Name switch
		{
			"get_Stopping" => stopping,
			_ when method.Name.StartsWith("get_", StringComparison.Ordinal) &&
				   values.TryGetValue(method.Name[4..], out object? value) => value,
			_ => throw new NotSupportedException($"No client double value was configured for {method.Name}.")
		});
	}

	[SuppressMessage("Performance", "CA1852:Seal internal types",
		Justification = "DispatchProxy dynamically subclasses this type.")]
	private class Proxy : DispatchProxy
	{
		public Func<MethodInfo, object?[]?, object?>? Handler
		{
			get;
			set;
		}

		protected override object? Invoke(MethodInfo? targetMethod, object?[]? arguments)
		{
			ArgumentNullException.ThrowIfNull(targetMethod);
			try
			{
				return Handler!(targetMethod, arguments);
			}
			catch (TargetInvocationException exception) when (exception.InnerException is { } innerException)
			{
				ExceptionDispatchInfo.Capture(innerException).Throw();
				throw;
			}
		}
	}
}
