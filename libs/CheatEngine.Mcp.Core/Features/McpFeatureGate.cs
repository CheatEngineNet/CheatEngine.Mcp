using CheatEngine.Mcp.Core.Contract;

using Microsoft.Extensions.Options;

namespace CheatEngine.Mcp.Core.Features;

/// <summary>
///     The activation's exposure switches, read once from the options instance that also drove the Client opt-ins. It is
///     an activation-scoped service: the call-tool filter reaches it through the activation's primitive targets, and tools
///     and <see cref="Execution.ToolDispatch" /> use it for requirements that depend on an argument or a fixed script.
/// </summary>
public sealed class McpFeatureGate
{
	/// <summary>The tool <c>_meta</c> key that lists a tool's declared requirements as snake_case switch names.</summary>
	public const string RequiresMetaKey = "cheatengine/requires";

	private readonly McpFeatureSummary _state;

	/// <summary>Creates the gate over the activation's switches.</summary>
	/// <param name="options">The switches; their value is read once.</param>
	public McpFeatureGate(IOptions<McpFeatureOptions> options)
	{
		ArgumentNullException.ThrowIfNull(options);
		McpFeatureOptions value = options.Value;
		_state = new McpFeatureSummary(value.EnableUnsafeLua, value.EnableAutoAssembler,
			value.EnableTargetCodeExecution, value.EnableKernelAccess);
	}

	/// <summary>Whether a switch is on.</summary>
	/// <param name="feature">The switch.</param>
	/// <returns><see langword="true" /> when the switch allows its tools.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="feature" /> is not a defined switch.</exception>
	public bool IsEnabled(McpFeature feature)
	{
		return feature switch
		{
			McpFeature.UnsafeLua => _state.UnsafeLua,
			McpFeature.AutoAssembler => _state.AutoAssembler,
			McpFeature.TargetCodeExecution => _state.TargetCodeExecution,
			McpFeature.KernelAccess => _state.KernelAccess,
			_ => throw new ArgumentOutOfRangeException(nameof(feature), feature, "Unknown MCP feature.")
		};
	}

	/// <summary>
	///     Refuses the call when a switch is off. Call it before the first host effect, for requirements that depend on an
	///     argument value; a fixed requirement belongs in <see cref="RequiresFeatureAttribute" />.
	/// </summary>
	/// <param name="feature">The switch the call needs.</param>
	/// <param name="toolName">The refused tool or operation, named in the error.</param>
	/// <exception cref="CheatEngineToolException">
	///     <c>capability_disabled</c> with <c>not_started</c>; its hint names the <c>Mcp:Enable…</c> setting.
	/// </exception>
	public void Require(McpFeature feature, string toolName)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
		if (!IsEnabled(feature))
		{
			throw CheatEngineToolException.CapabilityDisabled(SettingName(feature), toolName);
		}
	}

	/// <summary>Reports every switch, for status tools.</summary>
	/// <returns>The switch states.</returns>
	public McpFeatureSummary Snapshot()
	{
		return _state;
	}

	/// <summary>The <c>Mcp</c> setting of a switch, such as <c>EnableKernelAccess</c>.</summary>
	/// <param name="feature">The switch.</param>
	/// <returns>The flat key under the <c>Mcp</c> section.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="feature" /> is not a defined switch.</exception>
	public static string SettingName(McpFeature feature)
	{
		return feature switch
		{
			McpFeature.UnsafeLua => nameof(McpFeatureOptions.EnableUnsafeLua),
			McpFeature.AutoAssembler => nameof(McpFeatureOptions.EnableAutoAssembler),
			McpFeature.TargetCodeExecution => nameof(McpFeatureOptions.EnableTargetCodeExecution),
			McpFeature.KernelAccess => nameof(McpFeatureOptions.EnableKernelAccess),
			_ => throw new ArgumentOutOfRangeException(nameof(feature), feature, "Unknown MCP feature.")
		};
	}

	/// <summary>The snake_case contract name of a switch, as listed in <see cref="RequiresMetaKey" />.</summary>
	/// <param name="feature">The switch.</param>
	/// <returns>The name, such as <c>kernel_access</c>.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="feature" /> is not a defined switch.</exception>
	public static string ContractName(McpFeature feature)
	{
		return feature switch
		{
			McpFeature.UnsafeLua => "unsafe_lua",
			McpFeature.AutoAssembler => "auto_assembler",
			McpFeature.TargetCodeExecution => "target_code_execution",
			McpFeature.KernelAccess => "kernel_access",
			_ => throw new ArgumentOutOfRangeException(nameof(feature), feature, "Unknown MCP feature.")
		};
	}
}
