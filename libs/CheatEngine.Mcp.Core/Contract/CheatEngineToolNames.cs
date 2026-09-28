using System.Collections.Frozen;

namespace CheatEngine.Mcp.Core.Contract;

/// <summary>
///     The reviewed names of the v2 tool contract: 186 backend tools in 21 domains, in catalog order, and the gateway's
///     own
///     <see cref="InstanceList" />. Tools, prompts, server instructions and error hints cite these constants, so a rename
///     breaks the build instead of the contract, and the startup validator refuses a v2 tool whose name is not listed.
/// </summary>
/// <remarks>
///     A name is <c>domain_verb[_object]</c>: a reviewed domain, then a verb from the closed list of
///     <c>McpContractRules</c>. Adding, renaming or removing a name is a reviewed contract change.
/// </remarks>
public static class CheatEngineToolNames
{
	// instance (1)

	/// <summary>The gateway's own <c>instance_list</c> tool, the only one without an <c>instanceId</c>.</summary>
	public const string InstanceList = "instance_list";

	// runtime (6)

	/// <summary>The <c>runtime_get_info</c> tool.</summary>
	public const string RuntimeGetInfo = "runtime_get_info";

	/// <summary>The <c>runtime_get_overview</c> tool.</summary>
	public const string RuntimeGetOverview = "runtime_get_overview";

	/// <summary>The <c>runtime_list_resources</c> tool.</summary>
	public const string RuntimeListResources = "runtime_list_resources";

	/// <summary>The <c>runtime_release_resources</c> tool.</summary>
	public const string RuntimeReleaseResources = "runtime_release_resources";

	/// <summary>The <c>runtime_list_jobs</c> tool.</summary>
	public const string RuntimeListJobs = "runtime_list_jobs";

	/// <summary>The <c>runtime_stop_job</c> tool.</summary>
	public const string RuntimeStopJob = "runtime_stop_job";

	// process (9)

	/// <summary>The <c>process_list</c> tool.</summary>
	public const string ProcessList = "process_list";

	/// <summary>The <c>process_attach</c> tool.</summary>
	public const string ProcessAttach = "process_attach";

	/// <summary>The <c>process_get_current</c> tool.</summary>
	public const string ProcessGetCurrent = "process_get_current";

	/// <summary>The <c>process_create</c> tool.</summary>
	public const string ProcessCreate = "process_create";

	/// <summary>The <c>process_open_file</c> tool.</summary>
	public const string ProcessOpenFile = "process_open_file";

	/// <summary>The <c>process_save_file</c> tool.</summary>
	public const string ProcessSaveFile = "process_save_file";

	/// <summary>The <c>process_set_paused</c> tool.</summary>
	public const string ProcessSetPaused = "process_set_paused";

	/// <summary>The <c>process_list_threads</c> tool.</summary>
	public const string ProcessListThreads = "process_list_threads";

	/// <summary>The <c>process_set_pointer_size</c> tool.</summary>
	public const string ProcessSetPointerSize = "process_set_pointer_size";

	// memory (19)

	/// <summary>The <c>memory_read</c> tool.</summary>
	public const string MemoryRead = "memory_read";

	/// <summary>The <c>memory_read_batch</c> tool.</summary>
	public const string MemoryReadBatch = "memory_read_batch";

	/// <summary>The <c>memory_write</c> tool.</summary>
	public const string MemoryWrite = "memory_write";

	/// <summary>The <c>memory_write_batch</c> tool.</summary>
	public const string MemoryWriteBatch = "memory_write_batch";

	/// <summary>The <c>memory_get_address_info</c> tool.</summary>
	public const string MemoryGetAddressInfo = "memory_get_address_info";

	/// <summary>The <c>memory_list_regions</c> tool.</summary>
	public const string MemoryListRegions = "memory_list_regions";

	/// <summary>The <c>memory_set_protection</c> tool.</summary>
	public const string MemorySetProtection = "memory_set_protection";

	/// <summary>The <c>memory_allocate</c> tool.</summary>
	public const string MemoryAllocate = "memory_allocate";

