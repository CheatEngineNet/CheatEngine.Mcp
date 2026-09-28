using System.ComponentModel;
using System.Text.Json.Serialization;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Core.Targets;

/// <summary>What <see cref="TargetResources.ReleaseAll" /> did, newest resource first.</summary>
/// <param name="Released">The resources released completely, in release order.</param>
/// <param name="Failed">The first release that did not complete; the release stopped there.</param>
/// <param name="Remaining">The resources that still hold host state, including those awaiting an acknowledgement.</param>
public sealed record ReleaseAllResult(
	[property: Description("The resources released completely, newest first.")]
	IReadOnlyList<ReleasedResource> Released,
	[property: Description("The first release that did not complete; the release stopped there.")]
	ReleasedResource? Failed,
	[property: Description("The resources that still hold host state, including those awaiting an acknowledgement.")]
	IReadOnlyList<TargetResourceDescriptor> Remaining)
{
	/// <summary>Whether every resource was released and nothing holds host state any more.</summary>
	[JsonIgnore]
	public bool IsComplete => Failed is null && Remaining.Count == 0;

	/// <summary>
	///     Reports an incomplete release as the <c>partial_effect</c> error whose details are this result; a complete one
	///     returns unchanged. An incomplete release is never a success.
	/// </summary>
	/// <returns>This result, when it is complete.</returns>
	/// <exception cref="CheatEngineToolException">The release is incomplete.</exception>
	public ReleaseAllResult ThrowIfIncomplete()
	{
		if (IsComplete)
		{
			return this;
		}

		if (Failed is { } failed)
		{
			string hint = failed.Release.IsRetryable
				? "Repeat runtime_release_resources; nothing was done to that resource yet."
				: "Recover that resource manually, then acknowledge it with runtime_release_resources.";
			throw CheatEngineToolException.PartialEffect(
				$"{Released.Count} resource(s) were released; releasing {failed.Resource.Id} did not complete ({failed.Release.Kind}), so older resources were kept.",
				failed.Release.HostEffect, this, StateJsonContext.Default.ReleaseAllResult, failed.Release.IsRetryable,
				hint);
		}

		throw CheatEngineToolException.PartialEffect(
			$"{Released.Count} resource(s) were released; {Remaining.Count} still hold host state and need manual recovery or an acknowledgement.",
			Released.Count == 0 ? ToolHostEffect.NotStarted : ToolHostEffect.Completed, this,
			StateJsonContext.Default.ReleaseAllResult, false,
			"Recover the remaining resources manually, then acknowledge them with runtime_release_resources.");
	}
}
