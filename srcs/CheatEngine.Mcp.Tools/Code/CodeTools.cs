using System.ComponentModel;
using System.Text;

using CheatEngine.Client;
using CheatEngine.Client.Assembly;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Jobs;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.Mcp.Tools.Memory;
using CheatEngine.SDK.Engine.Values;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools.Code;

/// <summary>Typed instruction operations and bounded code-dissector views for the <c>code_*</c> contract.</summary>
[McpServerToolType]
public sealed class CodeTools
{
	internal const int MaximumInstructions = 1024;
	internal const int MaximumByteTextLength = 131072;
	internal const int MaximumCodeBytes = 1024 * 1024;
	internal const int MaximumPage = 1000;
	internal const int MaximumScannedEntries = 100000;
	internal const int MaximumCommentAddresses = 256;
	internal const int MaximumCommentLength = 4096;
	internal const string JobKind = "code";

	private readonly ToolDispatch _dispatch;
	private readonly JobRegistry _jobs;
	private int _dissectState;

	/// <summary>Creates the code tools; no Client work occurs during construction.</summary>
	public CodeTools(ToolDispatch dispatch, JobRegistry jobs)
	{
		ArgumentNullException.ThrowIfNull(dispatch);
		ArgumentNullException.ThrowIfNull(jobs);
		_dispatch = dispatch;
		_jobs = jobs;
	}

