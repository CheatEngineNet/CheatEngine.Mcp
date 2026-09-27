using System.Collections.Immutable;
using System.ComponentModel;
using System.Globalization;

using CheatEngine.Client;
using CheatEngine.Client.Inspection;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.SDK.Engine.Inspection;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools.Modules;

/// <summary>The <c>module_list</c> and <c>module_get</c> tools: the target's modules and one module's PE layout.</summary>
[McpServerToolType]
public sealed class ModuleTools
{
	/// <summary>The largest page of <c>module_list</c>.</summary>
	internal const int MaximumLimit = 1000;

	/// <summary>The most sections copied for one module.</summary>
	internal const int MaximumSections = 4096;

	private readonly ToolDispatch _dispatch;

	/// <summary>Creates the tools; the constructor does no Cheat Engine work.</summary>
	/// <param name="dispatch">The activation's dispatch facade.</param>
	public ModuleTools(ToolDispatch dispatch)
	{
		ArgumentNullException.ThrowIfNull(dispatch);
		_dispatch = dispatch;
	}

	/// <summary>Pages the modules of the attached process or of another process.</summary>
	/// <param name="processId">Another process's id; the attached process when omitted.</param>
	/// <param name="nameContains">A case-insensitive substring of the module name.</param>
	/// <param name="offset">The index of the first module to return.</param>
	/// <param name="limit">The most modules to return.</param>
	/// <param name="format">How much to return per module.</param>
	/// <param name="cancellationToken">The request's token.</param>
	/// <returns>The page.</returns>
	[McpServerTool(Name = CheatEngineToolNames.ModuleList, Title = "List modules", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Page the modules (EXE and DLLs) loaded in the attached process, or in another process by processId, in Cheat Engine's order. concise returns name, base and size; detailed adds is64Bit and the file path. Filter with nameContains, then page with offset and limit (at most 1000). Use it to classify the runtime (mono, GameAssembly, coreclr) and to find a module for module_get.")]
	public ModuleList List(
		[Description("Another process's id; omit it for the attached process.")]
		int? processId = null,
		[Description("A case-insensitive substring of the module name, such as mono or .dll; at most 256 characters.")]
		string? nameContains = null,
		[Description("The index of the first module to return.")]
		int offset = 0,
		[Description("The most modules to return, 1 to 1000.")]
		int limit = 200,
		[Description("concise (name, base, size) or detailed (adds is64Bit and path).")]
		ResultFormat format = ResultFormat.Concise,
		CancellationToken cancellationToken = default)
	{
		if (processId is <= 0)
		{
			throw CheatEngineToolException.InvalidArgument("processId", "must be a positive process id.");
		}

		string? filter = ModuleLocator.OptionalFilter(nameContains, "nameContains");
		_ = Paging.Slice(Array.Empty<ModuleInfo>(), offset, limit, MaximumLimit);
		ICheatEngineClient client = _dispatch.Client;
		(int pid, ImmutableArray<ModuleInfo> modules) = _dispatch.Run(CheatEngineToolNames.ModuleList, token =>
		{
			TargetProcessId? requested = processId is { } value ? new TargetProcessId(value) : null;
			int owner = processId ?? client.Processes.GetCurrentProcess(token).Id.Value;
			return (owner, ModuleLocator.GetModules(client, requested, token));
		}, cancellationToken);

		ModuleInfo[] matching = filter is null
			? [.. modules]
			: [.. modules.Where(module => module.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))];
		PageSlice<ModuleInfo> page = Paging.Slice(matching, offset, limit, MaximumLimit);
		bool detailed = format is ResultFormat.Detailed;
		ModuleEntry[] entries =
		[
			.. page.Items.Select(module => new ModuleEntry(module.Name, HexFormat.Address(module.BaseAddress),
				Size(module.ImageSize), detailed ? module.Is64Bit : null, detailed ? module.PathToFile : null))
		];
		return new ModuleList(pid, page.Total, entries, page.NextOffset);
	}

	/// <summary>Reads one module's sections and PE header fields.</summary>
	/// <param name="module">The module name, or an address inside it.</param>
	/// <param name="cancellationToken">The request's token.</param>
	/// <returns>The module details.</returns>
	[McpServerTool(Name = CheatEngineToolNames.ModuleGet, Title = "Get module details", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Read one module of the attached process: base, size, file path, its sections (with executable and writable flags) and the fields of its mapped PE header: machine, COFF time stamp, entry point, subsystem, DLL and .NET flags and the PDB it names. The time stamp identifies the build, so keep it to tell later whether the game was updated. module is a name such as game.exe (case-insensitive) or any address expression inside the module.")]
	public ModuleDetails Get(
		[Description("The module name, such as game.exe, or an address expression inside it, such as game.exe+1000.")]
		string module,
		CancellationToken cancellationToken = default)
	{
		string wanted = ModuleLocator.RequireModule(module);
		ICheatEngineClient client = _dispatch.Client;
		return _dispatch.Run(CheatEngineToolNames.ModuleGet, token =>
		{
			ModuleInfo found = ModuleLocator.Find(client, wanted, token);
			ImmutableArray<ModuleSectionInfo> sections = client.Inspection.GetModuleSections(
				new ModuleName(found.Name), new InspectionCollectionRequest(MaximumSections), token);
			PeHeaders? headers = TryParseHeaders(ModuleLocator.ReadHeaderBytes(client, found, token));
			PdbReference? pdb = headers is null ? null : TryReadPdb(client, found, headers, token);
			ModuleSection[] copied = [.. sections.Select(section => Section(found, headers, section))];
			return new ModuleDetails(found.Name, HexFormat.Address(found.BaseAddress), found.PathToFile,
				found.Is64Bit, copied, Size(found.ImageSize), headers is null ? null : Describe(found, headers, pdb));
		}, cancellationToken);
	}

	internal static long? Size(MemorySize? size)
	{
		return size is { } value ? (long) Math.Min(value.Value, long.MaxValue) : null;
	}

	internal static PeHeaders? TryParseHeaders(byte[] bytes)
	{
		try
		{
			return PeImageReader.ParseHeaders(bytes);
		}
		catch (InvalidDataException)
		{
			return null;
		}
	}

	internal static PeMachine Machine(ushort machine)
	{
		return machine switch
		{
			0x014C => PeMachine.X86,
			0x8664 => PeMachine.X64,
			0x01C4 => PeMachine.Arm,
			0xAA64 => PeMachine.Arm64,
			_ => PeMachine.Unknown
		};
	}

	internal static PeSubsystem Subsystem(ushort subsystem)
	{
		return subsystem switch
		{
			1 => PeSubsystem.Native,
			2 => PeSubsystem.WindowsGui,
			3 => PeSubsystem.WindowsCui,
			10 or 11 or 12 or 13 => PeSubsystem.Efi,
			16 => PeSubsystem.WindowsBootApplication,
			_ => PeSubsystem.Unknown
		};
	}

	private static PdbReference? TryReadPdb(ICheatEngineClient client, ModuleInfo module, PeHeaders headers,
		CancellationToken token)
	{
		ModuleMemoryImage image = new(client, module.Name, module.BaseAddress.ToUInt64(),
			ModuleLocator.MappedSize(module, headers.SizeOfImage), 64 * 1024, token);
		try
		{
			PeCodeView? codeView = PeImageReader.ReadCodeView(headers, image);
			return codeView is null
				? null
				: new PdbReference(codeView.Path, codeView.Guid.ToString("D").ToUpperInvariant(), codeView.Age);
		}
		catch (InvalidDataException)
		{
			return null;
		}
		catch (CheatEngineToolException exception) when (exception.Error.Kind is ToolErrorKind.MemoryReadFailed
															 or ToolErrorKind.LimitExceeded)
		{
			// The PDB reference is optional: an unreadable or oversized debug directory leaves it out.
			return null;
		}
	}

	private static PeImageInfo Describe(ModuleInfo module, PeHeaders headers, PdbReference? pdb)
	{
		ulong baseAddress = module.BaseAddress.ToUInt64();
		DateTimeOffset? stamp = headers.TimeDateStamp == 0
			? null
			: DateTimeOffset.FromUnixTimeSeconds(headers.TimeDateStamp);
		string? entryPoint = headers.AddressOfEntryPoint == 0
			? null
			: HexFormat.Address(unchecked(baseAddress + headers.AddressOfEntryPoint));
		return new PeImageInfo(Machine(headers.Machine),
			headers.TimeDateStamp.ToString("X8", CultureInfo.InvariantCulture), HexFormat.Address(headers.ImageBase),
			Subsystem(headers.Subsystem), headers.IsDll, headers.IsManaged, stamp, entryPoint, pdb);
	}

	private static ModuleSection Section(ModuleInfo module, PeHeaders? headers, ModuleSectionInfo section)
	{
		ulong address = section.Address.ToUInt64();
		ulong baseAddress = module.BaseAddress.ToUInt64();
		PeSection? header = headers is not null && address >= baseAddress && address - baseAddress <= uint.MaxValue
			? headers.Sections.FirstOrDefault(candidate =>
				candidate.VirtualAddress == (uint) (address - baseAddress))
			: null;
		return new ModuleSection(section.Name, HexFormat.Address(address),
			(long) Math.Min(section.Size.Value, long.MaxValue), HexFormat.Address(section.FileOffset.Value),
			header?.IsExecutable, header?.IsWritable);
	}
}
