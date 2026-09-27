using CheatEngine.Mcp.Hosting.Discovery;

namespace CheatEngine.Mcp.Hosting.Gateway;

/// <summary>The gateway's command-line contract; it must agree with the plugins on the instance directory.</summary>
public sealed class GatewayOptions
{
	private GatewayOptions(string instanceDirectory)
	{
		InstanceDirectory = instanceDirectory;
	}

	/// <summary>The absolute directory holding the plugins' discovery records.</summary>
	public string InstanceDirectory
	{
		get;
	}

	/// <summary>Resolves <c>--instance-directory</c>, then <c>MCP_INSTANCE_DIRECTORY</c>, then the default directory.</summary>
	/// <param name="arguments">The gateway's command-line arguments; any other argument is rejected.</param>
	/// <param name="environment">Reads an environment variable.</param>
	/// <returns>The resolved options.</returns>
	public static GatewayOptions Resolve(IReadOnlyList<string> arguments, Func<string, string?> environment)
	{
		ArgumentNullException.ThrowIfNull(arguments);
		ArgumentNullException.ThrowIfNull(environment);
		string? directory = environment("MCP_INSTANCE_DIRECTORY");
		for (int index = 0; index < arguments.Count; index++)
		{
			if (string.Equals(arguments[index], "--instance-directory", StringComparison.Ordinal))
			{
				if (++index >= arguments.Count)
				{
					throw new ArgumentException("--instance-directory requires an absolute directory path.");
				}

				directory = arguments[index];
			}
			else
			{
				throw new ArgumentException($"Unknown gateway argument '{arguments[index]}'.");
			}
		}

		if (string.IsNullOrWhiteSpace(directory))
		{
			return new GatewayOptions(InstanceRegistry.DefaultDirectory);
		}

		return Path.IsPathFullyQualified(directory)
			? new GatewayOptions(Path.GetFullPath(directory))
			: throw new ArgumentException("The instance directory must be an absolute path.", nameof(arguments));
	}
}