	/// <summary>Disassembles a bounded forward sequence, optionally preceded by estimated instructions.</summary>
	[McpServerTool(Name = CheatEngineToolNames.CodeDisassemble, Title = "Disassemble code", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Disassemble 1 to 1024 target instructions with CheatEngine.Client's typed instruction API. before asks Cheat Engine for estimated predecessor boundaries, so it is useful for context but cannot prove a variable-length instruction boundary. The returned bytes are read from target memory in the same Client operation.")]
	public CodeDisassembly Disassemble(
		[Description("An instruction address or Cheat Engine expression, such as game.exe+1C0.")]
		string address,
		[Description("The number of forward instructions, 1 to 1024 after any predecessors.")]
		int count = 20,
		[Description(
			"The estimated predecessor instructions to include, 0 to 1024; count plus before is at most 1024.")]
		int before = 0,
		CancellationToken cancellationToken = default)
	{
		string expression = MemoryTargets.RequireExpression(address, "address");
		RequireRange(count, "count", 1, MaximumInstructions);
		RequireRange(before, "before", 0, MaximumInstructions);
		if (count + before > MaximumInstructions)
		{
			throw CheatEngineToolException.LimitExceeded("count",
				$"count plus before accepts at most {MaximumInstructions} instructions.");
		}

		return _dispatch.Run(CheatEngineToolNames.CodeDisassemble, token =>
		{
			ICheatEngineClient client = _dispatch.Client;
			Address requested = MemoryTargets.Resolve(client, expression, "address", token);
			Address first = requested;
			for (int index = 0; index < before; index++)
			{
				first = client.Assembly.GetPreviousInstructionAddress(first, token);
			}

			CodeInstruction[] instructions = new CodeInstruction[count + before];
			Address current = first;
			for (int index = 0; index < instructions.Length; index++)
			{
				AssemblyInstructionSnapshot decoded = CorrectColumns(_dispatch,
					client.Assembly.Disassemble(current, token), CheatEngineToolNames.CodeDisassemble, token);
				instructions[index] = Instruction(decoded, CheatEngineToolNames.CodeDisassemble);
				current = decoded.Address + decoded.Length;
			}

			return new CodeDisassembly(HexFormat.Address(requested), instructions);
		}, cancellationToken);
	}

	/// <summary>Decodes a single target instruction and exposes its exact length.</summary>
	[McpServerTool(Name = CheatEngineToolNames.CodeDecode, Title = "Decode an instruction", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Decode one target instruction through CheatEngine.Client and return Cheat Engine's columns, exact bytes and exact length. Use code_disassemble when surrounding instructions are needed.")]
	public CodeDecodeResult Decode(
		[Description("An instruction address or Cheat Engine expression.")]
		string address,
		CancellationToken cancellationToken = default)
	{
		string expression = MemoryTargets.RequireExpression(address, "address");
		return _dispatch.Run(CheatEngineToolNames.CodeDecode, token =>
		{
			Address target = MemoryTargets.Resolve(_dispatch.Client, expression, "address", token);
			AssemblyInstructionSnapshot decoded = CorrectColumns(_dispatch,
				_dispatch.Client.Assembly.Disassemble(target, token), CheatEngineToolNames.CodeDecode, token);
			CodeInstruction instruction = Instruction(decoded, CheatEngineToolNames.CodeDecode);
			return new CodeDecodeResult(instruction, instruction.Size);
		}, cancellationToken);
	}

	/// <summary>Asks Cheat Engine to decode supplied bytes without reading the target process.</summary>
	[McpServerTool(Name = CheatEngineToolNames.CodeDisassembleBytes, Title = "Disassemble bytes", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Decode a hexadecimal byte sequence through Cheat Engine's disassembleBytes function, without reading target memory. origin is optional and affects relative operands. The complete textual decode is bounded to 131072 input characters.")]
	public CodeByteDisassembly DisassembleBytes(
		[Description("Hexadecimal bytes, with optional spaces, up to 131072 characters.")]
		string hexadecimalBytes,
		[Description("The optional origin address or expression for relative operands.")]
		string? origin = null,
		CancellationToken cancellationToken = default)
	{
		string bytes = NormalizeHexadecimalBytes(hexadecimalBytes);

		if (origin is not null)
		{
			MemoryTargets.RequireExpression(origin, "origin");
		}

		CodeLuaByteDisassembly decoded = _dispatch.RunLua(CheatEngineToolNames.CodeDisassembleBytes,
			CodeScripts.DisassembleBytes, CodeLuaJsonContext.Default.CodeLuaByteDisassembly, cancellationToken,
			bytes, origin);
		return new CodeByteDisassembly(decoded.Origin, decoded.Text);
	}

	/// <summary>Gets function boundaries and jump-destination information from Cheat Engine's code view.</summary>
	[McpServerTool(Name = CheatEngineToolNames.CodeGetFunction, Title = "Get function estimate", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Get Cheat Engine's estimated function boundaries for an address and whether it is a jump destination in a bounded containing range. Function boundaries are heuristics, not symbols or a proof of control flow.")]
	public CodeFunction GetFunction(
		[Description("An address or expression inside the candidate function.")]
		string address,
		[Description("The containing range used for the jump-destination check, 1 to 1048576 bytes.")]
		int jumpRange = 4096,
		CancellationToken cancellationToken = default)
	{
		string expression = MemoryTargets.RequireExpression(address, "address");
		RequireRange(jumpRange, "jumpRange", 1, MaximumCodeBytes);
		CodeLuaFunction result = _dispatch.RunLua(CheatEngineToolNames.CodeGetFunction, CodeScripts.GetFunction,
			CodeLuaJsonContext.Default.CodeLuaFunction, cancellationToken, expression, jumpRange);
		return new CodeFunction(result.Address, result.Found, result.StartAddress, result.EndAddress, result.Size,
			result.AddressIsJumpDestination);
	}

	/// <summary>Starts a code-dissection pass outside the request's lifetime.</summary>
	[McpServerTool(Name = CheatEngineToolNames.CodeStartDissect, Title = "Start code dissect", ReadOnly = false,
		Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.BlockingNative)]
	[Description(
		"Start one Cheat Engine code-dissection pass over up to 1 MiB. The request returns a job immediately, while Cheat Engine performs the native dissector call in the background; it can still block Cheat Engine. Poll code_poll_job, then read references, strings and functions. Only one dissect pass runs at a time, and code_clear_dissect refuses while it runs.")]
	public CodeJobStart StartDissect(
		[Description("The first address or expression to dissect.")]
		string address,
		[Description("The bytes to dissect, 1 to 1048576.")]
		int size = 4096,
		[Description("The job TTL in seconds, 1 to 300; the configured default when omitted.")]
		int? lifetimeSeconds = null,
		CancellationToken cancellationToken = default)
	{
		string expression = MemoryTargets.RequireExpression(address, "address");
		RequireRange(size, "size", 1, MaximumCodeBytes);
		TimeSpan lifetime = _jobs.ResolveTimeToLive(lifetimeSeconds);
		if (Interlocked.CompareExchange(ref _dissectState, 1, 0) != 0)
		{
			throw CheatEngineToolException.Busy("A code dissect job is already running.",
				"Poll it with code_poll_job or stop it with runtime_stop_job.");
		}

		try
		{
			(Address target, long epoch) = _dispatch.Run(CheatEngineToolNames.CodeStartDissect, token =>
			{
				ICheatEngineClient client = _dispatch.Client;
				return (MemoryTargets.Resolve(client, expression, "address", token),
					MemoryTargets.SelectionEpoch(client, token));
			}, cancellationToken);
			ManagedJob<CodeJobEvent> job = _jobs.StartManaged<CodeJobEvent>(JobKind, lifetime, 1,
				(writer, token) => RunDissectAsync(writer, target, epoch, size, token));
			return new CodeJobStart(job.Id, job.GetStatus());
		}
		catch
		{
			Volatile.Write(ref _dissectState, 0);
			throw;
		}
	}

	/// <summary>Starts a bounded instruction-text search over a target range.</summary>
	[McpServerTool(Name = CheatEngineToolNames.CodeStartSearch, Title = "Start code search", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Start a background search for a case-insensitive substring in decoded instruction text. It disassembles at most 1 MiB in short Client dispatches, rechecks the selected target each time, and retains at most the configured job-buffer events. Poll code_poll_job or stop it with runtime_stop_job.")]
	public CodeJobStart StartSearch(
		[Description("The first target address or expression to search.")]
		string address,
		[Description("The bytes to search, 1 to 1048576.")]
		int size,
		[Description("The case-insensitive instruction-text substring, 1 to 256 characters.")]
		string textContains,
		[Description("The job TTL in seconds, 1 to 300; the configured default when omitted.")]
		int? lifetimeSeconds = null,
		[Description("The retained match events, 1 to the configured job limit; the configured limit when omitted.")]
		int? maximumResults = null,
		CancellationToken cancellationToken = default)
	{
		string expression = MemoryTargets.RequireExpression(address, "address");
		RequireRange(size, "size", 1, MaximumCodeBytes);
		if (string.IsNullOrWhiteSpace(textContains) || textContains.Length > 256)
		{
			throw CheatEngineToolException.InvalidArgument("textContains", "must contain 1 to 256 characters.");
		}

		TimeSpan lifetime = _jobs.ResolveTimeToLive(lifetimeSeconds);
		int buffer = _jobs.ResolveBufferLimit(maximumResults, "maximumResults");
		(Address target, long epoch) = _dispatch.Run(CheatEngineToolNames.CodeStartSearch, token =>
		{
			ICheatEngineClient client = _dispatch.Client;
			return (MemoryTargets.Resolve(client, expression, "address", token),
				MemoryTargets.SelectionEpoch(client, token));
		}, cancellationToken);
		ManagedJob<CodeJobEvent> job = _jobs.StartManaged<CodeJobEvent>(JobKind, lifetime, buffer,
			(writer, token) => RunSearchAsync(writer, target, epoch, size, textContains, token));
		return new CodeJobStart(job.Id, job.GetStatus());
	}

	/// <summary>Polls a job created by code_start_dissect or code_start_search.</summary>
	[McpServerTool(Name = CheatEngineToolNames.CodePollJob, Title = "Poll code job", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Read retained events from a code job without consuming them. Start with afterSequence 0 and pass nextAfterSequence to the next poll. Finished jobs remain pollable until their TTL; runtime_stop_job discards their events.")]
	public CodeJobPage PollJob(
		[Description("The code job id returned by code_start_dissect or code_start_search.")]
		string jobId,
		[Description("0 first, then nextAfterSequence from the preceding page.")]
		long afterSequence = 0,
		[Description("The maximum events to return, 1 to 1000.")]
		int limit = 100)
	{
		ManagedJob<CodeJobEvent> job = _jobs.Get<ManagedJob<CodeJobEvent>>(jobId, JobKind);
		JobPoll<CodeJobEvent> page = job.Poll(afterSequence, limit);
		return new CodeJobPage(page.Job, page.Items, page.FirstSequence, page.NextAfterSequence, page.More,
			page.Dropped);
	}

	/// <summary>Pages references to one target in Cheat Engine's current code dissector.</summary>
	[McpServerTool(Name = CheatEngineToolNames.CodeFindReferences, Title = "Find code references", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Page references to an address from Cheat Engine's current code dissector. The copy considers at most 100000 references; exact is false when that bound was reached. Run code_start_dissect first, and do not rely on a page after the code dissector changed.")]
	public CodeReferencePage FindReferences(
		[Description("The referenced address or expression.")]
		string address,
		[Description("The zero-based first result.")]
		int offset = 0,
		[Description("The most references returned, 1 to 1000.")]
		int limit = 100,
		CancellationToken cancellationToken = default)
	{
		string expression = MemoryTargets.RequireExpression(address, "address");
		Page(offset, limit);
		return _dispatch.Run(CheatEngineToolNames.CodeFindReferences, token =>
		{
			Address target = MemoryTargets.Resolve(_dispatch.Client, expression, "address", token);
			CodeLuaReferencePage page = _dispatch.ExecuteLua(CheatEngineToolNames.CodeFindReferences,
				CodeScripts.FindReferences, CodeLuaJsonContext.Default.CodeLuaReferencePage, token, target.ToUInt64(),
				offset, limit, MaximumScannedEntries);
			return new CodeReferencePage(page.Address, page.Total, page.Exact, offset,
			[
				.. page.References.Select(reference => new CodeReference(reference.FromAddress, reference.ToAddress,
					reference.Kind))
			], page.NextOffset);
		}, cancellationToken);
	}

	/// <summary>Pages strings that Cheat Engine's current code dissector recorded.</summary>
	[McpServerTool(Name = CheatEngineToolNames.CodeFindStrings, Title = "Find referenced strings", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Page strings referenced by Cheat Engine's current code dissector, optionally filtered by textContains. The enumeration considers at most 100000 dissector entries; exact is false when that bound was reached.")]
	public CodeStringPage FindStrings(
		[Description("Keep strings containing this case-insensitive text, up to 256 characters.")]
		string? textContains = null,
		[Description("The zero-based first result.")]
		int offset = 0,
		[Description("The most strings returned, 1 to 1000.")]
		int limit = 100,
		CancellationToken cancellationToken = default)
	{
		if (textContains is { Length: > 256 })
		{
			throw CheatEngineToolException.InvalidArgument("textContains", "must contain at most 256 characters.");
		}

		Page(offset, limit);
		CodeLuaStringPage page = _dispatch.RunLua(CheatEngineToolNames.CodeFindStrings, CodeScripts.FindStrings,
			CodeLuaJsonContext.Default.CodeLuaStringPage, cancellationToken, textContains, offset, limit,
			MaximumScannedEntries);
		return new CodeStringPage(page.Total, page.Exact, offset,
			[.. page.Strings.Select(value => new CodeString(value.Address, value.Text))], page.NextOffset);
	}

	/// <summary>Pages function addresses referenced by the current code dissector.</summary>
	[McpServerTool(Name = CheatEngineToolNames.CodeListFunctions, Title = "List referenced functions", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Page function addresses that Cheat Engine's current code dissector reports. Run code_start_dissect first; the result is code-dissector state, not a symbol list.")]
	public CodeFunctionPage ListFunctions(
		[Description("The zero-based first function.")]
		int offset = 0,
		[Description("The most function addresses returned, 1 to 1000.")]
		int limit = 100,
		CancellationToken cancellationToken = default)
	{
		Page(offset, limit);
		CodeLuaFunctionPage page = _dispatch.RunLua(CheatEngineToolNames.CodeListFunctions,
			CodeScripts.ListFunctions, CodeLuaJsonContext.Default.CodeLuaFunctionPage, cancellationToken, offset,
			limit);
		return new CodeFunctionPage(page.Total, offset, page.Functions, page.NextOffset);
	}

	/// <summary>Clears Cheat Engine's code-dissector state when no MCP dissect job is active.</summary>
	[McpServerTool(Name = CheatEngineToolNames.CodeClearDissect, Title = "Clear code dissect", ReadOnly = false,
		Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Clear Cheat Engine's current code-dissector references, functions and strings. It refuses while an MCP code dissect job runs, because clearing during that native pass would make its output indeterminate.")]
	public CodeClearResult ClearDissect(CancellationToken cancellationToken = default)
	{
		if (Interlocked.CompareExchange(ref _dissectState, -1, 0) != 0)
		{
			throw CheatEngineToolException.Busy("A code dissect job is running.",
				"Poll or stop it before clearing the code dissector.");
		}

		try
		{
			CodeLuaCleared cleared = _dispatch.RunLua(CheatEngineToolNames.CodeClearDissect, CodeScripts.ClearDissect,
				CodeLuaJsonContext.Default.CodeLuaCleared, cancellationToken);
			return new CodeClearResult(cleared.Cleared);
		}
		finally
		{
			Volatile.Write(ref _dissectState, 0);
		}
	}

	/// <summary>Reads comments at a bounded list of target addresses.</summary>
	[McpServerTool(Name = CheatEngineToolNames.CodeGetComments, Title = "Get code comments", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Read user-defined Memory View comments at 1 to 256 addresses. A result with comment null means no comment is set at that address.")]
	public CodeComments GetComments(
		[Description("The addresses or expressions to inspect, 1 to 256 entries.")]
		string[] addresses,
		CancellationToken cancellationToken = default)
	{
		if (addresses is null || addresses.Length is < 1 or > MaximumCommentAddresses)
		{
			throw CheatEngineToolException.InvalidArgument("addresses",
				$"must list 1 to {MaximumCommentAddresses} addresses.");
		}

		string[] expressions = new string[addresses.Length];
		for (int index = 0; index < addresses.Length; index++)
		{
			expressions[index] = MemoryTargets.RequireExpression(addresses[index], $"addresses[{index}]");
		}

		CodeLuaComments comments = _dispatch.RunLua(CheatEngineToolNames.CodeGetComments, CodeScripts.GetComments,
			CodeLuaJsonContext.Default.CodeLuaComments, cancellationToken, expressions);
		return new CodeComments([
			.. comments.Comments.Select(comment => new CodeComment(comment.Address, comment.Comment))
		]);
	}

	/// <summary>Sets one user-defined Memory View comment.</summary>
	[McpServerTool(Name = CheatEngineToolNames.CodeSetComment, Title = "Set code comment", ReadOnly = false,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Set or replace the user-defined Memory View comment at one address. The comment is Cheat Engine UI state and is saved with the table; use an empty string to retain an explicitly blank comment.")]
	public CodeComment SetComment(
		[Description("The address or expression to annotate.")]
		string address,
		[Description("The comment text, 0 to 4096 characters.")]
		string comment,
		CancellationToken cancellationToken = default)
	{
		string expression = MemoryTargets.RequireExpression(address, "address");
		if (comment is null || comment.Length > MaximumCommentLength)
		{
			throw CheatEngineToolException.InvalidArgument("comment",
				$"must contain at most {MaximumCommentLength} characters.");
		}

		CodeLuaComments result = _dispatch.RunLua(CheatEngineToolNames.CodeSetComment, CodeScripts.SetComment,
			CodeLuaJsonContext.Default.CodeLuaComments, cancellationToken, expression, comment);
		CodeLuaComment changed = result.Comments.Single();
		return new CodeComment(changed.Address, changed.Comment);
	}

	private async Task RunDissectAsync(JobWriter<CodeJobEvent> writer, Address target, long epoch, int size,
		CancellationToken cancellationToken)
	{
		try
		{
			writer.Progress(0, size);
			CodeLuaDissected dissected = await CodeJobs.DispatchAsync(_dispatch, CheatEngineToolNames.CodeStartDissect,
				token =>
				{
					ICheatEngineClient client = _dispatch.Client;
					MemoryTargets.RequireSameTarget(client, epoch, CheatEngineToolNames.CodeStartDissect, token);
					return _dispatch.ExecuteLua(CheatEngineToolNames.CodeStartDissect, CodeScripts.Dissect,
						CodeLuaJsonContext.Default.CodeLuaDissected, token, target.ToUInt64(), size);
				}, cancellationToken).ConfigureAwait(false);
			writer.Progress(size, size);
			writer.Add(new CodeJobEvent("dissect", dissected.Address, null, dissected.Size));
		}
		finally
		{
			Volatile.Write(ref _dissectState, 0);
		}
	}

	private async Task RunSearchAsync(JobWriter<CodeJobEvent> writer, Address target, long epoch, int size,
		string textContains, CancellationToken cancellationToken)
	{
		Address current = target;
		int consumed = 0;
		while (consumed < size)
		{
			cancellationToken.ThrowIfCancellationRequested();
			AssemblyInstructionSnapshot decoded = await CodeJobs.DispatchAsync(_dispatch,
				CheatEngineToolNames.CodeStartSearch, token =>
				{
					ICheatEngineClient client = _dispatch.Client;
					MemoryTargets.RequireSameTarget(client, epoch, CheatEngineToolNames.CodeStartSearch, token);
					return CorrectColumns(_dispatch, client.Assembly.Disassemble(current, token),
						CheatEngineToolNames.CodeStartSearch, token);
				}, cancellationToken).ConfigureAwait(false);
			CodeInstruction instruction = Instruction(decoded, CheatEngineToolNames.CodeStartSearch);
			if (instruction.Text.Contains(textContains, StringComparison.OrdinalIgnoreCase))
			{
				writer.Add(new CodeJobEvent("search", instruction.Address, instruction));
			}

			consumed += instruction.Size;
			writer.Progress(Math.Min(consumed, size), size);
			current = decoded.Address + decoded.Length;
		}
	}

	/// <summary>Copies CE's actual display columns while retaining Client-owned instruction bytes and length.</summary>
	/// <param name="dispatch">The enclosing Client dispatch.</param>
	/// <param name="instruction">The typed instruction snapshot.</param>
	/// <param name="operation">The tool name.</param>
	/// <param name="cancellationToken">The enclosing dispatch token.</param>
	/// <returns>The snapshot with corrected display columns.</returns>
	internal static AssemblyInstructionSnapshot CorrectColumns(ToolDispatch dispatch,
		AssemblyInstructionSnapshot instruction, string operation, CancellationToken cancellationToken)
	{
		if (instruction.Length <= 0)
		{
			throw NonPositiveLength(operation);
		}

		// Client 1.0 / SDK 2.0 follows the documented split order; CE 7.7 returns extra, opcode, bytes, address.
		CodeLuaDisassemblyColumns columns = dispatch.ExecuteLua(operation, CodeScripts.DisassemblyColumns,
			CodeLuaJsonContext.Default.CodeLuaDisassemblyColumns, cancellationToken, instruction.Address.Value);
		return new AssemblyInstructionSnapshot(instruction.Address, instruction.Length, columns.AddressText,
			columns.Opcode, columns.Extra, instruction.Bytes.AsSpan());
	}

	/// <summary>Copies one decoded instruction into the contract form.</summary>
	/// <param name="instruction">Cheat Engine's copied instruction.</param>
	/// <param name="operation">The tool name, for a malformed host instruction.</param>
	/// <returns>The contract instruction.</returns>
	internal static CodeInstruction Instruction(AssemblyInstructionSnapshot instruction, string operation)
	{
		if (instruction.Length <= 0)
		{
			throw NonPositiveLength(operation);
		}

		return new CodeInstruction(HexFormat.Address(instruction.Address), instruction.AddressText, instruction.Opcode,
			instruction.Extra, instruction.Text, Convert.ToHexString(instruction.Bytes.AsSpan()), instruction.Length);
	}

	/// <summary>The contract error for a decoded instruction without a positive byte length.</summary>
	/// <param name="operation">The tool name.</param>
	/// <returns>The exception to throw.</returns>
	internal static CheatEngineToolException NonPositiveLength(string operation)
	{
		return new CheatEngineToolException(new ToolError(ToolErrorKind.HostRefused,
			"Cheat Engine returned a decoded instruction without a positive byte length.", operation,
			ToolHostEffect.Completed, false, "Inspect the target's code region, then retry the disassembly."));
	}

	private static void Page(int offset, int limit)
	{
		RequireRange(offset, "offset", 0, int.MaxValue);
		RequireRange(limit, "limit", 1, MaximumPage);
	}

	/// <summary>Checks an inclusive integer range before any dispatch, as <c>invalid_argument</c> like the domain's paging.</summary>
	/// <param name="value">The caller's value.</param>
	/// <param name="parameter">The parameter that carried it.</param>
	/// <param name="minimum">The smallest accepted value.</param>
	/// <param name="maximum">The largest accepted value.</param>
	internal static void RequireRange(int value, string parameter, int minimum, int maximum)
	{
		if (value < minimum || value > maximum)
		{
			throw CheatEngineToolException.InvalidArgument(parameter, $"must be between {minimum} and {maximum}.");
		}
	}

	/// <summary>
	///     Checks hexadecimal byte text and returns its digits without whitespace, because Cheat Engine's byte parser
	///     splits words only at spaces, commas and dashes and pairs digits within each word.
	/// </summary>
	/// <param name="value">The caller's text.</param>
	/// <returns>The contiguous uppercase digits.</returns>
	private static string NormalizeHexadecimalBytes(string? value)
	{
		if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumByteTextLength)
		{
			throw CheatEngineToolException.InvalidArgument("hexadecimalBytes",
				$"must contain 1 to {MaximumByteTextLength} characters.");
		}

		StringBuilder digits = new(value.Length);
		foreach (char character in value)
		{
			if (char.IsWhiteSpace(character))
			{
				continue;
			}

			if (!char.IsAsciiHexDigit(character))
			{
				throw CheatEngineToolException.InvalidArgument("hexadecimalBytes",
					"must contain hexadecimal bytes with optional whitespace.");
			}

			digits.Append(char.ToUpperInvariant(character));
		}

		if (digits.Length == 0 || digits.Length % 2 != 0)
		{
			throw CheatEngineToolException.InvalidArgument("hexadecimalBytes",
				"must contain a non-empty, even number of hexadecimal digits.");
		}

		return digits.ToString();
	}
}

/// <summary>Runs short code-job dispatches without allowing a background job to starve tool calls.</summary>
internal static class CodeJobs
{
	private static readonly TimeSpan BusyRetryDelay = TimeSpan.FromMilliseconds(20);

	internal static async Task<T> DispatchAsync<T>(ToolDispatch dispatch, string operation,
		Func<CancellationToken, T> body, CancellationToken cancellationToken)
	{
		while (true)
		{
			cancellationToken.ThrowIfCancellationRequested();
			try
			{
				return dispatch.Run(operation, body, cancellationToken);
			}
			catch (CheatEngineToolException exception) when (exception.Error.Kind is ToolErrorKind.Busy &&
															 exception.Error.HostEffect is ToolHostEffect.NotStarted)
			{
				await Task.Delay(BusyRetryDelay, cancellationToken).ConfigureAwait(false);
			}
		}
	}
}
