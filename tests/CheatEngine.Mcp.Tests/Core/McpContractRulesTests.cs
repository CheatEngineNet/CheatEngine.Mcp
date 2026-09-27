using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tests.Support;

using Microsoft.Extensions.Options;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tests.Core;

/// <summary>The v2 naming and metadata rules and their startup validation.</summary>
public sealed class McpContractRulesTests
{
	private const string ObjectSchema = """{"type":"object","properties":{"a":{"type":"string"}}}""";

	private const string InputSchema =
		"""{"type":"object","properties":{"address":{"description":"Address expression.","type":"string"}},"required":["address"]}""";

	/// <summary>The 169 backend tool names of the reviewed v2 catalog (plan section 2).</summary>
	public static TheoryData<string> PlanCatalogNames => new(
		"runtime_get_info", "runtime_get_overview", "runtime_list_resources", "runtime_release_resources",
		"runtime_list_jobs", "runtime_stop_job",
		"process_list", "process_attach", "process_get_current", "process_create", "process_open_file",
		"process_save_file", "process_set_paused", "process_list_threads", "process_set_pointer_size",
		"memory_read", "memory_read_batch", "memory_write", "memory_write_batch", "memory_get_address_info",
		"memory_list_regions", "memory_set_protection", "memory_allocate", "memory_free", "memory_copy",
		"memory_compare", "memory_hash", "memory_dump_to_file",
		"scan_first", "scan_next", "scan_get_status", "scan_list_results", "scan_list_scanners", "scan_reset",
		"scan_delete", "scan_stop",
		"aob_find", "aob_generate_signature",
		"pointer_read_chain", "pointer_find_references", "pointer_create_map", "pointer_list_maps",
		"pointer_delete_map", "pointer_find_paths", "pointer_rescan_paths", "pointer_list_paths", "pointer_list_scans",
		"pointer_delete_scan",
		"module_list", "module_get", "module_list_exports", "module_find_patches",
		"symbol_resolve", "symbol_register", "symbol_unregister", "symbol_list_registered",
		"symbol_get_module_preference", "symbol_set_module_preference", "symbol_reload", "symbol_add_module",
		"speedhack_get_state", "speedhack_set_speed",
		"util_convert_value", "util_calculate",
		"code_disassemble", "code_decode", "code_disassemble_bytes", "code_get_function", "code_start_dissect",
		"code_start_search", "code_poll_job", "code_find_references", "code_find_strings", "code_list_functions",
		"code_clear_dissect", "code_get_comments", "code_set_comment",
		"asm_assemble", "asm_check", "asm_apply", "asm_apply_code_patch", "asm_release_patch", "asm_list_patches",
		"asm_generate_injection", "asm_generate_api_hook",
		"record_list", "record_get", "record_find", "record_get_selected", "record_select", "record_create",
		"record_update", "record_set_active", "record_delete", "record_move", "record_group", "record_set_script",
		"table_load", "table_save", "table_list_files",
		"structure_list", "structure_get", "structure_create", "structure_delete", "structure_add_elements",
		"structure_update_elements", "structure_remove_elements", "structure_autoguess", "structure_fill_from_dotnet",
		"structure_get_pdb_layout", "structure_read", "structure_write_element", "structure_compare",
		"debugger_attach", "debugger_detach", "debugger_get_status", "debugger_break_thread",
		"debugger_set_breakpoint", "debugger_delete_breakpoint", "debugger_list_breakpoints", "debugger_continue",
		"debugger_step", "debugger_get_context", "debugger_set_register", "debugger_set_thread_ignored",
		"debugger_get_stack_trace", "debugger_start_capture", "debugger_poll_capture", "debugger_start_trace",
		"debugger_poll_trace", "debugger_run_to",
		"exec_inject_library", "exec_inject_dotnet", "exec_call_remote", "exec_call_method", "exec_call_local",
		"exec_compile_c",
		"dotnet_get_status", "dotnet_list_domains", "dotnet_list_modules", "dotnet_list_types", "dotnet_get_type",
		"dotnet_list_methods", "dotnet_get_object", "dotnet_start_instance_search", "dotnet_poll_instance_search",
		"mono_attach", "mono_detach", "mono_get_status", "mono_list_assemblies", "mono_list_classes",
		"mono_find_class", "mono_list_fields", "mono_list_methods", "mono_find_method",
		"mono_get_static_field_address", "mono_compile_method", "mono_invoke_method", "mono_start_instance_search",
		"mono_poll_instance_search",
		"kernel_get_status", "kernel_initialize_dbvm", "kernel_translate_address", "kernel_read_physical",
		"kernel_write_physical", "kernel_start_watch", "kernel_poll_watch",
		"lua_execute", "lua_find_api");

