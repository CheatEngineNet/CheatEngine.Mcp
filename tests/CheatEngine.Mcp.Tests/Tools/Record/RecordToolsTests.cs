using System.Reflection;

using CheatEngine.Client;
using CheatEngine.Client.Lua;
using CheatEngine.Client.Tables;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Record;
using CheatEngine.SDK.Engine.AddressList;
using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Values;

using Microsoft.Extensions.Options;

namespace CheatEngine.Mcp.Tests.Tools.Record;

/// <summary>Contract-level behavior of the typed and fixed-Lua record tools.</summary>
public sealed class RecordToolsTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public void List_PagesTopLevelRecordsThroughTheTypedTableClient()
	{
		RecordTarget target = new();
		target.Records.AddRange([Snapshot(1, 0, "Health"), Snapshot(2, 1, "Ammo"), Snapshot(3, 2, "Gold")]);

		RecordPage page = new RecordReadTools(target.Dispatch).List(1, 1, Token);

		Assert.Equal(3, page.Total);
		Assert.Equal("Ammo", Assert.Single(page.Records).Description);
		Assert.Equal(2, page.NextOffset);
		Assert.Equal(["Tables.GetRecordCount", "Tables.GetRecordAt"], target.Calls);
	}

	[Fact]
	public void Create_ValueRecord_UsesTypedDefinitionAndReturnsCopiedRecord()
	{
		RecordTarget target = new();
		MemoryRecordDefinition? definition = null;
		target.Create = value =>
		{
			definition = value;
			return Snapshot(17, 0, value.Description, value.AddressExpression, value.Value, value.VariableType);
		};

		RecordCreateResult result = new RecordMutationTools(target.Dispatch).Create(
			[new RecordCreateSpec("Health", "game.exe+20", "100")], Token);

		Assert.Equal("Health", definition!.Value.Description);
		Assert.Equal("game.exe+20", definition.Value.AddressExpression);
		Assert.Equal(17, Assert.Single(result.Records).Id);
		Assert.Equal(["Tables.Create"], target.Calls);
	}

	[Fact]
	public void Update_PointerOffsets_UsesFixedLuaAndReadsBackTheRecord()
	{
		RecordTarget target = new();
		target.Records.Add(Snapshot(7, 0, "Pointer", variableType: VariableType.Pointer));

		RecordUpdateResult result = new RecordMutationTools(target.Dispatch).Update(
			[new RecordUpdateSpec(7, Offsets: [0x10, 0x20])], Token);

		Assert.Equal(7, Assert.Single(result.Records).Id);
		string source = Assert.Single(target.LuaSources);
		Assert.EndsWith(RecordLuaScripts.SetOffsets, source, StringComparison.Ordinal);
	}

	[Fact]
	public void SetActive_AutoAssemblerRecord_RequiresItsFeatureBeforeMutation()
	{
		RecordTarget target = new(new McpFeatureOptions { EnableAutoAssembler = false });
		target.Records.Add(Snapshot(9, 0, "Patch", variableType: VariableType.AutoAssembler));
		RecordMutationTools tools = new(target.Dispatch);

		CheatEngineToolException exception =
			Assert.Throws<CheatEngineToolException>(() => tools.SetActive([9], true, Token));

		Assert.Equal(ToolErrorKind.CapabilityDisabled, exception.Error.Kind);
		Assert.Equal(["Tables.GetRecord"], target.Calls);
	}

	[Fact]
	public void Delete_UsesTypedDeleteAfterItCopiesEveryRequestedRecord()
	{
		RecordTarget target = new();
		target.Records.Add(Snapshot(4, 0, "Health"));
		target.Records.Add(Snapshot(8, 1, "Ammo"));

		RecordDeleteResult result = new RecordMutationTools(target.Dispatch).Delete([4, 8], Token);

		Assert.Equal(2, result.Deleted);
		Assert.Equal([4, 8], target.Deleted);
		Assert.Equal(["Tables.GetRecord", "Tables.GetRecord", "Tables.Delete", "Tables.Delete"], target.Calls);
	}

	[Fact]
	public void SetScript_AutoAssemblerRecord_UsesFixedLuaAndReadsBack()
	{
		RecordTarget target = new(new McpFeatureOptions { EnableAutoAssembler = true });
		target.Records.Add(Snapshot(12, 0, "Patch", variableType: VariableType.AutoAssembler));

		RecordScriptResult result = new RecordMutationTools(target.Dispatch).SetScript(12, "[ENABLE]\nnop", Token);

		Assert.Equal(12, result.Record.Id);
		string source = Assert.Single(target.LuaSources);
		Assert.EndsWith(RecordLuaScripts.SetScript, source, StringComparison.Ordinal);
		Assert.Equal(["Tables.GetRecord", "Lua.Execute", "Tables.GetRecord"], target.Calls);
	}

	[Fact]
	public void Create_LaterFailure_RollsBackPriorCreatedRecordsInReverseOrder()
	{
		RecordTarget target = new();
		int creates = 0;
		target.Create = definition =>
		{
			creates++;
			if (creates == 3)
			{
				throw new InvalidOperationException("simulated host refusal");
			}

			return Snapshot(creates, creates - 1, definition.Description, definition.AddressExpression,
				definition.Value,
				definition.VariableType);
		};

		Assert.Throws<CheatEngineToolException>(() => new RecordMutationTools(target.Dispatch).Create(
		[
			new RecordCreateSpec("One", "1000", "1"), new RecordCreateSpec("Two", "2000", "2"),
			new RecordCreateSpec("Three", "3000", "3")
		], Token));

		Assert.Equal([2, 1], target.Deleted);
	}

	[Theory]
	[InlineData(-1)]
	[InlineData(1001)]
	public void List_InvalidPage_RefusesBeforeAnyDispatch(int limit)
	{
		RecordTarget target = new();

		Assert.Throws<CheatEngineToolException>(() => new RecordReadTools(target.Dispatch).List(0, limit, Token));

		Assert.Empty(target.Calls);
		Assert.Equal(0, target.Dispatcher.Calls);
	}

	[Fact]
	public void Update_TooManyOffsets_RefusesBeforeAnyDispatch()
	{
		RecordTarget target = new();
		long[] offsets = new long[RecordArguments.MaximumOffsets + 1];

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new RecordMutationTools(target.Dispatch).Update([new RecordUpdateSpec(1, Offsets: offsets)], Token));

		Assert.Equal(ToolErrorKind.LimitExceeded, exception.Error.Kind);
		Assert.Empty(target.Calls);
	}

	private static MemoryRecordSnapshot Snapshot(int id, int index, string description, string address = "game.exe+10",
		string value = "0", VariableType variableType = VariableType.Dword)
	{
		return new MemoryRecordSnapshot(new MemoryRecordId(id), index,
			new MemoryRecordContentSnapshot(description, address, value, variableType),
			new MemoryRecordStateSnapshot(new Address(0x401000)));
	}

	private sealed class RecordTarget
	{
		internal RecordTarget(McpFeatureOptions? features = null)
		{
			ITableClient tables = ClientTestDouble.Create<ITableClient>(Tables);
			ILuaClient lua = ClientTestDouble.Create<ILuaClient>(Lua);
			Client = ClientTestDouble.Client(Dispatcher.Dispatcher, CancellationToken.None,
				(nameof(ICheatEngineClient.Tables), tables), (nameof(ICheatEngineClient.Lua), lua));
			IOptions<McpExecutionOptions> execution = Options.Create(new McpExecutionOptions());
			Dispatch = new ToolDispatch(Client, new McpFeatureGate(Options.Create(features ?? new McpFeatureOptions())),
				execution, new DispatchStatistics(execution), TimeProvider.System, new RecordingLogger<ToolDispatch>(),
				new PluginFixedLuaExecutor(Client));
		}

		internal RecordingDispatcher Dispatcher
		{
			get;
		} = new();

		internal ICheatEngineClient Client
		{
			get;
		}

		internal ToolDispatch Dispatch
		{
			get;
		}

		internal List<MemoryRecordSnapshot> Records
		{
			get;
		} = [];

		internal List<string> Calls
		{
			get;
		} = [];

		internal List<int> Deleted
		{
			get;
		} = [];

		internal List<string> LuaSources
		{
			get;
		} = [];

		internal Func<MemoryRecordDefinition, MemoryRecordSnapshot>? Create
		{
			get;
			set;
		}

		private object? Tables(MethodInfo method, object?[]? arguments)
		{
			object?[] values = arguments ?? throw new ArgumentNullException(nameof(arguments));
			Calls.Add("Tables." + method.Name);
			return method.Name switch
			{
				nameof(ITableClient.GetRecordCount) => Records.Count,
				nameof(ITableClient.GetRecordAt) => Records[(int) values[0]!],
				nameof(ITableClient.GetRecord) => Get(new MemoryRecordId(((MemoryRecordId) values[0]!).Value)),
				nameof(ITableClient.Create) => (Create ?? DefaultCreate)((MemoryRecordDefinition) values[0]!),
				nameof(ITableClient.Update) => Update((MemoryRecordId) values[0]!, (MemoryRecordUpdate) values[1]!),
				nameof(ITableClient.SetActive) => Activate((MemoryRecordId) values[0]!, (bool) values[1]!),
				nameof(ITableClient.Delete) => Delete((MemoryRecordId) values[0]!),
				_ => throw new NotSupportedException($"Unexpected table call {method.Name}.")
			};
		}

		private object? Lua(MethodInfo method, object?[]? arguments)
		{
			object?[] values = arguments ?? throw new ArgumentNullException(nameof(arguments));
			Calls.Add("Lua." + method.Name);
			string source = (string) values[0]!.GetType().GetProperty("Source")!.GetValue(values[0])!;
			LuaSources.Add(source);
			Type resultType = method.GetGenericArguments()[1];
			return Activator.CreateInstance(resultType, new RecordLuaChanged(7), null, 0);
		}

		private MemoryRecordSnapshot DefaultCreate(MemoryRecordDefinition definition)
		{
			int id = Records.Count + 1;
			MemoryRecordSnapshot snapshot = Snapshot(id, Records.Count, definition.Description,
				definition.AddressExpression,
				definition.Value, definition.VariableType);
			Records.Add(snapshot);
			return snapshot;
		}

		private MemoryRecordSnapshot Get(MemoryRecordId id)
		{
			return Records.Single(record => record.Id == id);
		}

		private MemoryRecordSnapshot Update(MemoryRecordId id, MemoryRecordUpdate update)
		{
			MemoryRecordSnapshot old = Get(id);
			MemoryRecordSnapshot changed = Snapshot(id.Value, old.Index, update.Description ?? old.Content.Description,
				update.AddressExpression ?? old.Content.AddressExpression, update.Value ?? old.Content.Value,
				update.VariableType ?? old.Content.VariableType);
			Records[Records.FindIndex(record => record.Id == id)] = changed;
			return changed;
		}

		private MemoryRecordSnapshot Activate(MemoryRecordId id, bool active)
		{
			MemoryRecordSnapshot old = Get(id);
			MemoryRecordSnapshot changed = new(old.Id, old.Index, old.Content,
				new MemoryRecordStateSnapshot(old.State.CurrentAddress, active, old.State.ChildCount));
			Records[Records.FindIndex(record => record.Id == id)] = changed;
			return changed;
		}

		private object? Delete(MemoryRecordId id)
		{
			Deleted.Add(id.Value);
			Records.RemoveAll(record => record.Id == id);
			return null;
		}
	}
}