	/// <summary>The <c>memory_free</c> tool.</summary>
	public const string MemoryFree = "memory_free";

	/// <summary>The <c>memory_copy</c> tool.</summary>
	public const string MemoryCopy = "memory_copy";

	/// <summary>The <c>memory_compare</c> tool.</summary>
	public const string MemoryCompare = "memory_compare";

	/// <summary>The <c>memory_hash</c> tool.</summary>
	public const string MemoryHash = "memory_hash";

	/// <summary>The <c>memory_dump_to_file</c> tool.</summary>
	public const string MemoryDumpToFile = "memory_dump_to_file";

	/// <summary>The <c>memory_load_from_file</c> tool.</summary>
	public const string MemoryLoadFromFile = "memory_load_from_file";

	/// <summary>The <c>memory_create_snapshot</c> tool.</summary>
	public const string MemoryCreateSnapshot = "memory_create_snapshot";

	/// <summary>The <c>memory_compare_snapshot</c> tool.</summary>
	public const string MemoryCompareSnapshot = "memory_compare_snapshot";

	/// <summary>The <c>memory_list_snapshots</c> tool.</summary>
	public const string MemoryListSnapshots = "memory_list_snapshots";

	/// <summary>The <c>memory_delete_snapshot</c> tool.</summary>
	public const string MemoryDeleteSnapshot = "memory_delete_snapshot";

	/// <summary>The <c>memory_read_samples</c> tool.</summary>
	public const string MemoryReadSamples = "memory_read_samples";

	// scan (8)

	/// <summary>The <c>scan_first</c> tool.</summary>
	public const string ScanFirst = "scan_first";

	/// <summary>The <c>scan_next</c> tool.</summary>
	public const string ScanNext = "scan_next";

	/// <summary>The <c>scan_get_status</c> tool.</summary>
	public const string ScanGetStatus = "scan_get_status";

	/// <summary>The <c>scan_list_results</c> tool.</summary>
	public const string ScanListResults = "scan_list_results";

	/// <summary>The <c>scan_list_scanners</c> tool.</summary>
	public const string ScanListScanners = "scan_list_scanners";

	/// <summary>The <c>scan_reset</c> tool.</summary>
	public const string ScanReset = "scan_reset";

	/// <summary>The <c>scan_delete</c> tool.</summary>
	public const string ScanDelete = "scan_delete";

	/// <summary>The <c>scan_stop</c> tool.</summary>
	public const string ScanStop = "scan_stop";

	// aob (3)

	/// <summary>The <c>aob_find</c> tool.</summary>
	public const string AobFind = "aob_find";

	/// <summary>The <c>aob_generate_signature</c> tool.</summary>
	public const string AobGenerateSignature = "aob_generate_signature";

	/// <summary>The <c>aob_find_value</c> tool.</summary>
	public const string AobFindValue = "aob_find_value";

	// pointer (14)

	/// <summary>The <c>pointer_read_chain</c> tool.</summary>
	public const string PointerReadChain = "pointer_read_chain";

	/// <summary>The <c>pointer_find_references</c> tool.</summary>
	public const string PointerFindReferences = "pointer_find_references";

	/// <summary>The <c>pointer_create_map</c> tool.</summary>
	public const string PointerCreateMap = "pointer_create_map";

	/// <summary>The <c>pointer_list_maps</c> tool.</summary>
	public const string PointerListMaps = "pointer_list_maps";

	/// <summary>The <c>pointer_delete_map</c> tool.</summary>
	public const string PointerDeleteMap = "pointer_delete_map";

	/// <summary>The <c>pointer_find_paths</c> tool.</summary>
	public const string PointerFindPaths = "pointer_find_paths";

	/// <summary>The <c>pointer_rescan_paths</c> tool.</summary>
	public const string PointerRescanPaths = "pointer_rescan_paths";

	/// <summary>The <c>pointer_list_paths</c> tool.</summary>
	public const string PointerListPaths = "pointer_list_paths";