	[Fact]
	public void PlanCatalogNames_Count_Is169()
	{
		Assert.Equal(169, PlanCatalogNames.Count);
		Assert.Equal(169, PlanCatalogNames.Select(static row => row.Data).Distinct(StringComparer.Ordinal).Count());
	}

	[Fact]
	public void CheatEngineToolNames_Backend_IsExactlyThePlanCatalog()
	{
		Assert.Equal(PlanCatalogNames.Select(static row => row.Data).Order(StringComparer.Ordinal),
			CheatEngineToolNames.Backend.Order(StringComparer.Ordinal));
	}

	[Theory]
	[InlineData("memory_read_value")]
	[InlineData("code_dissect_range")]
	[InlineData("symbol_get_loading_state")]
	[InlineData("lua_check")]
	public void ValidateTool_WellFormedNameOutsideTheFrozenCatalog_Fails(string name)
	{
		Assert.Contains(McpContractRules.ValidateTool(Sample(name), false),
			static failure => failure.Contains("frozen v2 catalog", StringComparison.Ordinal));
	}

	[Fact]
	public void ValidateTool_GatewayNameServedByABackend_Fails()
	{
		Assert.Contains(McpContractRules.ValidateTool(Sample("instance_list"), false),
			static failure => failure.Contains("backend name", StringComparison.Ordinal));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("""{}""")]
	[InlineData("""{"cheatengine/dispatchClass":"slow"}""")]
	[InlineData("""{"cheatengine/dispatchClass":1}""")]
	[InlineData("""{"cheatengine/dispatchClass":["short"]}""")]
	public void ValidateTool_BackendToolWithoutAReviewedDispatchClass_Fails(string? meta)
	{
		Tool tool = Sample("memory_read");
		tool.Meta = meta is null ? null : JsonNode.Parse(meta)!.AsObject();

		Assert.Contains(McpContractRules.ValidateTool(tool, false),
			static failure => failure.Contains(McpDispatchClass.MetaKey, StringComparison.Ordinal));
	}

	[Theory]
	[InlineData(McpDispatchClass.Short)]
	[InlineData(McpDispatchClass.HostScan)]
	[InlineData(McpDispatchClass.BlockingNative)]
	[InlineData(McpDispatchClass.MayPrompt)]
	public void ValidateTool_ReviewedDispatchClass_Passes(string dispatchClass)
	{
		Tool tool = Sample("memory_read");
		tool.Meta = new JsonObject { [McpDispatchClass.MetaKey] = dispatchClass };

		Assert.Empty(McpContractRules.ValidateTool(tool, false));
	}

	[Fact]
	public void ValidateTool_GatewayTool_NeedsNoDispatchClass()
	{
		Tool tool = Sample("instance_list");
		tool.Meta = null;

		Assert.Empty(McpContractRules.ValidateTool(tool, true));
	}

	[Theory]
	[MemberData(nameof(PlanCatalogNames))]
	public void ValidateTool_PlanCatalogName_PassesTheNameRules(string name)
	{
		bool polls = name.Split('_')[1] == "poll";
		Assert.Empty(McpContractRules.ValidateTool(Sample(name, idempotent: true, readOnly: polls), false));
	}

