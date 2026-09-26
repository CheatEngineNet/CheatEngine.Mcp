using CheatEngine.Client.Tables;
using CheatEngine.Mcp.Tools;
using CheatEngine.SDK.Engine.AddressList;
using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Mcp.Tests;

public sealed class TableToolTests
{
	[Fact]
	public void GetAddressList_OverMaximum_RefusesBeforeTableSnapshot()
	{
		AddressListTool tool = new(ClientTestDouble.Client());

		object result = tool.GetAddressList(4097);

		ToolResultAssert.IsFailure(result, "maximumRecords must be between 1 and 4096.");
	}

	[Fact]
	public void GetAddressList_BoundedSnapshot_MapsClientRecordFields()
	{
		MemoryRecordSnapshot record = Snapshot(17, 3, "Health", "game.exe+20", "99", true);
		int requestedMaximum = 0;
		ITableClient tables = ClientTestDouble.Create<ITableClient>((method, arguments) =>
		{
			if (method.Name == nameof(ITableClient.GetSnapshot))
			{
				requestedMaximum = ((MemoryRecordCollectionRequest) arguments![0]!).MaximumItems;
				return new AddressTableSnapshot([record]);
			}

			throw new Xunit.Sdk.XunitException($"Unexpected table method: {method.Name}");
		});
		AddressListTool tool = new(ClientTestDouble.Client(("Tables", tables)));

		object result = tool.GetAddressList(12);

		ToolResultAssert.IsSuccess(result);
		ToolResultAssert.HasPropertyValue(result, "count", 1);
		Assert.Equal(12, requestedMaximum);
		object[] records = ToolResultAssert.GetProperty<object[]>(result, "records");
		Assert.Single(records);
		object mapped = records[0];
		ToolResultAssert.HasPropertyValue(mapped, "id", 17);
		ToolResultAssert.HasPropertyValue(mapped, "address", "game.exe+20");
		ToolResultAssert.HasPropertyValue(mapped, "active", true);
	}

	[Fact]
	public void UpdateMemoryRecord_ChangesRequestedFieldsThroughClient()
	{
		bool updateCalled = false;
		MemoryRecordSnapshot updatedRecord = Snapshot(9, 0, "Ammo", "game.exe+30", "20", false);
		ITableClient tables = ClientTestDouble.Create<ITableClient>((method, arguments) =>
		{
			if (method.Name != nameof(ITableClient.Update))
			{
				throw new Xunit.Sdk.XunitException($"Unexpected table method: {method.Name}");
			}

			MemoryRecordId id = (MemoryRecordId) arguments![0]!;
			MemoryRecordUpdate update = (MemoryRecordUpdate) arguments[1]!;
			Assert.Equal(9, id.Value);
			Assert.Equal("Ammo", update.Description);
			Assert.Equal("20", update.Value);
			updateCalled = true;
			return updatedRecord;
		});
		AddressListTool tool = new(ClientTestDouble.Client(("Tables", tables)));

		object result = tool.UpdateMemoryRecord(9, description: "Ammo", value: "20");

		ToolResultAssert.IsSuccess(result);
		Assert.True(updateCalled);
		object mapped = ToolResultAssert.GetProperty<object>(result, "record");
		ToolResultAssert.HasPropertyValue(mapped, "active", false);
	}

	[Fact]
	public void SetMemoryRecordActive_ClientIssuedId_ChangesOnlyActivation()
	{
		bool active = false;
		ITableClient tables = ClientTestDouble.Create<ITableClient>((method, arguments) =>
		{
			if (method.Name == nameof(ITableClient.SetActive))
			{
				Assert.Equal(9, ((MemoryRecordId) arguments![0]!).Value);
				Assert.Equal(true, arguments[1]);
				active = true;
				return Snapshot(9, 0, "Ammo", "game.exe+30", "20", true);
			}

			throw new Xunit.Sdk.XunitException($"Unexpected table method: {method.Name}");
		});
		AddressListTool tool = new(ClientTestDouble.Client(("Tables", tables)));

		object result = tool.SetMemoryRecordActive(9, true);

		ToolResultAssert.IsSuccess(result);
		Assert.True(active);
	}

	[Fact]
	public void DeleteMemoryRecord_ClientIssuedId_DeletesThatRecord()
	{
		int deletedId = 0;
		ITableClient tables = ClientTestDouble.Create<ITableClient>((method, arguments) =>
		{
			if (method.Name == nameof(ITableClient.Delete))
			{
				deletedId = ((MemoryRecordId) arguments![0]!).Value;
				return null;
			}

			throw new Xunit.Sdk.XunitException($"Unexpected table method: {method.Name}");
		});
		AddressListTool tool = new(ClientTestDouble.Client(("Tables", tables)));

		object result = tool.DeleteMemoryRecord(77);

		ToolResultAssert.IsSuccess(result);
		Assert.Equal(77, deletedId);
	}

	private static MemoryRecordSnapshot Snapshot(int id, int index, string description, string address, string value, bool active)
	{
		return new MemoryRecordSnapshot(
			new MemoryRecordId(id),
			index,
			new MemoryRecordContentSnapshot(description, address, value, VariableType.Dword),
			new MemoryRecordStateSnapshot(new Address(0x401000), active));
	}
}
