using System.Globalization;

using CheatEngine.Mcp.Hosting.Discovery;

namespace CheatEngine.Mcp.Hosting.Gateway;

/// <summary>The gateway's command-line contract; it must agree with the plugins on the instance directory.</summary>
public sealed class GatewayOptions
{
	/// <summary>The command-line argument that sets the registry directory.</summary>
	public const string InstanceDirectoryArgument = "--instance-directory";

	/// <summary>The command-line argument that sets <see cref="CallTimeout" /> in whole seconds.</summary>
	public const string CallTimeoutArgument = "--call-timeout-seconds";

	/// <summary>The environment variable that sets the registry directory.</summary>
	public const string InstanceDirectoryVariable = "MCP_INSTANCE_DIRECTORY";

	/// <summary>The environment variable that sets <see cref="CallTimeout" /> in whole seconds.</summary>
	public const string CallTimeoutVariable = "MCP_GATEWAY_CALL_TIMEOUT_SECONDS";

	/// <summary>The smallest accepted call timeout, in seconds.</summary>
	public const int MinimumCallTimeoutSeconds = 5;

	/// <summary>The largest accepted call timeout, in seconds.</summary>
	public const int MaximumCallTimeoutSeconds = 3600;

	/// <summary>The call timeout when neither the argument nor the environment variable sets one.</summary>
	public static readonly TimeSpan DefaultCallTimeout = TimeSpan.FromSeconds(45);

	/// <summary>Creates options directly; tests use it for short timeouts that the command line refuses.</summary>
	/// <param name="instanceDirectory">The absolute registry directory.</param>
	/// <param name="callTimeout">How long one routed call may take before the gateway reports a timeout.</param>
	internal GatewayOptions(string instanceDirectory, TimeSpan callTimeout)
	{
		ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(callTimeout, TimeSpan.Zero);
		InstanceDirectory = Path.IsPathFullyQualified(instanceDirectory)
			? Path.GetFullPath(instanceDirectory)
			: throw new ArgumentException("The instance directory must be an absolute path.",
				nameof(instanceDirectory));
		CallTimeout = callTimeout;
	}

	/// <summary>The absolute directory holding the plugins' discovery records.</summary>
	public string InstanceDirectory
	{
		get;
	}

	/// <summary>
	///     How long the gateway waits for one routed tool call. When it expires the call is reported as a timeout with an
	///     unknown host effect and is never sent again.
	/// </summary>
	public TimeSpan CallTimeout
	{
		get;
	}

	/// <summary>
	///     Resolves each setting from its argument, then its environment variable, then its default:
	///     <c>--instance-directory</c> or <c>MCP_INSTANCE_DIRECTORY</c>, and <c>--call-timeout-seconds</c> or
	///     <c>MCP_GATEWAY_CALL_TIMEOUT_SECONDS</c> (whole seconds from 5 to 3600, 45 by default).
	/// </summary>
	/// <param name="arguments">The gateway's command-line arguments; any other argument is rejected.</param>
	/// <param name="environment">Reads an environment variable.</param>
	/// <returns>The resolved options.</returns>
	/// <exception cref="ArgumentException">An argument is unknown, incomplete or out of range.</exception>
	public static GatewayOptions Resolve(IReadOnlyList<string> arguments, Func<string, string?> environment)
	{
		ArgumentNullException.ThrowIfNull(arguments);
		ArgumentNullException.ThrowIfNull(environment);
		string? directory = environment(InstanceDirectoryVariable);
		string? timeout = environment(CallTimeoutVariable);
		timeout = string.IsNullOrWhiteSpace(timeout) ? null : timeout;
		string timeoutSource = CallTimeoutVariable;
		for (int index = 0; index < arguments.Count; index++)
		{
			if (string.Equals(arguments[index], InstanceDirectoryArgument, StringComparison.Ordinal))
			{
				directory = ReadValue(arguments, ref index, "an absolute directory path");
			}
			else if (string.Equals(arguments[index], CallTimeoutArgument, StringComparison.Ordinal))
			{
				timeout = ReadValue(arguments, ref index, "a whole number of seconds");
				timeoutSource = CallTimeoutArgument;
			}
			else
			{
				throw new ArgumentException($"Unknown gateway argument '{arguments[index]}'.", nameof(arguments));
			}
		}

		if (!string.IsNullOrWhiteSpace(directory) && !Path.IsPathFullyQualified(directory))
		{
			throw new ArgumentException("The instance directory must be an absolute path.", nameof(arguments));
		}

		return new GatewayOptions(
			string.IsNullOrWhiteSpace(directory) ? InstanceRegistry.DefaultDirectory : directory,
			ParseCallTimeout(timeout, timeoutSource));
	}

	private static string ReadValue(IReadOnlyList<string> arguments, ref int index, string expected)
	{
		string name = arguments[index];
		if (++index >= arguments.Count)
		{
			throw new ArgumentException($"{name} requires {expected}.", nameof(arguments));
		}

		return arguments[index];
	}

	private static TimeSpan ParseCallTimeout(string? value, string source)
	{
		if (value is null)
		{
			return DefaultCallTimeout;
		}

		return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int seconds)
			   && seconds is >= MinimumCallTimeoutSeconds and <= MaximumCallTimeoutSeconds
			? TimeSpan.FromSeconds(seconds)
			: throw new ArgumentException(
				$"{source} must be a whole number of seconds from {MinimumCallTimeoutSeconds} to " +
				$"{MaximumCallTimeoutSeconds}.", nameof(value));
	}
}