	[Fact]
	public void ValidateTool_GatewayInstanceList_PassesOnlyAsAGatewayTool()
	{
		Assert.Empty(McpContractRules.ValidateTool(Sample("instance_list"), true));
		Assert.NotEmpty(McpContractRules.ValidateTool(Sample("instance_list"), false));
		Assert.NotEmpty(McpContractRules.ValidateTool(Sample("memory_read"), true));
	}

	[Theory]
	[InlineData("memory")]
	[InlineData("Memory_read")]
	[InlineData("memory__read")]
	[InlineData("memory_read_")]
	[InlineData("memory-read")]
	[InlineData("memory_read_a_very_long_object_name_beyond")]
	[InlineData("game_read_value")]
	[InlineData("memory_peek")]
	public void ValidateTool_BadName_Fails(string name)
	{
		Assert.NotEmpty(McpContractRules.ValidateTool(Sample(name), false));
	}

	[Theory]
	[InlineData("")]
	[InlineData("Read Memory Values")]
	[InlineData("read memory")]
	[InlineData("Read memory values from the target process at the given address now")]
	public void ValidateTool_BadTitle_Fails(string title)
	{
		Assert.Contains(McpContractRules.ValidateTool(Sample("memory_read", title), false),
			static failure => failure.Contains("title", StringComparison.Ordinal));
	}

	[Theory]
	[InlineData("Read memory")]
	[InlineData("Find an AOB signature")]
	[InlineData("List .NET types")]
	[InlineData("Initialize DBVM")]
	[InlineData("Apply an Auto Assembler script")]
	[InlineData("List Mono classes")]
	public void IsSentenceCase_ReviewedTitle_Passes(string title)
	{
		Assert.True(McpContractRules.IsSentenceCase(title));
	}

	[Fact]
	public void ValidateTool_MissingAnnotation_Fails()
	{
		Tool tool = Sample("memory_read");
		tool.Annotations!.OpenWorldHint = null;

		Assert.Contains(McpContractRules.ValidateTool(tool, false),
			static failure => failure.Contains("explicitly", StringComparison.Ordinal));
	}

	[Fact]
	public void ValidateTool_ReadOnlyAndDestructive_Fails()
	{
		Assert.Contains(McpContractRules.ValidateTool(Sample("memory_read", destructive: true), false),
			static failure => failure.Contains("read-only and destructive", StringComparison.Ordinal));
	}

	[Fact]
	public void ValidateTool_PollNotReadOnlyOrNotIdempotent_Fails()
	{
		Assert.NotEmpty(McpContractRules.ValidateTool(Sample("code_poll_job", readOnly: false), false));
		Assert.NotEmpty(McpContractRules.ValidateTool(Sample("code_poll_job", idempotent: false), false));
	}

	[Theory]
	[InlineData("runtime_stop_job")]
	[InlineData("asm_release_patch")]
	public void ValidateTool_StopOrReleaseNotIdempotent_Fails(string name)
	{
		Assert.NotEmpty(McpContractRules.ValidateTool(Sample(name, readOnly: false, idempotent: false), false));
		Assert.Empty(McpContractRules.ValidateTool(Sample(name, readOnly: false, idempotent: true), false));
	}

	[Theory]
	[InlineData("""{"type":"array","items":{"type":"string"}}""")]
	[InlineData("""{"type":["object","null"]}""")]
	public void ValidateTool_NonObjectOutputSchema_Fails(string outputSchema)
	{
		Assert.Contains(McpContractRules.ValidateTool(Sample("memory_read", outputSchema: outputSchema), false),
			static failure => failure.Contains("output schema", StringComparison.Ordinal));
	}

	[Theory]
	[InlineData("""{"type":"object","properties":{"address":{"type":"string"}}}""", "no description")]
	[InlineData("""{"type":"object","properties":{"address":{"description":"Address."}}}""", "empty schema")]
	[InlineData("""{"type":"object","properties":{"address":true}}""", "empty schema")]
	[InlineData("""{"type":"object","properties":{},"required":["address"]}""", "not a declared parameter")]
	[InlineData("""{"type":"object","properties":{"instanceId":{"description":"Id.","type":"string"}}}""",
		"instanceId")]
	public void ValidateTool_BadInputSchema_Fails(string inputSchema, string expected)
	{
		Assert.Contains(McpContractRules.ValidateTool(Sample("memory_read", inputSchema: inputSchema), false),
			failure => failure.Contains(expected, StringComparison.Ordinal));
	}