	/// <summary>The <c>pointer_list_scans</c> tool.</summary>
	public const string PointerListScans = "pointer_list_scans";

	/// <summary>The <c>pointer_delete_scan</c> tool.</summary>
	public const string PointerDeleteScan = "pointer_delete_scan";

	/// <summary>The <c>pointer_save_map</c> tool.</summary>
	public const string PointerSaveMap = "pointer_save_map";

	/// <summary>The <c>pointer_load_map</c> tool.</summary>
	public const string PointerLoadMap = "pointer_load_map";

	/// <summary>The <c>pointer_save_scan</c> tool.</summary>
	public const string PointerSaveScan = "pointer_save_scan";

	/// <summary>The <c>pointer_load_scan</c> tool.</summary>
	public const string PointerLoadScan = "pointer_load_scan";

	// module (5)

	/// <summary>The <c>module_list</c> tool.</summary>
	public const string ModuleList = "module_list";

	/// <summary>The <c>module_get</c> tool.</summary>
	public const string ModuleGet = "module_get";

	/// <summary>The <c>module_list_exports</c> tool.</summary>
	public const string ModuleListExports = "module_list_exports";

	/// <summary>The <c>module_list_imports</c> tool.</summary>
	public const string ModuleListImports = "module_list_imports";

	/// <summary>The <c>module_find_patches</c> tool.</summary>
	public const string ModuleFindPatches = "module_find_patches";

	// symbol (10)

	/// <summary>The <c>symbol_resolve</c> tool.</summary>
	public const string SymbolResolve = "symbol_resolve";

	/// <summary>The <c>symbol_find</c> tool.</summary>
	public const string SymbolFind = "symbol_find";

	/// <summary>The <c>symbol_register</c> tool.</summary>
	public const string SymbolRegister = "symbol_register";

	/// <summary>The <c>symbol_unregister</c> tool.</summary>
	public const string SymbolUnregister = "symbol_unregister";

	/// <summary>The <c>symbol_list_registered</c> tool.</summary>
	public const string SymbolListRegistered = "symbol_list_registered";

	/// <summary>The <c>symbol_get_module_preference</c> tool.</summary>
	public const string SymbolGetModulePreference = "symbol_get_module_preference";

	/// <summary>The <c>symbol_set_module_preference</c> tool.</summary>
	public const string SymbolSetModulePreference = "symbol_set_module_preference";

	/// <summary>The <c>symbol_reload</c> tool.</summary>
	public const string SymbolReload = "symbol_reload";

	/// <summary>The <c>symbol_add_module</c> tool.</summary>
	public const string SymbolAddModule = "symbol_add_module";

	/// <summary>The <c>symbol_enable_sources</c> tool.</summary>
	public const string SymbolEnableSources = "symbol_enable_sources";

	// speedhack (2)

	/// <summary>The <c>speedhack_get_state</c> tool.</summary>
	public const string SpeedhackGetState = "speedhack_get_state";

	/// <summary>The <c>speedhack_set_speed</c> tool.</summary>
	public const string SpeedhackSetSpeed = "speedhack_set_speed";

	// util (2)

	/// <summary>The <c>util_convert_value</c> tool.</summary>
	public const string UtilConvertValue = "util_convert_value";

	/// <summary>The <c>util_calculate</c> tool.</summary>
	public const string UtilCalculate = "util_calculate";

	// code (14)

	/// <summary>The <c>code_disassemble</c> tool.</summary>
	public const string CodeDisassemble = "code_disassemble";

	/// <summary>The <c>code_decode</c> tool.</summary>
	public const string CodeDecode = "code_decode";

	/// <summary>The <c>code_disassemble_bytes</c> tool.</summary>
	public const string CodeDisassembleBytes = "code_disassemble_bytes";

	/// <summary>The <c>code_get_function</c> tool.</summary>
	public const string CodeGetFunction = "code_get_function";

	/// <summary>The <c>code_get_function_graph</c> tool.</summary>
	public const string CodeGetFunctionGraph = "code_get_function_graph";

