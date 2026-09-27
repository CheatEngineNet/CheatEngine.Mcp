using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

using CheatEngine.Mcp.Tests.Support;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Interop.Api;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.Mcp.Tests.NativeLua;

public sealed unsafe partial class NativeLuaToolRuntimeTests
{
	[Fact]
	public void LuaJsonWriter_SequencesPackResultsAndEmptyTables_BecomeArrays()
	{
		using RuntimeScope scope = CreateScope();

		Assert.Equal("[1,\"two\",true,[]]", CopyJson("return {1, 'two', true, {}}").Json);
		Assert.Equal("[]", CopyJson("return {}").Json);
		Assert.Equal("[]", CopyJson("return table.pack()").Json);
		Assert.Equal("[null]", CopyJson("return table.pack(nil)").Json);
		Assert.Equal("[1,null,3,null]", CopyJson("return table.pack(1, nil, 3, nil)").Json);
		Assert.Equal("[null,null]", CopyJson("return {n = 2}").Json);
		Assert.Equal("[[1,2],[],[null,\"x\"]]",
			CopyJson("return {{1, 2}, {}, table.pack(nil, 'x')}").Json);
		Assert.Equal("[5,6,7]", CopyJson("local t = {}; t[3] = 7; t[1] = 5; t[2] = 6; return t").Json);
	}

	[Fact]
	public void LuaJsonWriter_StringKeyedTables_BecomeNestedObjectsAndSharedTablesAreCopied()
	{
		using RuntimeScope scope = CreateScope();

		AssertJson("""{"name":"root","n":1,"child":{"items":["a","b"],"deep":{"value":false}}}""",
			CopyJson("return {name = 'root', n = 1, child = {items = {'a', 'b'}, deep = {value = false}}}").Json);
		AssertJson("""{"left":{"v":1},"right":{"v":1}}""",
			CopyJson("local shared = {v = 1}; return {left = shared, right = shared}").Json);
		Assert.Equal("\"text\"", CopyJson("return 'text'").Json);
		Assert.Equal("null", CopyJson("return nil").Json);
	}

	[Fact]
	public void LuaJsonWriter_IntegerAndFloatSubtypes_KeepAllSixtyFourBits()
	{
		using RuntimeScope scope = CreateScope();
		const string source = """
		                      return {math.maxinteger, math.mininteger, 0x7FFFFFFFFFFFFFFF, -42, a[1], 0xFFFFFFFFFFFFFFFF,
		                      	3.0, 0.5, 7 // 2, 7 / 2, 2^63, 18446744073709551615,
		                      	math.type(3.0), math.type(7 // 2), math.type(18446744073709551615), math.type(a[1])}
		                      """;

		using JsonDocument document = JsonDocument.Parse(
			CopyJson(LuaToolRuntime.BuildSource(source, [0xFFFF800000001000UL])).Json);
		JsonElement[] values = [.. document.RootElement.EnumerateArray()];

		Assert.Equal(long.MaxValue, values[0].GetInt64());
		Assert.Equal(long.MinValue, values[1].GetInt64());
		Assert.Equal(long.MaxValue, values[2].GetInt64());
		Assert.Equal(-42, values[3].GetInt64());
		Assert.Equal(unchecked((long) 0xFFFF800000001000UL), values[4].GetInt64());
		Assert.Equal(-1, values[5].GetInt64());
		Assert.Equal("3", values[6].GetRawText());
		Assert.Equal(0.5, values[7].GetDouble());
		Assert.Equal("3", values[8].GetRawText());
		Assert.Equal(3.5, values[9].GetDouble());
		Assert.Equal(9.2233720368547758E+18, values[10].GetDouble());
		Assert.False(values[10].TryGetInt64(out _));
		// CE's lua53-64.dll wraps an overflowing decimal integer literal like a hexadecimal one, where the Lua 5.3
		// manual reads it as a float. Addresses must still reach Lua as hexadecimal literals or a[] integers.
		Assert.Equal(-1, values[11].GetInt64());
		string[] subtypes = ["float", "integer", "integer", "integer"];
		Assert.Equal(subtypes, values[12..].Select(value => value.GetString()));
	}

	[Fact]
	public void LuaJsonWriter_HexFormatOfNegativeIntegers_PrintsTheUnsignedAddress()
	{
		using RuntimeScope scope = CreateScope();
		const string source = """
		                      return {string.format('%X', a[1]), string.format('%X', -1), string.format('%016X', 0x1000),
		                      	string.format('%X', math.mininteger), string.format('%x', a[2])}
		                      """;

		string json = CopyJson(LuaToolRuntime.BuildSource(source, [0xFFFF800000001000UL, 0x8000000000000001UL])).Json;

		Assert.Equal(
			"""["FFFF800000001000","FFFFFFFFFFFFFFFF","0000000000001000","8000000000000000","8000000000000001"]""",
			json);
	}

