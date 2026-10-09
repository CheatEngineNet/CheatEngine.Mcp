using System.Diagnostics.CodeAnalysis;

namespace CheatEngine.Mcp.Core.Composition;

/// <summary>
///     Declares the read-only tool a live <c>cheatengine://instance/…</c> resource projects: the resource returns that
///     tool's structured result. Every Instance resource declares exactly one.
/// </summary>
/// <remarks>
///     <para>
///         <c>WithCheatEnginePrimitives</c> publishes the tool name in the resource's <c>_meta</c> under
///         <see cref="MetaKey" />. The startup validator requires the tool to exist on <see cref="ToolType" /> and to be
///         read-only, closed-world and ungated. Its dispatch class must be <c>short</c>, unless
///         <see cref="PreparedProjection" /> names a separate short, ungated prepared read with the same result type.
///         Every read also re-checks the source tool's feature gates.
///     </para>
///     <para>
///         The reference is typed, so the rule holds in any composition, including one that serves resources without
///         tools; when the server also serves tools, it must serve this one.
///     </para>
/// </remarks>
/// <param name="toolType">The tool container type that declares the tool method.</param>
/// <param name="toolName">The tool's v2 name, such as <c>module_list</c>.</param>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class McpSourceToolAttribute(
	[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)]
	Type toolType,
	string toolName) : Attribute
{
	/// <summary>The resource <c>_meta</c> key that names the projected tool.</summary>
	public const string MetaKey = "cheatengine/sourceTool";

	/// <summary>The resource <c>_meta</c> key that names its bounded prepared projection.</summary>
	public const string PreparedProjectionMetaKey = "cheatengine/preparedProjection";

	/// <summary>The tool container type that declares the tool method.</summary>
	[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)]
	public Type ToolType
	{
		get;
	} = toolType;

	/// <summary>The tool's v2 name, such as <c>module_list</c>.</summary>
	public string ToolName
	{
		get;
	} = toolName;

	/// <summary>
	///     The optional public instance method on <see cref="ToolType" /> that returns the latest prepared result without
	///     repeating the source tool's host enumeration.
	/// </summary>
	public string? PreparedProjection
	{
		get;
		set;
	}
}