	/// <summary>The <c>code_start_dissect</c> tool.</summary>
	public const string CodeStartDissect = "code_start_dissect";

	/// <summary>The <c>code_start_search</c> tool.</summary>
	public const string CodeStartSearch = "code_start_search";

	/// <summary>The <c>code_poll_job</c> tool.</summary>
	public const string CodePollJob = "code_poll_job";

	/// <summary>The <c>code_find_references</c> tool.</summary>
	public const string CodeFindReferences = "code_find_references";

	/// <summary>The <c>code_find_strings</c> tool.</summary>
	public const string CodeFindStrings = "code_find_strings";

	/// <summary>The <c>code_list_functions</c> tool.</summary>
	public const string CodeListFunctions = "code_list_functions";

	/// <summary>The <c>code_clear_dissect</c> tool.</summary>
	public const string CodeClearDissect = "code_clear_dissect";

	/// <summary>The <c>code_get_comments</c> tool.</summary>
	public const string CodeGetComments = "code_get_comments";

	/// <summary>The <c>code_set_comment</c> tool.</summary>
	public const string CodeSetComment = "code_set_comment";

	// asm (8)

	/// <summary>The <c>asm_assemble</c> tool.</summary>
	public const string AsmAssemble = "asm_assemble";

	/// <summary>The <c>asm_check</c> tool.</summary>
	public const string AsmCheck = "asm_check";

	/// <summary>The <c>asm_apply</c> tool.</summary>
	public const string AsmApply = "asm_apply";

	/// <summary>The <c>asm_apply_code_patch</c> tool.</summary>
	public const string AsmApplyCodePatch = "asm_apply_code_patch";

	/// <summary>The <c>asm_release_patch</c> tool.</summary>
	public const string AsmReleasePatch = "asm_release_patch";

	/// <summary>The <c>asm_list_patches</c> tool.</summary>
	public const string AsmListPatches = "asm_list_patches";

	/// <summary>The <c>asm_generate_injection</c> tool.</summary>
	public const string AsmGenerateInjection = "asm_generate_injection";

	/// <summary>The <c>asm_generate_api_hook</c> tool.</summary>
	public const string AsmGenerateApiHook = "asm_generate_api_hook";

	// record (14)

	/// <summary>The <c>record_list</c> tool.</summary>
	public const string RecordList = "record_list";

	/// <summary>The <c>record_get</c> tool.</summary>
	public const string RecordGet = "record_get";

	/// <summary>The <c>record_find</c> tool.</summary>
	public const string RecordFind = "record_find";

	/// <summary>The <c>record_get_selected</c> tool.</summary>
	public const string RecordGetSelected = "record_get_selected";

	/// <summary>The <c>record_select</c> tool.</summary>
	public const string RecordSelect = "record_select";

	/// <summary>The <c>record_create</c> tool.</summary>
	public const string RecordCreate = "record_create";

	/// <summary>The <c>record_update</c> tool.</summary>
	public const string RecordUpdate = "record_update";

	/// <summary>The <c>record_set_active</c> tool.</summary>
	public const string RecordSetActive = "record_set_active";

	/// <summary>The <c>record_delete</c> tool.</summary>
	public const string RecordDelete = "record_delete";

	/// <summary>The <c>record_move</c> tool.</summary>
	public const string RecordMove = "record_move";

	/// <summary>The <c>record_group</c> tool.</summary>
	public const string RecordGroup = "record_group";

	/// <summary>The <c>record_set_script</c> tool.</summary>
	public const string RecordSetScript = "record_set_script";

	/// <summary>The <c>record_set_dropdown</c> tool.</summary>
	public const string RecordSetDropdown = "record_set_dropdown";

	/// <summary>The <c>record_clear</c> tool.</summary>
	public const string RecordClear = "record_clear";

	// table (3)

	/// <summary>The <c>table_load</c> tool.</summary>
	public const string TableLoad = "table_load";

	/// <summary>The <c>table_save</c> tool.</summary>
	public const string TableSave = "table_save";