	[Fact]
	public void ValidateTools_RepeatedTitle_Fails()
	{
		Assert.Contains(
			McpContractRules.ValidateTools([Sample("memory_read"), Sample("memory_write", readOnly: false)], false),
			static failure => failure.Contains("repeats the title", StringComparison.Ordinal));
	}

	[Fact]
	public void ValidateTool_LegacyTool_OnlyChecksTheRoutingArgument()
	{
		Tool legacy = new()
		{
			Name = "LegacyName",
			InputSchema = Parse("""{"type":"object","properties":{"x":{"type":"integer"}}}""")
		};
		Tool routed = new()
		{
			Name = "legacy_routed",
			InputSchema = Parse("""{"type":"object","properties":{"InstanceID":{"type":"string"}}}""")
		};

		Assert.Empty(McpContractRules.ValidateTool(legacy, false));
		Assert.Single(McpContractRules.ValidateTool(routed, false));
	}

	[Fact]
	public void BackendCatalog_LegacyTools_PassTheValidator()
	{
		Assert.NotEmpty(McpPrimitiveCatalog.Create(TestComposition.BackendManifest).Tools);
		Assert.Empty(McpContractRules.ValidateTools(McpPrimitiveCatalog.Create(TestComposition.BackendManifest).Tools,
			false));
	}

	[Fact]
	public void Catalog_V2ProbeTools_PassTheValidator()
	{
		McpPrimitiveCatalog catalog = McpPrimitiveCatalog.Create(TestMcpPipeline.ProbeManifest);

		Assert.Contains(catalog.Tools, static tool => tool.Name == ContractProbeTool.ConvertName);
		Assert.Empty(McpContractRules.ValidateTools(catalog.Tools, false));
	}

	[Fact]
	public void Catalog_V2ToolBreakingARule_FailsStartup()
	{
		CheatEngineMcpPrimitiveOptions manifest = CheatEngineMcpComposition.CreateManifest(CheatEngineMcpMode.Catalog,
			static builder => builder.AddToolType<RuleBreakingTool>());

		OptionsValidationException exception =
			Assert.Throws<OptionsValidationException>(() => McpPrimitiveCatalog.Create(manifest));

		Assert.Contains("memory_peek", exception.Message, StringComparison.Ordinal);
		Assert.Contains("explicitly", exception.Message, StringComparison.Ordinal);
	}

	private static Tool Sample(string name, string title = "Read memory", bool readOnly = true,
		bool destructive = false, bool idempotent = true, string outputSchema = ObjectSchema,
		string inputSchema = InputSchema)
	{
		return new Tool
		{
			Name = name,
			Title = title,
			Description = "A sample tool.",
			InputSchema = Parse(inputSchema),
			OutputSchema = Parse(outputSchema),
			Meta = new JsonObject { [McpDispatchClass.MetaKey] = McpDispatchClass.Short },
			Annotations = new ToolAnnotations
			{
				Title = title,
				ReadOnlyHint = readOnly,
				DestructiveHint = destructive,
				IdempotentHint = idempotent,
				OpenWorldHint = false
			}
		};
	}

	private static JsonElement Parse(string json)
	{
		using JsonDocument document = JsonDocument.Parse(json);
		return document.RootElement.Clone();
	}

	[McpServerToolType]
	public sealed class RuleBreakingTool
	{
		[McpServerTool(Name = "memory_peek", Title = "Peek Memory", UseStructuredContent = true)]
		[Description("Breaks the verb, title and annotation rules.")]
		public static ContractProbeResult Peek([Description("A value.")] string value)
		{
			return new ContractProbeResult(value, [], false, 0, null);
		}
	}
}