	[Fact]
	public void LuaJsonWriter_NonFiniteNumbers_AreContractViolations()
	{
		using RuntimeScope scope = CreateScope();

		foreach (string source in new[]
				 {
					 "return 0/0", "return 1/0", "return {-1/0}", "return {deep = {value = 0/0}}"
				 })
		{
			LuaJsonException exception = Assert.Throws<LuaJsonException>(() => CopyJson(source));
			Assert.Equal(LuaJsonViolation.Contract, exception.Violation);
			Assert.Contains("non-finite", exception.Message, StringComparison.Ordinal);
		}

		// The script-side conversion that the future mcp.num prelude helper performs.
		Assert.Equal("[\"NaN\",\"Infinity\",1.5]",
			CopyJson("""
			         local function num(v) if v ~= v then return 'NaN' elseif v == math.huge then return 'Infinity' end return v end
			         return {num(0/0), num(1/0), num(1.5)}
			         """).Json);
	}

	[Fact]
	public void LuaJsonWriter_InvalidTableShapes_AreContractViolations()
	{
		using RuntimeScope scope = CreateScope();
		string[] sources =
		[
			"return {1, 2, x = 3}",
			"return {1, nil, 3}",
			"return {[2] = 'b'}",
			"return {[0] = 'zero', 'one'}",
			"return {[1.5] = 'x'}",
			"return {[true] = 'x'}",
			"return {[{}] = 'x'}",
			"return {n = 1, [1] = 'a', [2] = 'b'}",
			"return {n = 2, x = 'mixed', [1] = 'a'}",
			"return {['\\255'] = 1}",
			"local t = {}; t.self = t; return t",
			"local a, b = {}, {}; a.b = b; b.list = {a}; return a",
			"return {f = print}",
			"return {coroutine.create(function() end)}",
			"return io.stdout"
		];
		int top = LuaApi.lua_gettop(s_state);

		foreach (string source in sources)
		{
			LuaJsonException exception = Assert.Throws<LuaJsonException>(() => CopyJson(source));
			Assert.True(exception.Violation == LuaJsonViolation.Contract, source);
			Assert.Equal(top, LuaApi.lua_gettop(s_state));
		}
	}

	[Fact]
	public void LuaJsonWriter_Bounds_AreLimitViolationsAtTheirExactEdge()
	{
		using RuntimeScope scope = CreateScope();
		const string sequence = "local t = {}; for i = 1, a[1] do t[i] = i end; return t";
		const string nested =
			"local root = {}; local t = root; for i = 2, a[1] do t.child = {}; t = t.child end; t.leaf = true; return root";
		const string strings = "return {string.rep('x', a[1]), string.rep('y', a[2])}";

		// The table itself is one of the 65,536 values.
		string largest = CopyJson(LuaToolRuntime.BuildSource(sequence, [LuaJsonWriter.MaximumItems - 1])).Json;
		using JsonDocument document = JsonDocument.Parse(largest);
		Assert.Equal(LuaJsonWriter.MaximumItems - 1, document.RootElement.GetArrayLength());
		AssertLimit(LuaToolRuntime.BuildSource(sequence, [LuaJsonWriter.MaximumItems]), "values");
		Assert.Equal("[" + string.Join(',', Enumerable.Repeat("null", LuaJsonWriter.MaximumItems - 1)) + "]",
			CopyJson("return {n = 65535}").Json);
		AssertLimit("return {n = 65536}", "values");
		Assert.Equal(LuaJsonWriter.MaximumDepth,
			CopyJson(LuaToolRuntime.BuildSource(nested, [LuaJsonWriter.MaximumDepth])).Json.Count(c => c == '{'));
		AssertLimit(LuaToolRuntime.BuildSource(nested, [LuaJsonWriter.MaximumDepth + 1]), "nests");
		const int half = LuaJsonWriter.MaximumStringBytes / 2;
		Assert.Equal(LuaJsonWriter.MaximumStringBytes + 7,
			CopyJson(LuaToolRuntime.BuildSource(strings, [half, half])).Json.Length);
		AssertLimit(LuaToolRuntime.BuildSource(strings, [half, half + 1]), "strings");
		AssertLimit("return {[string.rep('k', 4 * 1024 * 1024)] = 1, x = 2}", "strings");
		// Two MiB of control characters stay within the string bound but escape to 12 MiB of JSON.
		AssertLimit("return string.rep('\\1', 2 * 1024 * 1024)", "JSON");
	}

