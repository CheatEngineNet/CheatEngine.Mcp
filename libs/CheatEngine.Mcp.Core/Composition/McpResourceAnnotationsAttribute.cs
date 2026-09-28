using System.Globalization;

using ModelContextProtocol.Protocol;

namespace CheatEngine.Mcp.Core.Composition;

/// <summary>
///     Declares the MCP annotations of a resource or resource template: its audience, its priority from 0 (optional) to 1
///     (effectively required) and, optionally, when its content last changed. They are hints a client may use to filter,
///     order or skip resources.
/// </summary>
/// <remarks>
///     <para>
///         <c>WithCheatEnginePrimitives</c> and <c>McpLocalPrimitives</c> copy them into the listing entry when the
///         resource is created, as they publish <c>size</c>, so a backend, the schema-only catalog and the gateway list the
///         same values. The startup validator refuses a priority outside 0 to 1, a <see cref="LastModified" /> that is not
///         ISO 8601 and an attribute that sets nothing.
///     </para>
///     <para>
///         Usage: <c>[McpResourceAnnotations(Role.Assistant, Role.User, Priority = 1.0)]</c> on a document, or
///         <c>[McpResourceAnnotations(Role.Assistant, Priority = 0.3)]</c> on a live resource. Set
///         <see cref="LastModified" /> only from a build-time constant, never from the process start time.
///     </para>
/// </remarks>
/// <param name="audience">Who the resource is for, in publication order; empty publishes no audience.</param>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class McpResourceAnnotationsAttribute(params Role[] audience) : Attribute
{
	private static readonly string[] LastModifiedFormats =
	[
		"yyyy-MM-dd'T'HH:mm:ssK", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK"
	];

	/// <summary>Who the resource is for, in publication order; empty publishes no audience.</summary>
	public IReadOnlyList<Role> Audience
	{
		get;
	} = [.. audience];

	/// <summary>How important the resource is, from 0 to 1; <see cref="double.NaN" /> (the default) publishes none.</summary>
	public double Priority
	{
		get;
		set;
	} = double.NaN;

	/// <summary>
	///     When the content last changed, as an ISO 8601 date and time with an offset, such as
	///     <c>2026-09-27T00:00:00Z</c>; <see langword="null" /> (the default) publishes none.
	/// </summary>
	public string? LastModified
	{
		get;
		set;
	}

	/// <summary>Checks the declared values.</summary>
	/// <returns>One reason per invalid value; empty when the annotations can be published.</returns>
	internal IEnumerable<string> Validate()
	{
		if (Audience.Count == 0 && double.IsNaN(Priority) && LastModified is null)
		{
			yield return "declares annotations without an audience, a priority or a lastModified";
		}

		if (Audience.Any(static role => !Enum.IsDefined(role)))
		{
			yield return "declares an audience that is neither user nor assistant";
		}

		if (!double.IsNaN(Priority) && Priority is not (>= 0 and <= 1))
		{
			yield return "needs an annotation priority from 0 to 1";
		}

		if (LastModified is not null && !TryParseLastModified(LastModified, out _))
		{
			yield return "needs an ISO 8601 lastModified with an offset, such as 2026-09-27T00:00:00Z";
		}
	}

	/// <summary>Creates the protocol annotations.</summary>
	/// <returns>
	///     A new instance, or <see langword="null" /> when a value is invalid (the validator reports it) or nothing is
	///     set.
	/// </returns>
	internal Annotations? Create()
	{
		if (Validate().Any())
		{
			return null;
		}

		return new Annotations
		{
			Audience = Audience.Count == 0 ? null : [.. Audience.Distinct()],
			Priority = double.IsNaN(Priority) ? null : (float) Priority,
			LastModified = LastModified is not null && TryParseLastModified(LastModified, out DateTimeOffset parsed)
				? parsed
				: null
		};
	}

	private static bool TryParseLastModified(string value, out DateTimeOffset parsed)
	{
		// K also accepts no offset at all, which would publish the build machine's local time zone.
		bool hasOffset = value.EndsWith('Z') || (value.Length > 6 && value[^6] is '+' or '-' && value[^3] == ':');
		return DateTimeOffset.TryParseExact(value, LastModifiedFormats, CultureInfo.InvariantCulture,
			DateTimeStyles.None, out parsed) && hasOffset;
	}
}
