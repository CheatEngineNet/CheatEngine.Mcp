using System.Globalization;
using System.Runtime.Versioning;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Reversible live qualification for the address-list, structure, and owned-symbol state APIs.</summary>
[SupportedOSPlatform("windows")]
internal static class LiveCatalogStateQualification
{
	private const string Allocation = "live-catalog-state-allocation";
	private static readonly int[] FirstElement = [0];

	internal static async Task VerifyAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient instance)
	{
		ArgumentNullException.ThrowIfNull(sandbox);
		ArgumentNullException.ThrowIfNull(instance);

		string suffix = sandbox.HostA.TargetProcessId.ToString(CultureInfo.InvariantCulture);
		string recordDescription = "McpLiveCatalogRecord_" + suffix;
		string structure = "McpLiveCatalogStructure_" + suffix;
		string renamedStructure = structure + "_Renamed";
		string symbol = "McpLiveCatalogSymbol_" + suffix;
		bool allocated = false;
		int? recordId = null;
		bool structureCreated = false;
		bool symbolRegistered = false;
		Exception? failure = null;
		List<Exception> cleanup = [];

		try
		{
			JsonNode allocation = await CallAsync(instance, CheatEngineToolNames.MemoryAllocate,
				new Dictionary<string, object?> { ["name"] = Allocation, ["size"] = 16 });
			allocated = true;
			string address = Text(allocation, "address");
			Assert.Equal(16L, allocation["size"]!.GetValue<long>());
			Receipt(sandbox, CheatEngineToolNames.MemoryAllocate, allocation.DeepClone());

			JsonNode created = await CallAsync(instance, CheatEngineToolNames.RecordCreate,
				new Dictionary<string, object?>
				{
					["records"] = new object?[]
					{
						new Dictionary<string, object?>
						{
							["description"] = recordDescription,
							["address"] = address,
							["value"] = "17",
							["variableType"] = 2
						}
					}
				});
			JsonNode createdRecord = Assert.Single(created["records"]!.AsArray())!;
			recordId = createdRecord["id"]!.GetValue<int>();
			Assert.Equal((recordDescription, address, "17"),
				(Text(createdRecord, "description"), Text(createdRecord, "address"), Text(createdRecord, "value")));
			Receipt(sandbox, CheatEngineToolNames.RecordCreate, created.DeepClone());

			JsonNode listed = await CallAsync(instance, CheatEngineToolNames.RecordList,
				new Dictionary<string, object?> { ["offset"] = 0, ["limit"] = 1000 });
			Assert.Contains(listed["records"]!.AsArray(), item => item!["id"]!.GetValue<int>() == recordId.Value);
			Receipt(sandbox, CheatEngineToolNames.RecordList, new
			{
				id = recordId,
				listed = true
			});

			JsonNode read = await CallAsync(instance, CheatEngineToolNames.RecordGet,
				new Dictionary<string, object?> { ["ids"] = new[] { recordId.Value } });
			JsonNode getRecord = Assert.Single(read["records"]!.AsArray())!;
			Assert.Equal((recordDescription, address, "17"),
				(Text(getRecord, "description"), Text(getRecord, "address"), Text(getRecord, "value")));
			Receipt(sandbox, CheatEngineToolNames.RecordGet, read.DeepClone());

			JsonNode found = await CallAsync(instance, CheatEngineToolNames.RecordFind,
				new Dictionary<string, object?> { ["descriptionContains"] = recordDescription, ["limit"] = 16 });
			Assert.Equal(1, found["total"]!.GetValue<int>());
			Assert.Equal(recordId.Value, Assert.Single(found["records"]!.AsArray())!["id"]!.GetValue<int>());
			Receipt(sandbox, CheatEngineToolNames.RecordFind, found.DeepClone());

			JsonNode updated = await CallAsync(instance, CheatEngineToolNames.RecordUpdate,
				new Dictionary<string, object?>
				{
					["updates"] = new object?[]
					{
						new Dictionary<string, object?> { ["id"] = recordId.Value, ["description"] = recordDescription + "_updated", ["value"] = "23" }
					}
				});
			JsonNode updatedRecord = Assert.Single(updated["records"]!.AsArray())!;
			Assert.Equal((recordDescription + "_updated", "23"),
				(Text(updatedRecord, "description"), Text(updatedRecord, "value")));
			recordDescription += "_updated";
			Receipt(sandbox, CheatEngineToolNames.RecordUpdate, updated.DeepClone());

			foreach (bool active in new[] { true, false })
			{
				JsonNode activation = await CallAsync(instance, CheatEngineToolNames.RecordSetActive,
					new Dictionary<string, object?> { ["ids"] = new[] { recordId.Value }, ["active"] = active });
				JsonNode activationRecord = Assert.Single(activation["records"]!.AsArray())!;
				Assert.Equal(recordId.Value, activationRecord["id"]!.GetValue<int>());
				Assert.Equal(active, activationRecord["requestedActive"]!.GetValue<bool>());
				Assert.Equal(active, activationRecord["active"]!.GetValue<bool>());
				Assert.False(activationRecord["pending"]!.GetValue<bool>());
				JsonNode observed = await CallAsync(instance, CheatEngineToolNames.RecordGet,
					new Dictionary<string, object?> { ["ids"] = new[] { recordId.Value } });
				Assert.Equal(active, Assert.Single(observed["records"]!.AsArray())!["active"]!.GetValue<bool>());
			}
			Receipt(sandbox, CheatEngineToolNames.RecordSetActive, new
			{
				activated = true,
				deactivated = true,
				observedByRecordGet = true
			});

			JsonNode createdStructure = await CallAsync(instance, CheatEngineToolNames.StructureCreate,
				new Dictionary<string, object?> { ["name"] = structure, ["elements"] = Array.Empty<object?>() });
			structureCreated = true;
			Assert.Equal((structure, 0), (Text(createdStructure, "name"), createdStructure["elementCount"]!.GetValue<int>()));
			Receipt(sandbox, CheatEngineToolNames.StructureCreate, createdStructure.DeepClone());

			JsonNode structureList = await CallAsync(instance, CheatEngineToolNames.StructureList,
				new Dictionary<string, object?> { ["nameContains"] = structure, ["limit"] = 16 });
			Assert.Contains(structureList["structures"]!.AsArray(), item => Text(item, "name") == structure);
			Receipt(sandbox, CheatEngineToolNames.StructureList, new
			{
				name = structure,
				listed = true
			});

			JsonNode definition = await CallAsync(instance, CheatEngineToolNames.StructureGet,
				new Dictionary<string, object?> { ["name"] = structure, ["limit"] = 16 });
			Assert.Equal((structure, 0), (Text(definition, "name"), definition["elementCount"]!.GetValue<int>()));
			Receipt(sandbox, CheatEngineToolNames.StructureGet, definition.DeepClone());

			JsonNode added = await CallAsync(instance, CheatEngineToolNames.StructureAddElements,
				new Dictionary<string, object?>
				{
					["name"] = structure,
					["elements"] = new object?[] { new Dictionary<string, object?> { ["offset"] = "0", ["name"] = "counter", ["valueType"] = "int32" } }
				});
			Assert.Equal((structure, 1), (Text(added, "name"), added["elementCount"]!.GetValue<int>()));
			Assert.Equal(0, Assert.Single(added["indices"]!.AsArray())!.GetValue<int>());
			Receipt(sandbox, CheatEngineToolNames.StructureAddElements, added.DeepClone());

			JsonNode changed = await CallAsync(instance, CheatEngineToolNames.StructureUpdateElements,
				new Dictionary<string, object?>
				{
					["name"] = structure,
					["updates"] = new object?[] { new Dictionary<string, object?> { ["index"] = 0, ["name"] = "counterUpdated" } }
				});
			Assert.Equal((structure, 1), (Text(changed, "name"), changed["elementCount"]!.GetValue<int>()));
			Receipt(sandbox, CheatEngineToolNames.StructureUpdateElements, changed.DeepClone());

			JsonNode afterUpdate = await CallAsync(instance, CheatEngineToolNames.StructureGet,
				new Dictionary<string, object?> { ["name"] = structure, ["limit"] = 16 });
			JsonNode element = Assert.Single(afterUpdate["elements"]!.AsArray())!;
			Assert.Equal(("0", "counterUpdated", "int32"),
				(Text(element, "offset"), Text(element, "name"), Text(element, "valueType")));

			JsonNode removed = await CallAsync(instance, CheatEngineToolNames.StructureRemoveElements,
				new Dictionary<string, object?> { ["name"] = structure, ["indices"] = FirstElement });
			Assert.Equal((structure, 0), (Text(removed, "name"), removed["elementCount"]!.GetValue<int>()));
			JsonNode afterRemove = await CallAsync(instance, CheatEngineToolNames.StructureGet,
				new Dictionary<string, object?> { ["name"] = structure, ["limit"] = 16 });
			Assert.Equal(0, afterRemove["elementCount"]!.GetValue<int>());
			Assert.Empty(afterRemove["elements"]!.AsArray());
			Receipt(sandbox, CheatEngineToolNames.StructureRemoveElements, removed.DeepClone());

			JsonNode renamed = await CallAsync(instance, CheatEngineToolNames.StructureSetName,
				new Dictionary<string, object?> { ["name"] = structure, ["newName"] = renamedStructure });
			structure = renamedStructure;
			Assert.Equal(renamedStructure, Text(renamed, "name"));
			Receipt(sandbox, CheatEngineToolNames.StructureSetName, renamed.DeepClone());

			JsonNode registered = await CallAsync(instance, CheatEngineToolNames.SymbolRegister,
				new Dictionary<string, object?> { ["name"] = symbol, ["address"] = address, ["doNotSave"] = true });
			symbolRegistered = true;
			Assert.Equal((symbol, address, true), (Text(registered, "name"), Text(registered, "address"), registered["doNotSave"]!.GetValue<bool>()));
			Receipt(sandbox, CheatEngineToolNames.SymbolRegister, registered.DeepClone());

			JsonNode registeredList = await CallAsync(instance, CheatEngineToolNames.SymbolListRegistered,
				new Dictionary<string, object?> { ["nameContains"] = symbol, ["limit"] = 16 });
			JsonNode listSymbol = Assert.Single(registeredList["symbols"]!.AsArray())!;
			Assert.Equal(symbol, Text(listSymbol, "name"));
			Assert.True(listSymbol["ownedByMcp"]!.GetValue<bool>());
			Assert.Equal(address, Text(listSymbol, "address"));
			Receipt(sandbox, CheatEngineToolNames.SymbolListRegistered, registeredList.DeepClone());

			JsonNode symbolFind = await CallAsync(instance, CheatEngineToolNames.SymbolFind,
				new Dictionary<string, object?> { ["nameContains"] = symbol, ["limit"] = 16 });
			Assert.Contains(symbolFind["symbols"]!.AsArray(), item => Text(item, "name").Equals(symbol, StringComparison.OrdinalIgnoreCase) && Text(item, "address") == address);
			Receipt(sandbox, CheatEngineToolNames.SymbolFind, symbolFind.DeepClone());

			JsonNode resolved = await CallAsync(instance, CheatEngineToolNames.SymbolResolve,
				new Dictionary<string, object?> { ["expressions"] = new[] { symbol }, ["shallow"] = false });
			JsonNode resolvedItem = Assert.Single(resolved["items"]!.AsArray())!;
			Assert.Null(resolvedItem["error"]);
			Assert.Equal(address, Text(resolvedItem, "address"));
			Receipt(sandbox, CheatEngineToolNames.SymbolResolve, resolved.DeepClone());
		}
		catch (Exception exception)
		{
			failure = exception;
			throw;
		}
		finally
		{
			if (recordId is not null)
			{
				await CleanupAsync(cleanup, async () =>
				{
					JsonNode deleted = await CallAsync(instance, CheatEngineToolNames.RecordDelete,
						new Dictionary<string, object?> { ["ids"] = new[] { recordId.Value } });
					Assert.Equal(1, deleted["deleted"]!.GetValue<int>());
					JsonNode list = await CallAsync(instance, CheatEngineToolNames.RecordList,
						new Dictionary<string, object?> { ["offset"] = 0, ["limit"] = 1000 });
					Assert.DoesNotContain(list["records"]!.AsArray(), item => item!["id"]!.GetValue<int>() == recordId.Value);
					Receipt(sandbox, CheatEngineToolNames.RecordDelete, new
					{
						id = recordId,
						deleted = true,
						absentFromList = true
					});
				});
			}

			if (structureCreated)
			{
				await CleanupAsync(cleanup, async () =>
				{
					JsonNode deleted = await CallAsync(instance, CheatEngineToolNames.StructureDelete,
						new Dictionary<string, object?> { ["name"] = structure });
					Assert.Equal(structure, Text(deleted, "name"));
					JsonNode list = await CallAsync(instance, CheatEngineToolNames.StructureList,
						new Dictionary<string, object?> { ["nameContains"] = structure, ["limit"] = 16 });
					Assert.DoesNotContain(list["structures"]!.AsArray(), item => Text(item, "name") == structure);
					Receipt(sandbox, CheatEngineToolNames.StructureDelete, new
					{
						name = structure,
						deleted = true,
						absentFromList = true
					});
				});
			}

			if (symbolRegistered)
			{
				await CleanupAsync(cleanup, async () =>
				{
					JsonNode unregistered = await CallAsync(instance, CheatEngineToolNames.SymbolUnregister,
						new Dictionary<string, object?> { ["name"] = symbol });
					Assert.Equal(symbol, Text(unregistered, "name"));
					JsonNode list = await CallAsync(instance, CheatEngineToolNames.SymbolListRegistered,
						new Dictionary<string, object?> { ["nameContains"] = symbol, ["limit"] = 16 });
					Assert.Empty(list["symbols"]!.AsArray());
					Receipt(sandbox, CheatEngineToolNames.SymbolUnregister, new
					{
						name = symbol,
						unregistered = true,
						absentFromList = true
					});
				});
			}

			if (allocated)
			{
				await CleanupAsync(cleanup, async () =>
				{
					JsonNode freed = await CallAsync(instance, CheatEngineToolNames.MemoryFree,
						new Dictionary<string, object?> { ["name"] = Allocation });
					sandbox.Record("live_catalog_state_memory_free", freed.DeepClone());
				});
			}

			sandbox.Record("live_catalog_state_cleanup", new
			{
				recordDeleteAttempted = recordId is not null,
				structureDeleteAttempted = structureCreated,
				symbolUnregisterAttempted = symbolRegistered,
				allocationFreeAttempted = allocated,
				failures = cleanup.Select(exception => exception.ToString()).ToArray()
			});
			ThrowCleanup(failure, cleanup);
		}
	}

	private static async Task<JsonNode> CallAsync(LiveMcpInstanceClient instance, string tool,
		IReadOnlyDictionary<string, object?>? arguments = null) =>
		await instance.CallToolAsync(tool, arguments) ?? throw new InvalidDataException($"Tool '{tool}' returned no payload.");

	private static void Receipt(LiveSandboxSession sandbox, string tool, object oracle) =>
		sandbox.Record("all_tools_" + tool, oracle);

	private static string Text(JsonNode? node, string property) =>
		node?[property]?.GetValue<string>() ?? throw new InvalidDataException($"Missing string '{property}'.");

	private static async Task CleanupAsync(List<Exception> failures, Func<Task> action)
	{
		try
		{
			await action();
		}
		catch (Exception exception)
		{
			failures.Add(exception);
		}
	}

	private static void ThrowCleanup(Exception? failure, List<Exception> cleanup)
	{
		if (cleanup.Count == 0)
		{
			return;
		}

		if (failure is null)
		{
			throw new AggregateException("Live catalog-state cleanup failed.", cleanup);
		}

		throw new AggregateException("Live catalog-state qualification failed and cleanup also failed.", [failure, .. cleanup]);
	}
}