	/// <summary>The <c>table_list_files</c> tool.</summary>
	public const string TableListFiles = "table_list_files";

	// structure (15)

	/// <summary>The <c>structure_list</c> tool.</summary>
	public const string StructureList = "structure_list";

	/// <summary>The <c>structure_get</c> tool.</summary>
	public const string StructureGet = "structure_get";

	/// <summary>The <c>structure_create</c> tool.</summary>
	public const string StructureCreate = "structure_create";

	/// <summary>The <c>structure_delete</c> tool.</summary>
	public const string StructureDelete = "structure_delete";

	/// <summary>The <c>structure_add_elements</c> tool.</summary>
	public const string StructureAddElements = "structure_add_elements";

	/// <summary>The <c>structure_update_elements</c> tool.</summary>
	public const string StructureUpdateElements = "structure_update_elements";

	/// <summary>The <c>structure_remove_elements</c> tool.</summary>
	public const string StructureRemoveElements = "structure_remove_elements";

	/// <summary>The <c>structure_autoguess</c> tool.</summary>
	public const string StructureAutoguess = "structure_autoguess";

	/// <summary>The <c>structure_fill_from_dotnet</c> tool.</summary>
	public const string StructureFillFromDotNet = "structure_fill_from_dotnet";

	/// <summary>The <c>structure_get_pdb_layout</c> tool.</summary>
	public const string StructureGetPdbLayout = "structure_get_pdb_layout";

	/// <summary>The <c>structure_read</c> tool.</summary>
	public const string StructureRead = "structure_read";

	/// <summary>The <c>structure_write_element</c> tool.</summary>
	public const string StructureWriteElement = "structure_write_element";

	/// <summary>The <c>structure_compare</c> tool.</summary>
	public const string StructureCompare = "structure_compare";

	/// <summary>The <c>structure_generate_c_header</c> tool.</summary>
	public const string StructureGenerateCHeader = "structure_generate_c_header";

	/// <summary>The <c>structure_set_name</c> tool.</summary>
	public const string StructureSetName = "structure_set_name";

	// debugger (18)

	/// <summary>The <c>debugger_attach</c> tool.</summary>
	public const string DebuggerAttach = "debugger_attach";

	/// <summary>The <c>debugger_detach</c> tool.</summary>
	public const string DebuggerDetach = "debugger_detach";

	/// <summary>The <c>debugger_get_status</c> tool.</summary>
	public const string DebuggerGetStatus = "debugger_get_status";

	/// <summary>The <c>debugger_break_thread</c> tool.</summary>
	public const string DebuggerBreakThread = "debugger_break_thread";

	/// <summary>The <c>debugger_set_breakpoint</c> tool.</summary>
	public const string DebuggerSetBreakpoint = "debugger_set_breakpoint";

	/// <summary>The <c>debugger_delete_breakpoint</c> tool.</summary>
	public const string DebuggerDeleteBreakpoint = "debugger_delete_breakpoint";

	/// <summary>The <c>debugger_list_breakpoints</c> tool.</summary>
	public const string DebuggerListBreakpoints = "debugger_list_breakpoints";

	/// <summary>The <c>debugger_continue</c> tool.</summary>
	public const string DebuggerContinue = "debugger_continue";

	/// <summary>The <c>debugger_step</c> tool.</summary>
	public const string DebuggerStep = "debugger_step";

	/// <summary>The <c>debugger_get_context</c> tool.</summary>
	public const string DebuggerGetContext = "debugger_get_context";

	/// <summary>The <c>debugger_set_register</c> tool.</summary>
	public const string DebuggerSetRegister = "debugger_set_register";

	/// <summary>The <c>debugger_set_thread_ignored</c> tool.</summary>
	public const string DebuggerSetThreadIgnored = "debugger_set_thread_ignored";

	/// <summary>The <c>debugger_get_stack_trace</c> tool.</summary>
	public const string DebuggerGetStackTrace = "debugger_get_stack_trace";

	/// <summary>The <c>debugger_start_capture</c> tool.</summary>
	public const string DebuggerStartCapture = "debugger_start_capture";