	[Fact]
	public void LuaJsonWriter_InvalidUtf8String_IsReplacedWhileValidTextIsKept()
	{
		using RuntimeScope scope = CreateScope();

		string json = CopyJson("return {'\\255\\254ok', '\\xC3\\xA9\\u{1F98A}', 'nul\\0byte'}").Json;

		string[] values = LuaJsonWriter.Deserialize(Encoding.UTF8.GetBytes(json), TestJsonContext.Default.StringArray);
		string[] expected = ["\uFFFD\uFFFDok", "\u00E9\U0001F98A", "nul\0byte"];
		Assert.Equal(expected, values);
	}

	[Fact]
	public void LuaJsonWriter_OpaqueValues_AreRejectedOrDroppedAndCounted()
	{
		using RuntimeScope scope = CreateScope();
		const string source =
			"return {keep = 1, f = print, list = {print, 2, coroutine.create(function() end), io.stdout}}";

		Assert.Equal(LuaJsonViolation.Contract,
			Assert.Throws<LuaJsonException>(() => CopyJson(source)).Violation);
		JsonCopy dropped = CopyJson(source, LuaOpaqueValueHandling.Drop);
		AssertJson("""{"keep":1,"list":[null,2,null,null]}""", dropped.Json);
		Assert.Equal(4, dropped.DroppedOpaqueCount);
		JsonCopy topLevel = CopyJson("return print", LuaOpaqueValueHandling.Drop);
		Assert.Equal("null", topLevel.Json);
		Assert.Equal(1, topLevel.DroppedOpaqueCount);
	}

	[Fact]
	public void LuaJsonWriter_Metamethods_NeverRunDuringTheRawWalk()
	{
		using RuntimeScope scope = CreateScope();
		const string source = """
		                      touched = 0
		                      local function touch() touched = touched + 1 end
		                      local meta = {__index = function() touch(); return 'x' end, __len = function() touch(); return 9 end,
		                      	__pairs = function() touch(); return next, {}, nil end, __tostring = function() touch(); return 's' end,
		                      	__eq = function() touch(); return true end}
		                      return {list = setmetatable({1, 2}, meta), proxy = setmetatable({}, meta),
		                      	named = setmetatable({v = 1}, meta)}
		                      """;

		AssertJson("""{"list":[1,2],"proxy":[],"named":{"v":1}}""", CopyJson(source).Json);
		Assert.Equal(0L, ReadGlobal("touched"));
	}

	[Fact]
	public void LuaJsonWriter_DeclaredMcpError_IsReturnedWithoutCopyingTheResult()
	{
		using RuntimeScope scope = CreateScope();

		JsonCopy declared = CopyJson("""
		                             return {mcp_error = {kind = 'not_found', message = 'gone', hostEffect = 'not_started',
		                             	hint = 'list jobs'}, ignored = print, huge = 0/0}
		                             """);

		Assert.Equal(new LuaScriptError("not_found", "gone", "not_started", "list jobs"), declared.Error);
		Assert.Equal(string.Empty, declared.Json);
		Assert.Equal(new LuaScriptError("busy", "wait", null, null),
			CopyJson("return {mcp_error = {kind = 'busy', message = 'wait'}}").Error);
		LuaScriptError truncated =
			CopyJson("return {mcp_error = {kind = 'internal', message = string.rep('\\xC3\\xA9', 5000)}}")
				.Error!;
		Assert.Equal(new string('\u00E9', LuaJsonWriter.MaximumErrorFieldBytes / 2), truncated.Message);
		AssertJson("""{"data":{"mcp_error":{"kind":"nested"}}}""",
			CopyJson("return {data = {mcp_error = {kind = 'nested'}}}").Json);

		string[] malformedErrors =
		[
			"return {mcp_error = 'text'}", "return {mcp_error = {message = 'no kind'}}",
			"return {mcp_error = {kind = 'Not Found', message = 'm'}}",
			"return {mcp_error = {kind = 'k', message = ''}}",
			"return {mcp_error = {kind = 'k', message = 'm', hostEffect = 7}}"
		];
		foreach (string malformed in malformedErrors)
		{
			Assert.Equal(LuaJsonViolation.Contract,
				Assert.Throws<LuaJsonException>(() => CopyJson(malformed)).Violation);
		}
	}

