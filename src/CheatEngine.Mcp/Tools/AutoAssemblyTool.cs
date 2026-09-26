using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;

using CheatEngine.Client;
using CheatEngine.Client.Assembly;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools;

/// <summary>Owns Auto Assembler patch leases for one activated MCP server.</summary>
[McpServerToolType]
public sealed class AutoAssemblyTool : IDisposable
{
	private const int MaximumPatchCount = 128;
	private const int MaximumPatchNameLength = 256;
	private const int MaximumScriptLength = 1024 * 1024;
	private readonly IAutoAssemblerClient? _autoAssembler;
	private readonly ICheatEngineClient _client;
	private readonly Dictionary<string, IAutoAssemblerPatchLease> _patches = new(StringComparer.Ordinal);
	private readonly object _patchesLock = new();
	private readonly TargetResources? _targetResources;

	public AutoAssemblyTool(ICheatEngineClient client, IAutoAssemblerClient? autoAssembler = null,
		TargetResources? targetResources = null)
	{
		_client = client;
		_autoAssembler = autoAssembler;
		_targetResources = targetResources;
	}

	[McpServerTool(Name = "assemble"), Description("Assemble one target instruction into bytes through the Client instruction capability.")]
	public object Assemble(
		[Description("Assembly source for exactly one instruction.")] string instruction,
		[Description("Target origin as hexadecimal; required for relative operands.")] string address,
		[Description("0=None, 1=Short, 2=Long, 3=Far.")] InstructionEncodingPreference preference = InstructionEncodingPreference.None,
		[Description("Skip relative-branch reachability checks.")] bool skipRangeCheck = false)
	{
		return ToolExecution.Run(_client, () =>
		{
			byte[] bytes = [.. _client.Assembly.Assemble(new AssemblyInstructionRequest(
				ToolExecution.Address(_client, address), instruction, preference, skipRangeCheck))];
			return new
			{
				success = true,
				bytes = bytes.Select(static value => value.ToString("X2", CultureInfo.InvariantCulture)).ToArray(),
				hex = Convert.ToHexString(bytes),
				size = bytes.Length
			};
		});
	}

	[McpServerTool(Name = "auto_assemble"), Description("Apply an Auto Assembler patch and retain its Client lease, or release a previously returned patch ID.")]
	public object AutoAssemble(
		[Description("Complete Auto Assembler source, required when applying a patch.")] string? script = null,
		[Description("Patch ID returned by a prior enable call. Supplying it releases that Client-owned patch.")] string? disableId = null,
		[Description("Optional diagnostic name for a new patch.")] string? name = null)
	{
		if (_autoAssembler is null)
		{
			return ToolExecution.Error("Auto Assembler patches are disabled by this server.");
		}

		return ToolExecution.Run(_client, () =>
		{
			if (!string.IsNullOrWhiteSpace(disableId))
			{
				IAutoAssemblerPatchLease? lease;
				lock (_patchesLock)
				{
					if (!_patches.TryGetValue(disableId, out lease))
					{
						return ToolExecution.Error("Patch ID was not found or is no longer owned by this server.");
					}
				}

				CheatEngine.Client.Results.LeaseReleaseOutcome release = lease.Release();
				if (!release.IsRetryable)
				{
					_targetResources?.Forget(lease);
					lock (_patchesLock)
					{
						_patches.Remove(disableId);
					}
				}
				return new
				{
					success = release.IsComplete,
					mode = "disabled",
					disableId,
					name = lease.Name,
					release = release.Kind.ToString(),
					hostEffect = release.HostEffect.ToString(),
					retryable = release.IsRetryable,
					requiresManualRecovery = release.RequiresManualRecovery
				};
			}

			if (string.IsNullOrWhiteSpace(script))
			{
				return ToolExecution.Error("script is required.");
			}
			if (script.Length > MaximumScriptLength)
			{
				return ToolExecution.Error($"script must not exceed {MaximumScriptLength} characters.");
			}
			if (name is { Length: > MaximumPatchNameLength })
			{
				return ToolExecution.Error($"name must not exceed {MaximumPatchNameLength} characters.");
			}
			lock (_patchesLock)
			{
				if (_patches.Count >= MaximumPatchCount)
				{
					return ToolExecution.Error($"At most {MaximumPatchCount} patches may be owned by this server.");
				}
			}

			ArgumentException.ThrowIfNullOrWhiteSpace(script);
			IAutoAssemblerPatchLease created = _autoAssembler.ApplyPatch(new AutoAssemblerScript(script, name));
			string patchId = Guid.NewGuid().ToString("N");
			if (created.AppliedAfterTargetChange || created.IsReleased)
			{
				CheatEngine.Client.Results.LeaseReleaseOutcome? release = created.LastReleaseOutcome;
				return new
				{
					success = false,
					error = "The patch was applied but its ownership cannot be retained safely.",
					mode = "enabled",
					attemptId = patchId,
					name = created.Name,
					appliedAfterTargetChange = created.AppliedAfterTargetChange,
					release = release?.Kind.ToString(),
					hostEffect = release?.HostEffect.ToString(),
					warnings = created.HostWarnings,
					warningsTruncated = created.HostWarningsTruncated,
					requiresManualRecovery = created.RequiresManualRecovery || release?.RequiresManualRecovery == true
				};
			}
			lock (_patchesLock)
			{
				_patches.Add(patchId, created);
			}
			_targetResources?.Track(created, () =>
			{
				lock (_patchesLock)
				{
					_patches.Remove(patchId);
				}
			}, new
			{
				kind = "patch",
				id = patchId
			});

			return new
			{
				success = true,
				mode = "enabled",
				disableId = patchId,
				appliedAfterTargetChange = created.AppliedAfterTargetChange,
				warnings = created.HostWarnings,
				warningsTruncated = created.HostWarningsTruncated
			};
		});
	}

	[McpServerTool(Name = "auto_assemble_check"), Description("Check an Auto Assembler [ENABLE] section without applying a patch.")]
	public object AutoAssembleCheck(
		[Description("Auto Assembler source to check.")] string script,
		[Description("Optional diagnostic name for the script.")] string? name = null)
	{
		if (_autoAssembler is null)
		{
			return ToolExecution.Error("Auto Assembler patches are disabled by this server.");
		}

		return ToolExecution.Run(_client, () =>
		{
			if (string.IsNullOrWhiteSpace(script))
			{
				return ToolExecution.Error("script is required.");
			}
			if (script.Length > MaximumScriptLength)
			{
				return ToolExecution.Error($"script must not exceed {MaximumScriptLength} characters.");
			}
			if (name is { Length: > MaximumPatchNameLength })
			{
				return ToolExecution.Error($"name must not exceed {MaximumPatchNameLength} characters.");
			}

			AutoAssemblerCheckResult result = _autoAssembler.Check(new AutoAssemblerScript(script, name));
			return new
			{
				success = true,
				syntaxValid = result.IsAccepted,
				error = result.HostMessages,
				hostMessagesTruncated = result.HostMessagesTruncated
			};
		});
	}

	public void Dispose()
	{
		KeyValuePair<string, IAutoAssemblerPatchLease>[] patches;
		lock (_patchesLock)
		{
			patches = [.. _patches];
		}

		foreach ((string patchId, IAutoAssemblerPatchLease patch) in patches)
		{
			CheatEngine.Client.Results.LeaseReleaseOutcome outcome = patch.Release();
			if (!outcome.IsRetryable)
			{
				_targetResources?.Forget(patch);
				lock (_patchesLock)
				{
					_patches.Remove(patchId);
				}
			}
		}
	}
}