	/// <summary>The <c>debugger_poll_capture</c> tool.</summary>
	public const string DebuggerPollCapture = "debugger_poll_capture";

	/// <summary>The <c>debugger_start_trace</c> tool.</summary>
	public const string DebuggerStartTrace = "debugger_start_trace";

	/// <summary>The <c>debugger_poll_trace</c> tool.</summary>
	public const string DebuggerPollTrace = "debugger_poll_trace";

	/// <summary>The <c>debugger_run_to</c> tool.</summary>
	public const string DebuggerRunTo = "debugger_run_to";

	// exec (6)

	/// <summary>The <c>exec_inject_library</c> tool.</summary>
	public const string ExecInjectLibrary = "exec_inject_library";

	/// <summary>The <c>exec_inject_dotnet</c> tool.</summary>
	public const string ExecInjectDotNet = "exec_inject_dotnet";

	/// <summary>The <c>exec_call_remote</c> tool.</summary>
	public const string ExecCallRemote = "exec_call_remote";

	/// <summary>The <c>exec_call_method</c> tool.</summary>
	public const string ExecCallMethod = "exec_call_method";

	/// <summary>The <c>exec_call_local</c> tool.</summary>
	public const string ExecCallLocal = "exec_call_local";

	/// <summary>The <c>exec_compile_c</c> tool.</summary>
	public const string ExecCompileC = "exec_compile_c";

	// dotnet (10)

	/// <summary>The <c>dotnet_get_status</c> tool.</summary>
	public const string DotNetGetStatus = "dotnet_get_status";

	/// <summary>The <c>dotnet_list_domains</c> tool.</summary>
	public const string DotNetListDomains = "dotnet_list_domains";

	/// <summary>The <c>dotnet_list_modules</c> tool.</summary>
	public const string DotNetListModules = "dotnet_list_modules";

	/// <summary>The <c>dotnet_list_types</c> tool.</summary>
	public const string DotNetListTypes = "dotnet_list_types";

	/// <summary>The <c>dotnet_get_type</c> tool.</summary>
	public const string DotNetGetType = "dotnet_get_type";

	/// <summary>The <c>dotnet_list_methods</c> tool.</summary>
	public const string DotNetListMethods = "dotnet_list_methods";

	/// <summary>The <c>dotnet_get_method_parameters</c> tool.</summary>
	public const string DotNetGetMethodParameters = "dotnet_get_method_parameters";

	/// <summary>The <c>dotnet_get_object</c> tool.</summary>
	public const string DotNetGetObject = "dotnet_get_object";

	/// <summary>The <c>dotnet_start_instance_search</c> tool.</summary>
	public const string DotNetStartInstanceSearch = "dotnet_start_instance_search";

	/// <summary>The <c>dotnet_poll_instance_search</c> tool.</summary>
	public const string DotNetPollInstanceSearch = "dotnet_poll_instance_search";

	// mono (15)

	/// <summary>The <c>mono_attach</c> tool.</summary>
	public const string MonoAttach = "mono_attach";

	/// <summary>The <c>mono_detach</c> tool.</summary>
	public const string MonoDetach = "mono_detach";

	/// <summary>The <c>mono_get_status</c> tool.</summary>
	public const string MonoGetStatus = "mono_get_status";

	/// <summary>The <c>mono_list_assemblies</c> tool.</summary>
	public const string MonoListAssemblies = "mono_list_assemblies";

	/// <summary>The <c>mono_list_classes</c> tool.</summary>
	public const string MonoListClasses = "mono_list_classes";

	/// <summary>The <c>mono_find_class</c> tool.</summary>
	public const string MonoFindClass = "mono_find_class";

	/// <summary>The <c>mono_list_fields</c> tool.</summary>
	public const string MonoListFields = "mono_list_fields";

	/// <summary>The <c>mono_list_methods</c> tool.</summary>
	public const string MonoListMethods = "mono_list_methods";

	/// <summary>The <c>mono_find_method</c> tool.</summary>
	public const string MonoFindMethod = "mono_find_method";