	[Fact]
	public void LuaJsonWriter_Read_DeserializesIntoASourceGeneratedRecord()
	{
		using RuntimeScope scope = CreateScope();
		const string source = """
		                      return {maximum = math.maxinteger, minimum = math.mininteger, address = a[1],
		                      	addressHex = string.format('%X', a[1]), ratio = 0.25, flag = true, tags = {'x', 'y'},
		                      	child = {name = 'c', packed = table.pack(1, nil, 3)}, empty = {}}
		                      """;

		LuaJsonResult<LuaJsonProbe> result = ReadJson(LuaToolRuntime.BuildSource(source, [0xFFFF800000001000UL]),
			TestJsonContext.Default.LuaJsonProbe);

		Assert.False(result.IsError);
		LuaJsonProbe probe = result.Value;
		Assert.Equal(long.MaxValue, probe.Maximum);
		Assert.Equal(long.MinValue, probe.Minimum);
		Assert.Equal(0xFFFF800000001000UL, unchecked((ulong) probe.Address));
		Assert.Equal("FFFF800000001000", probe.AddressHex);
		Assert.Equal(0.25, probe.Ratio);
		Assert.True(probe.Flag);
		string[] tags = ["x", "y"];
		Assert.Equal(tags, probe.Tags);
		Assert.Equal("c", probe.Child.Name);
		Assert.Equal(new long?[] { 1, null, 3 }, probe.Child.Packed);
		Assert.Empty(probe.Empty);

		LuaJsonResult<LuaJsonProbe> declared = ReadJson("return {mcp_error = {kind = 'invalid_state', message = 'm'}}",
			TestJsonContext.Default.LuaJsonProbe);
		Assert.True(declared.IsError);
		Assert.Equal("invalid_state", declared.Error.Kind);
		Assert.Equal(LuaJsonViolation.Contract, Assert.Throws<LuaJsonException>(() =>
			ReadJson("return {maximum = 'text'}", TestJsonContext.Default.LuaJsonProbe)).Violation);
		Assert.Equal(LuaJsonViolation.Contract, Assert.Throws<LuaJsonException>(() =>
			ReadJson("return nil", TestJsonContext.Default.LuaJsonProbe)).Violation);
	}

	private static JsonCopy CopyJson(string source,
		LuaOpaqueValueHandling opaque = LuaOpaqueValueHandling.Reject)
	{
		using LuaRuntimeOperation operation = AcquireOperation();
		LuaState state = operation.State;
		using LuaFrame frame = new(state);
		ExecuteForResult(state, source);
		ArrayBufferWriter<byte> buffer = new();
		LuaScriptError? error = LuaJsonWriter.Write(state, -1, buffer, opaque, out int dropped);
		return new JsonCopy(Encoding.UTF8.GetString(buffer.WrittenSpan), error, dropped);
	}

	private static LuaJsonResult<T> ReadJson<T>(string source, JsonTypeInfo<T> typeInfo,
		LuaOpaqueValueHandling opaque = LuaOpaqueValueHandling.Reject)
	{
		using LuaRuntimeOperation operation = AcquireOperation();
		LuaState state = operation.State;
		using LuaFrame frame = new(state);
		ExecuteForResult(state, source);
		return LuaJsonWriter.Read(state, -1, typeInfo, new LuaJsonBufferPool(), opaque);
	}

	private static LuaRuntimeOperation AcquireOperation()
	{
		LuaAdmissionStatus admission = LuaRuntime.TryAcquireOperationWithOutcome(out LuaRuntimeOperation operation);
		Assert.Equal(LuaAdmissionStatus.Admitted, admission);
		return operation;
	}

	private static void ExecuteForResult(LuaState state, string source)
	{
		Assert.True(state.TryEnsureStack(64));
		LuaStatus status = state.TryExecute(Encoding.UTF8.GetBytes(source), 1, "=CheatEngine.Mcp/native_probe"u8);
		if (!status.IsOk)
		{
			Assert.Fail(LuaError.FromStack(state, status).Message);
		}
	}

	private static void AssertLimit(string source, string subject)
	{
		int top = LuaApi.lua_gettop(s_state);
		LuaJsonException exception = Assert.Throws<LuaJsonException>(() => CopyJson(source));
		Assert.Equal(LuaJsonViolation.Limit, exception.Violation);
		Assert.Contains(subject, exception.Message, StringComparison.Ordinal);
		Assert.Equal(top, LuaApi.lua_gettop(s_state));
	}

	private static void AssertJson(string expected, string actual)
	{
		using JsonDocument expectedDocument = JsonDocument.Parse(expected);
		using JsonDocument actualDocument = JsonDocument.Parse(actual);
		Assert.True(JsonElement.DeepEquals(expectedDocument.RootElement, actualDocument.RootElement),
			$"Expected {expected} but Lua produced {actual}.");
	}

	private sealed record JsonCopy(string Json, LuaScriptError? Error, int DroppedOpaqueCount);
}
