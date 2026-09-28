using System.Collections.Immutable;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Core.Features;

/// <summary>
///     The exposure switches that caller-supplied content needs, with the first reason for each, as found by
///     <see cref="AutoAssemblerScriptClassifier" /> or <see cref="Tables.CheatTableInspector" />. A tool enforces them
///     before the first host effect.
/// </summary>
public sealed class McpFeatureRequirements
{
	private readonly ImmutableArray<string?> _reasons;

	internal McpFeatureRequirements(ImmutableArray<string?> reasons)
	{
		_reasons = reasons;
		Features = [.. Enum.GetValues<McpFeature>().Where(feature => reasons[(int) feature] is not null)];
	}

	/// <summary>No requirement.</summary>
	public static McpFeatureRequirements None
	{
		get;
	} = new(ImmutableArray.Create(new string?[Enum.GetValues<McpFeature>().Length]));

	/// <summary>The required switches, each once, in <see cref="McpFeature" /> order.</summary>
	public ImmutableArray<McpFeature> Features
	{
		get;
	}

	/// <summary>The required switches as snake_case contract names, such as <c>unsafe_lua</c>, for tool results.</summary>
	public ImmutableArray<string> ContractNames => [.. Features.Select(McpFeatureGate.ContractName)];

	/// <summary>Whether a switch is required.</summary>
	/// <param name="feature">The switch.</param>
	/// <returns><see langword="true" /> when the content needs it.</returns>
	public bool Requires(McpFeature feature)
	{
		return ReasonFor(feature) is not null;
	}

	/// <summary>Why a switch is required, such as <c>the script has a {$lua} block</c>.</summary>
	/// <param name="feature">The switch.</param>
	/// <returns>The first reason found, or <see langword="null" /> when the switch is not required.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="feature" /> is not a defined switch.</exception>
	public string? ReasonFor(McpFeature feature)
	{
		return (int) feature >= 0 && (int) feature < _reasons.Length
			? _reasons[(int) feature]
			: throw new ArgumentOutOfRangeException(nameof(feature), feature, "Unknown MCP feature.");
	}

	/// <summary>
	///     Refuses the call when a required switch is off, naming the first such switch in <see cref="McpFeature" /> order
	///     and why the content needs it. Call it before the first host effect.
	/// </summary>
	/// <param name="gate">The activation's switches.</param>
	/// <param name="toolName">The refused tool, named in the error.</param>
	/// <exception cref="CheatEngineToolException"><c>capability_disabled</c> with <c>not_started</c>.</exception>
	public void Enforce(McpFeatureGate gate, string toolName)
	{
		ArgumentNullException.ThrowIfNull(gate);
		ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
		foreach (McpFeature feature in Features)
		{
			if (!gate.IsEnabled(feature))
			{
				string setting = McpFeatureGate.SettingName(feature);
				throw new CheatEngineToolException(new ToolError(ToolErrorKind.CapabilityDisabled,
					$"{toolName} is disabled by the Mcp:{setting} setting: {_reasons[(int) feature]}.", null,
					ToolHostEffect.NotStarted, false,
					$"Set Mcp:{setting} to true in appsettings.json, then disable and re-enable the plugin."));
			}
		}
	}
}

/// <summary>Collects requirements, keeping the first reason given for each switch.</summary>
internal sealed class McpFeatureRequirementsBuilder
{
	private readonly string?[] _reasons = new string?[Enum.GetValues<McpFeature>().Length];

	/// <summary>Requires a switch, unless a reason was already recorded for it.</summary>
	/// <param name="feature">The switch.</param>
	/// <param name="reason">A short clause such as <c>the script has a {$lua} block</c>.</param>
	internal void Add(McpFeature feature, string reason)
	{
		_reasons[(int) feature] ??= reason;
	}

	/// <summary>Adds every requirement of <paramref name="other" /> with its own reason.</summary>
	/// <param name="other">Requirements found in a part of the content.</param>
	internal void Add(McpFeatureRequirements other)
	{
		foreach (McpFeature feature in other.Features)
		{
			Add(feature, other.ReasonFor(feature)!);
		}
	}

	internal McpFeatureRequirements Build()
	{
		return new McpFeatureRequirements([.. _reasons]);
	}
}