	/// <summary>The <c>mono_get_static_field_address</c> tool.</summary>
	public const string MonoGetStaticFieldAddress = "mono_get_static_field_address";

	/// <summary>The <c>mono_compile_method</c> tool.</summary>
	public const string MonoCompileMethod = "mono_compile_method";

	/// <summary>The <c>mono_invoke_method</c> tool.</summary>
	public const string MonoInvokeMethod = "mono_invoke_method";

	/// <summary>The <c>mono_get_object</c> tool.</summary>
	public const string MonoGetObject = "mono_get_object";

	/// <summary>The <c>mono_start_instance_search</c> tool.</summary>
	public const string MonoStartInstanceSearch = "mono_start_instance_search";

	/// <summary>The <c>mono_poll_instance_search</c> tool.</summary>
	public const string MonoPollInstanceSearch = "mono_poll_instance_search";

	// kernel (7)

	/// <summary>The <c>kernel_get_status</c> tool.</summary>
	public const string KernelGetStatus = "kernel_get_status";

	/// <summary>The <c>kernel_initialize_dbvm</c> tool.</summary>
	public const string KernelInitializeDbvm = "kernel_initialize_dbvm";

	/// <summary>The <c>kernel_translate_address</c> tool.</summary>
	public const string KernelTranslateAddress = "kernel_translate_address";

	/// <summary>The <c>kernel_read_physical</c> tool.</summary>
	public const string KernelReadPhysical = "kernel_read_physical";

	/// <summary>The <c>kernel_write_physical</c> tool.</summary>
	public const string KernelWritePhysical = "kernel_write_physical";

	/// <summary>The <c>kernel_start_watch</c> tool.</summary>
	public const string KernelStartWatch = "kernel_start_watch";

	/// <summary>The <c>kernel_poll_watch</c> tool.</summary>
	public const string KernelPollWatch = "kernel_poll_watch";

	// lua (2)

	/// <summary>The <c>lua_execute</c> tool.</summary>
	public const string LuaExecute = "lua_execute";

	/// <summary>The <c>lua_find_api</c> tool.</summary>
	public const string LuaFindApi = "lua_find_api";

