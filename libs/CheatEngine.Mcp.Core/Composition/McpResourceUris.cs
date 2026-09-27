using System.Diagnostics.CodeAnalysis;

namespace CheatEngine.Mcp.Core.Composition;

/// <summary>
///     The one URI grammar of CheatEngine MCP resources, shared by the primitive projects, the validator and the gateway.
/// </summary>
/// <remarks>
///     <list type="bullet">
///         <item>
///             <c>cheatengine://docs/{slug}</c> and <c>cheatengine://docs/workflows/{workflow}</c>: static knowledge,
///             served identically by every backend and by the gateway (Local).
///         </item>
///         <item><c>cheatengine://instance/…</c>: live data of the one instance a backend serves (Instance).</item>
///         <item>
///             <c>cheatengine://instances/{instanceId}/…</c>: the gateway form of a live URI, a pure prefix swap of the
///             backend form; <c>cheatengine://instances</c> alone is the gateway's own instance list.
///         </item>
///     </list>
/// </remarks>
public static class McpResourceUris
{
	/// <summary>The URI scheme of every CheatEngine MCP resource.</summary>
	public const string Scheme = "cheatengine";

	/// <summary>The prefix of the static knowledge documents.</summary>
	public const string DocsPrefix = "cheatengine://docs/";

	/// <summary>The prefix of the workflow bodies, one per prompt.</summary>
	public const string WorkflowsPrefix = DocsPrefix + "workflows/";

	/// <summary>The prefix of a backend's live resources.</summary>
	public const string InstancePrefix = "cheatengine://instance/";

	/// <summary>The gateway's instance list, which never carries tokens or endpoints.</summary>
	public const string GatewayInstances = "cheatengine://instances";

	/// <summary>The prefix of a routed live resource at the gateway, followed by the instance id.</summary>
	public const string GatewayInstancesPrefix = GatewayInstances + "/";

	/// <summary>The template variable that selects an instance in a routed gateway URI.</summary>
	public const string InstanceIdVariable = "instanceId";

	/// <summary>The MIME type of every knowledge document.</summary>
	public const string MarkdownMimeType = "text/markdown";

	/// <summary>The MIME type of every live resource.</summary>
	public const string JsonMimeType = "application/json";

