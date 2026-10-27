using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace CheatEngine.Mcp.Tests.LiveQualification.Infrastructure.NetworkIsolation;

/// <summary>
/// A reviewable, non-activating description of the two WFP filters required for a private CE source qualification.
/// This type deliberately contains no P/Invoke declarations and cannot change host policy.
/// </summary>
internal sealed record WfpNetworkIsolationPlan(
	string OwnedRunRoot, string ApplicationPath, string ApplicationSha256, Guid SessionKey,
	WfpSessionKind SessionKind, Guid SublayerKey, IReadOnlyList<WfpIsolationFilter> Filters)
{
	internal const uint DynamicSessionFlag = 0x00000001;
	internal const uint IsLoopbackFlag = 0x00000001;
	internal const uint FilterFlags = 0x00000000;
	internal const string RequiredExecutableName = "cheatengine-x86_64.exe";

	internal static readonly Guid AleAuthConnectV4Layer = new("c38d57d1-05a7-4c33-904f-7fbceee60e82");
	internal static readonly Guid AleAuthConnectV6Layer = new("4a72393b-319f-44bc-84c3-ba54dcb3b6b4");
	internal static readonly Guid AleOriginalApplicationIdCondition = new("0e6cd086-e1fb-4212-842f-8a9f993fb3f6");
	internal static readonly Guid FlagsCondition = new("632ce23b-5167-435c-86d7-e903684aa80c");

	internal static WfpNetworkIsolationPlan Create(string ownedRunRoot, string applicationPath,
		string expectedApplicationSha256)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(ownedRunRoot);
		ArgumentException.ThrowIfNullOrWhiteSpace(applicationPath);
		ArgumentException.ThrowIfNullOrWhiteSpace(expectedApplicationSha256);
		if (!Path.IsPathFullyQualified(ownedRunRoot) || !Path.IsPathFullyQualified(applicationPath))
		{
			throw new ArgumentException("The owned run root and private Cheat Engine executable path must be absolute.");
		}

		(string root, string application, string actualSha256) = ValidateCandidate(ownedRunRoot, applicationPath, expectedApplicationSha256);

		Guid sessionKey = Guid.NewGuid();
		Guid sublayerKey = Guid.NewGuid();
		return new WfpNetworkIsolationPlan(root, application, actualSha256, sessionKey, WfpSessionKind.Dynamic, sublayerKey,
		[
			CreateBlockNonLoopbackFilter(sublayerKey, AleAuthConnectV4Layer, WfpNetworkAddressFamily.Ipv4, application),
			CreateBlockNonLoopbackFilter(sublayerKey, AleAuthConnectV6Layer, WfpNetworkAddressFamily.Ipv6, application)
		]);
	}

	internal void RevalidateCandidate()
	{
		ValidateCandidate(OwnedRunRoot, ApplicationPath, ApplicationSha256);
	}

	private static (string Root, string Application, string ActualSha256) ValidateCandidate(string ownedRunRoot,
		string applicationPath, string expectedApplicationSha256)
	{
		string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(ownedRunRoot));
		string application = Path.GetFullPath(applicationPath);
		if (!Directory.Exists(root))
		{
			throw new DirectoryNotFoundException("The exact owned run root must already exist.");
		}
		if (!IsStrictChild(application, root))
		{
			throw new ArgumentException("The private Cheat Engine executable must be strictly below the exact owned run root.");
		}
		if (!string.Equals(Path.GetFileName(application), RequiredExecutableName, StringComparison.OrdinalIgnoreCase))
		{
			throw new ArgumentException($"The private executable must be named {RequiredExecutableName}.");
		}
		if (!Regex.IsMatch(expectedApplicationSha256, "^[0-9A-Fa-f]{64}$", RegexOptions.CultureInvariant))
		{
			throw new ArgumentException("The expected private executable SHA-256 must be exactly 64 hexadecimal characters.");
		}
		if (!File.Exists(application))
		{
			throw new FileNotFoundException("The private Cheat Engine executable is missing.", application);
		}
		RejectReparsePointComponents(root, application);

		string actualSha256;
		using (FileStream stream = File.OpenRead(application))
		{
			actualSha256 = Convert.ToHexString(SHA256.HashData(stream));
		}
		if (!string.Equals(actualSha256, expectedApplicationSha256, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("The private Cheat Engine executable hash does not match the frozen expected hash.");
		}

		return (root, application, actualSha256);
	}

	private static bool IsStrictChild(string path, string directory)
	{
		string normalizedDirectory = Path.TrimEndingDirectorySeparator(directory);
		return path.StartsWith(normalizedDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
	}

	private static void RejectReparsePointComponents(string root, string application)
	{
		string volumeRoot = Path.GetPathRoot(application)!;
		string current = volumeRoot;
		string relative = application[volumeRoot.Length..];
		foreach (string segment in relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
		{
			current = Path.Combine(current, segment);
			if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
			{
				throw new InvalidOperationException("The owned run root and private executable path must not traverse reparse points.");
			}
		}
	}

	private static WfpIsolationFilter CreateBlockNonLoopbackFilter(Guid sublayerKey, Guid layer, WfpNetworkAddressFamily addressFamily,
		string applicationPath)
	{
		return new WfpIsolationFilter(Guid.NewGuid(), sublayerKey, layer, addressFamily, WfpFilterAction.Block, FilterFlags,
			WfpFilterWeight.Auto,
		[
			new WfpFilterCondition(AleOriginalApplicationIdCondition, WfpMatchType.Equal, WfpConditionValue.ApplicationId,
				applicationPath, null),
			new WfpFilterCondition(FlagsCondition, WfpMatchType.FlagsNoneSet, WfpConditionValue.UInt32,
				null, IsLoopbackFlag)
		]);
	}
}

internal enum WfpSessionKind
{
	Dynamic
}
internal enum WfpNetworkAddressFamily
{
	Ipv4, Ipv6
}
internal enum WfpFilterAction
{
	Block
}
internal enum WfpMatchType
{
	Equal, FlagsNoneSet
}
internal enum WfpConditionValue
{
	ApplicationId, UInt32
}
internal enum WfpFilterWeight
{
	Auto
}

internal sealed record WfpIsolationFilter(Guid FilterKey, Guid SublayerKey, Guid LayerKey, WfpNetworkAddressFamily AddressFamily,
	WfpFilterAction Action, uint Flags, WfpFilterWeight Weight, IReadOnlyList<WfpFilterCondition> Conditions);

internal sealed record WfpFilterCondition(Guid FieldKey, WfpMatchType MatchType, WfpConditionValue ValueType,
	string? ApplicationPath, uint? UInt32Value);