	/// <summary>The 186 backend tool names; every tool a Cheat Engine instance serves.</summary>
	public static FrozenSet<string> Backend
	{
		get;
	} = FrozenSet.Create(StringComparer.Ordinal,
		RuntimeGetInfo,
		RuntimeGetOverview,
		RuntimeListResources,
		RuntimeReleaseResources,
		RuntimeListJobs,
		RuntimeStopJob,
		ProcessList,
		ProcessAttach,
		ProcessGetCurrent,
		ProcessCreate,
		ProcessOpenFile,
		ProcessSaveFile,
		ProcessSetPaused,
		ProcessListThreads,
		ProcessSetPointerSize,
		MemoryRead,
		MemoryReadBatch,
		MemoryWrite,
		MemoryWriteBatch,
		MemoryGetAddressInfo,
		MemoryListRegions,
		MemorySetProtection,
		MemoryAllocate,
		MemoryFree,
		MemoryCopy,
		MemoryCompare,
		MemoryHash,
		MemoryDumpToFile,
		MemoryLoadFromFile,
		MemoryCreateSnapshot,
		MemoryCompareSnapshot,
		MemoryListSnapshots,
		MemoryDeleteSnapshot,
		MemoryReadSamples,
		ScanFirst,
		ScanNext,
		ScanGetStatus,
		ScanListResults,
		ScanListScanners,
		ScanReset,
		ScanDelete,
		ScanStop,
		AobFind,
		AobGenerateSignature,
		AobFindValue,
		PointerReadChain,
		PointerFindReferences,
		PointerCreateMap,
		PointerListMaps,
		PointerDeleteMap,
		PointerFindPaths,
		PointerRescanPaths,
		PointerListPaths,
		PointerListScans,
		PointerDeleteScan,
		PointerSaveMap,
		PointerLoadMap,
		PointerSaveScan,
		PointerLoadScan,
		ModuleList,
		ModuleGet,
		ModuleListExports,
		ModuleListImports,
		ModuleFindPatches,
		SymbolResolve,
		SymbolFind,
		SymbolRegister,
		SymbolUnregister,
		SymbolListRegistered,
		SymbolGetModulePreference,
		SymbolSetModulePreference,
		SymbolReload,
		SymbolAddModule,
		SymbolEnableSources,
		SpeedhackGetState,
		SpeedhackSetSpeed,
		UtilConvertValue,
		UtilCalculate,
		CodeDisassemble,
		CodeDecode,
		CodeDisassembleBytes,
		CodeGetFunction,
		CodeGetFunctionGraph,
		CodeStartDissect,
		CodeStartSearch,
		CodePollJob,
		CodeFindReferences,
		CodeFindStrings,
		CodeListFunctions,
		CodeClearDissect,
		CodeGetComments,
		CodeSetComment,
		AsmAssemble,
		AsmCheck,
		AsmApply,
		AsmApplyCodePatch,
		AsmReleasePatch,
		AsmListPatches,
		AsmGenerateInjection,
		AsmGenerateApiHook,
		RecordList,
		RecordGet,
		RecordFind,
		RecordGetSelected,
		RecordSelect,
		RecordCreate,
		RecordUpdate,
		RecordSetActive,
		RecordDelete,
		RecordMove,
		RecordGroup,
		RecordSetScript,
		RecordSetDropdown,
		RecordClear,
		TableLoad,
		TableSave,
		TableListFiles,
		StructureList,
		StructureGet,
		StructureCreate,
		StructureDelete,
		StructureAddElements,
		StructureUpdateElements,
		StructureRemoveElements,
		StructureAutoguess,
		StructureFillFromDotNet,
		StructureGetPdbLayout,
		StructureRead,
		StructureWriteElement,
		StructureCompare,
		StructureGenerateCHeader,
		StructureSetName,
		DebuggerAttach,
		DebuggerDetach,
		DebuggerGetStatus,
		DebuggerBreakThread,
		DebuggerSetBreakpoint,
		DebuggerDeleteBreakpoint,
		DebuggerListBreakpoints,
		DebuggerContinue,
		DebuggerStep,
		DebuggerGetContext,
		DebuggerSetRegister,
		DebuggerSetThreadIgnored,
		DebuggerGetStackTrace,
		DebuggerStartCapture,
		DebuggerPollCapture,
		DebuggerStartTrace,
		DebuggerPollTrace,
		DebuggerRunTo,
		ExecInjectLibrary,
		ExecInjectDotNet,
		ExecCallRemote,
		ExecCallMethod,
		ExecCallLocal,
		ExecCompileC,
		DotNetGetStatus,
		DotNetListDomains,
		DotNetListModules,
		DotNetListTypes,
		DotNetGetType,
		DotNetListMethods,
		DotNetGetMethodParameters,
		DotNetGetObject,
		DotNetStartInstanceSearch,
		DotNetPollInstanceSearch,
		MonoAttach,
		MonoDetach,
		MonoGetStatus,
		MonoListAssemblies,
		MonoListClasses,
		MonoFindClass,
		MonoListFields,
		MonoListMethods,
		MonoFindMethod,
		MonoGetStaticFieldAddress,
		MonoCompileMethod,
		MonoInvokeMethod,
		MonoGetObject,
		MonoStartInstanceSearch,
		MonoPollInstanceSearch,
		KernelGetStatus,
		KernelInitializeDbvm,
		KernelTranslateAddress,
		KernelReadPhysical,
		KernelWritePhysical,
		KernelStartWatch,
		KernelPollWatch,
		LuaExecute,
		LuaFindApi);

	/// <summary>Every tool name: <see cref="Backend" /> and the gateway's <see cref="InstanceList" />.</summary>
	public static FrozenSet<string> All
	{
		get;
	} = Backend.Append(InstanceList).ToFrozenSet(StringComparer.Ordinal);
}