	/// <summary>The URI of a knowledge document.</summary>
	/// <param name="slug">The document slug, such as <c>value-scans</c>.</param>
	/// <returns><c>cheatengine://docs/{slug}</c>.</returns>
	public static string Doc(string slug)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(slug);
		return DocsPrefix + slug;
	}

	/// <summary>The URI of a workflow body.</summary>
	/// <param name="workflow">The kebab-case workflow name, such as <c>find-writer</c>.</param>
	/// <returns><c>cheatengine://docs/workflows/{workflow}</c>.</returns>
	public static string Workflow(string workflow)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(workflow);
		return WorkflowsPrefix + workflow;
	}

	/// <summary>Whether a URI or template is in the static knowledge space.</summary>
	/// <param name="uri">The URI or URI template.</param>
	/// <returns><see langword="true" /> for <c>cheatengine://docs/…</c>.</returns>
	public static bool IsDocs(string uri)
	{
		ArgumentNullException.ThrowIfNull(uri);
		return uri.StartsWith(DocsPrefix, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>Whether a URI or template is in a backend's live space.</summary>
	/// <param name="uri">The URI or URI template.</param>
	/// <returns><see langword="true" /> for <c>cheatengine://instance/…</c>.</returns>
	public static bool IsInstance(string uri)
	{
		ArgumentNullException.ThrowIfNull(uri);
		return uri.StartsWith(InstancePrefix, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>Whether a URI is the gateway's instance list, <c>cheatengine://instances</c>.</summary>
	/// <param name="uri">The requested URI.</param>
	/// <returns><see langword="true" /> for the instance list.</returns>
	public static bool IsGatewayInstances(string uri)
	{
		ArgumentNullException.ThrowIfNull(uri);
		return string.Equals(uri, GatewayInstances, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>Whether a URI or template is reserved for the gateway's own routed space.</summary>
	/// <param name="uri">The URI or URI template.</param>
	/// <returns><see langword="true" /> for <c>cheatengine://instances</c> and everything under it.</returns>
	public static bool IsGatewayReserved(string uri)
	{
		ArgumentNullException.ThrowIfNull(uri);
		return IsGatewayInstances(uri) ||
			   uri.StartsWith(GatewayInstancesPrefix, StringComparison.OrdinalIgnoreCase) ||
			   uri.StartsWith(GatewayInstances + "{", StringComparison.OrdinalIgnoreCase) ||
			   uri.StartsWith(GatewayInstances + "?", StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	///     Converts a backend live URI or URI template to its gateway form:
	///     <c>cheatengine://instance/{path}</c> becomes <c>cheatengine://instances/{instance}/{path}</c>.
	/// </summary>
	/// <param name="backend">A <c>cheatengine://instance/…</c> URI or template.</param>
	/// <param name="instance">The instance id, or <c>{instanceId}</c> for a template.</param>
	/// <returns>The gateway URI or template.</returns>
	/// <exception cref="ArgumentException"><paramref name="backend" /> is not a backend live URI.</exception>
	public static string ToGateway(string backend, string instance)
	{
		ArgumentNullException.ThrowIfNull(backend);
		ArgumentException.ThrowIfNullOrWhiteSpace(instance);
		if (!IsInstance(backend))
		{
			throw new ArgumentException($"'{backend}' is not a {InstancePrefix} URI.", nameof(backend));
		}

		return GatewayInstancesPrefix + instance + "/" + backend[InstancePrefix.Length..];
	}

	/// <summary>
	///     Splits a gateway live URI into its instance id and the backend URI it routes to. The scheme and authority match
	///     case-insensitively; the instance id must have the published <c>ce-{pid}-{activation}</c> form exactly.
	/// </summary>
	/// <param name="uri">The requested gateway URI.</param>
	/// <param name="instanceId">The instance id, when parsed.</param>
	/// <param name="backendUri">The backend <c>cheatengine://instance/…</c> URI, when parsed.</param>
	/// <returns><see langword="true" /> when the URI has the routed gateway form.</returns>
	public static bool TryParseGateway(string uri, [NotNullWhen(true)] out string? instanceId,
		[NotNullWhen(true)] out string? backendUri)
	{
		ArgumentNullException.ThrowIfNull(uri);
		instanceId = null;
		backendUri = null;
		if (!uri.StartsWith(GatewayInstancesPrefix, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		string rest = uri[GatewayInstancesPrefix.Length..];
		int slash = rest.IndexOf('/', StringComparison.Ordinal);
		if (slash <= 0 || slash == rest.Length - 1 || !IsInstanceId(rest[..slash]))
		{
			return false;
		}

		instanceId = rest[..slash];
		backendUri = InstancePrefix + rest[(slash + 1)..];
		return true;
	}

	/// <summary>
	///     Rewrites a URI a backend returned so the gateway's client can read it again: a backend live URI gains the
	///     instance prefix; any other URI, such as a knowledge document, is returned unchanged.
	/// </summary>
	/// <param name="uri">The URI in a backend result.</param>
	/// <param name="instanceId">The instance that produced it.</param>
	/// <returns>The URI to show upstream.</returns>
	public static string RewriteContentUri(string uri, string instanceId)
	{
		ArgumentNullException.ThrowIfNull(uri);
		return IsInstance(uri) ? ToGateway(uri, instanceId) : uri;
	}

	/// <summary>
	///     Whether a string has the published instance id form <c>ce-{pid}-{32 lowercase hex digits}</c>, which uses only
	///     unreserved URI characters.
	/// </summary>
	/// <param name="value">The candidate.</param>
	/// <returns><see langword="true" /> for a well-formed id.</returns>
	public static bool IsInstanceId(string value)
	{
		ArgumentNullException.ThrowIfNull(value);
		if (!value.StartsWith("ce-", StringComparison.Ordinal))
		{
			return false;
		}

		string rest = value[3..];
		int dash = rest.IndexOf('-', StringComparison.Ordinal);
		if (dash is < 1 or > 10 || rest.Length - dash - 1 != 32)
		{
			return false;
		}

		for (int index = 0; index < rest.Length; index++)
		{
			char character = rest[index];
			bool valid = index < dash
				? char.IsAsciiDigit(character)
				: index == dash || char.IsAsciiDigit(character) || character is >= 'a' and <= 'f';
			if (!valid)
			{
				return false;
			}
		}

		return true;
	}
}
