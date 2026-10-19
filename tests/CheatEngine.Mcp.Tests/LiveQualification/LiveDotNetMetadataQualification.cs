using System.Globalization;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.Versioning;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Optional live qualification of the read-only Cheat Engine .NET collector against owned metadata.</summary>
[SupportedOSPlatform("windows")]
internal static class LiveDotNetMetadataQualification
{
	private const int PageSize = 100;
	private const int MaximumPages = 4;

	/// <summary>
	///     Qualifies collector metadata calls when the selected owned target exposes a compatible .NET collector. The
	///     caller supplies the published managed assembly because the target image can be an application host.
	/// </summary>
	internal static async Task<DotNetMetadataQualificationResult> VerifyAsync(LiveSandboxSession sandbox,
		LiveMcpInstanceClient instance, string managedAssemblyPath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(managedAssemblyPath);
		AssemblyExpectation expected = ReadExpectation(managedAssemblyPath);
		JsonNode status = await CallAsync(instance, CheatEngineToolNames.DotNetGetStatus);
		bool available = status["available"]!.GetValue<bool>();
		bool attached = status["attached"]!.GetValue<bool>();
		int domainCount = status["domainCount"]!.GetValue<int>();
		if (!available || !attached)
		{
			string reason = !available
				? "Cheat Engine did not expose its .NET collector."
				: "The .NET collector did not attach to the owned target.";
			sandbox.Record("dotnet_metadata_unavailable", new
			{
				available,
				attached,
				domainCount,
				reason,
				status = status.DeepClone()
			});
			return new DotNetMetadataQualificationResult(false, available, attached, domainCount, reason);
		}

		Receipt(sandbox, CheatEngineToolNames.DotNetGetStatus, status);
		JsonNode domain = await FindDomainWithModuleAsync(instance, expected.FileName);
		Receipt(sandbox, CheatEngineToolNames.DotNetListDomains, domain["page"]!);
		JsonNode module = domain["module"]!;
		Receipt(sandbox, CheatEngineToolNames.DotNetListModules, domain["modules"]!);
		string moduleHandle = Text(module, "handle");

		JsonNode types = await CallAsync(instance, CheatEngineToolNames.DotNetListTypes,
			new Dictionary<string, object?>
			{
				["moduleHandle"] = moduleHandle,
				["offset"] = 0,
				["limit"] = PageSize,
				["nameContains"] = expected.TypeName
			});
		JsonNode type = Assert.Single(types["types"]!.AsArray(), item =>
			string.Equals(Text(item!, "name"), expected.TypeName, StringComparison.Ordinal))!;
		Assert.Equal(expected.TypeToken, Text(type, "token"));
		Receipt(sandbox, CheatEngineToolNames.DotNetListTypes, types);
		string typeToken = Text(type, "token");

		JsonNode typeDetails = await CallAsync(instance, CheatEngineToolNames.DotNetGetType,
			new Dictionary<string, object?>
			{
				["moduleHandle"] = moduleHandle,
				["typeToken"] = typeToken,
				["maximumFields"] = 100
			});
		Assert.Equal((moduleHandle, typeToken, expected.TypeName),
			(Text(typeDetails, "moduleHandle"), Text(typeDetails, "typeToken"), Text(typeDetails, "name")));
		JsonNode pointerOffsets = Assert.Single(typeDetails["fields"]!.AsArray(), field =>
			string.Equals(Text(field!, "name"), expected.FieldName, StringComparison.Ordinal))!;
		Assert.True(pointerOffsets["isStatic"]!.GetValue<bool>());
		Receipt(sandbox, CheatEngineToolNames.DotNetGetType, typeDetails);

		JsonNode methods = await CallAsync(instance, CheatEngineToolNames.DotNetListMethods,
			new Dictionary<string, object?>
			{
				["moduleHandle"] = moduleHandle,
				["typeToken"] = typeToken,
				["offset"] = 0,
				["limit"] = PageSize,
				["nameContains"] = expected.MethodName
			});
		JsonNode method = Assert.Single(methods["methods"]!.AsArray(), item =>
			string.Equals(Text(item!, "name"), expected.MethodName, StringComparison.Ordinal))!;
		Assert.Equal(expected.MethodToken, Text(method, "token"));
		Assert.True((method["attributes"]!.GetValue<long>() & 0x10) != 0);
		Receipt(sandbox, CheatEngineToolNames.DotNetListMethods, methods);

		JsonNode parameters = await CallAsync(instance, CheatEngineToolNames.DotNetGetMethodParameters,
			new Dictionary<string, object?> { ["moduleHandle"] = moduleHandle, ["methodToken"] = expected.MethodToken });
		Assert.Equal((moduleHandle, expected.MethodToken),
			(Text(parameters, "moduleHandle"), Text(parameters, "methodToken")));
		string[] reportedParameterNames =
			[.. parameters["parameters"]!.AsArray().Select(parameter => Text(parameter!, "name"))];
		Assert.All(expected.ParameterNames, expectedName => Assert.Contains(expectedName, reportedParameterNames));
		Receipt(sandbox, CheatEngineToolNames.DotNetGetMethodParameters, parameters);
		return new DotNetMetadataQualificationResult(true, true, true, domainCount);
	}

	private static async Task<JsonNode> FindDomainWithModuleAsync(LiveMcpInstanceClient instance, string fileName)
	{
		int offset = 0;
		for (int attempt = 0; attempt < MaximumPages; attempt++)
		{
			JsonNode page = await CallAsync(instance, CheatEngineToolNames.DotNetListDomains,
				new Dictionary<string, object?> { ["offset"] = offset, ["limit"] = PageSize });
			foreach (JsonNode? domain in page["domains"]!.AsArray())
			{
				JsonNode modules = await FindModuleAsync(instance, Text(domain!, "handle"), fileName);
				if (modules["module"] is { } module)
				{
					return new JsonObject
					{
						["page"] = page.DeepClone(),
						["modules"] = modules["page"]!.DeepClone(),
						["module"] = module.DeepClone()
					};
				}
			}

			if (page["nextOffset"] is not { } next)
			{
				break;
			}

			offset = next.GetValue<int>();
		}

		throw new InvalidOperationException($"The collector did not expose owned managed module '{fileName}' within {MaximumPages} domain pages.");
	}

	private static async Task<JsonNode> FindModuleAsync(LiveMcpInstanceClient instance, string domainHandle, string fileName)
	{
		int offset = 0;
		for (int attempt = 0; attempt < MaximumPages; attempt++)
		{
			JsonNode page = await CallAsync(instance, CheatEngineToolNames.DotNetListModules,
				new Dictionary<string, object?> { ["domainHandle"] = domainHandle, ["offset"] = offset, ["limit"] = PageSize });
			JsonNode? module = page["modules"]!.AsArray().SingleOrDefault(item => item is not null &&
				string.Equals(Path.GetFileName(Text(item, "name")), fileName, StringComparison.OrdinalIgnoreCase));
			if (module is not null)
			{
				return new JsonObject { ["page"] = page.DeepClone(), ["module"] = module.DeepClone() };
			}

			if (page["nextOffset"] is not { } next)
			{
				break;
			}

			offset = next.GetValue<int>();
		}

		return new JsonObject();
	}

	private static AssemblyExpectation ReadExpectation(string path)
	{
		using FileStream stream = File.OpenRead(path);
		using PEReader image = new(stream);
		MetadataReader metadata = image.GetMetadataReader();
		TypeDefinitionHandle typeHandle = metadata.TypeDefinitions.Single(handle =>
		{
			TypeDefinition type = metadata.GetTypeDefinition(handle);
			return metadata.GetString(type.Namespace) == string.Empty && metadata.GetString(type.Name) == "Program";
		});
		TypeDefinition program = metadata.GetTypeDefinition(typeHandle);
		FieldDefinitionHandle fieldHandle = program.GetFields().Single(handle =>
			metadata.GetString(metadata.GetFieldDefinition(handle).Name) == "PointerOffsets");
		FieldDefinition field = metadata.GetFieldDefinition(fieldHandle);
		Assert.True((field.Attributes & System.Reflection.FieldAttributes.Static) != 0);
		MethodDefinitionHandle methodHandle = program.GetMethods().Single(handle =>
			metadata.GetString(metadata.GetMethodDefinition(handle).Name) == "Main");
		MethodDefinition method = metadata.GetMethodDefinition(methodHandle);
		Assert.True((method.Attributes & System.Reflection.MethodAttributes.Static) != 0);
		string[] parameters = method.GetParameters().Select(metadata.GetParameter).Where(parameter =>
			parameter.SequenceNumber != 0).Select(parameter => metadata.GetString(parameter.Name)).ToArray();
		return new AssemblyExpectation(Path.GetFileName(path), "Program", Token(typeHandle), "PointerOffsets", "Main",
			Token(methodHandle), parameters);
	}

	private static string Token(EntityHandle handle) => MetadataTokens.GetToken(handle).ToString(CultureInfo.InvariantCulture);
	private static void Receipt(LiveSandboxSession sandbox, string tool, JsonNode result) => sandbox.Record("all_tools_" + tool, result.DeepClone());
	private static string Text(JsonNode node, string property) => node[property]!.GetValue<string>();
	private static async Task<JsonNode> CallAsync(LiveMcpInstanceClient instance, string tool, IReadOnlyDictionary<string, object?>? arguments = null) => await instance.CallToolAsync(tool, arguments) ?? throw new InvalidDataException($"Tool '{tool}' returned no payload.");

	private sealed record AssemblyExpectation(string FileName, string TypeName, string TypeToken, string FieldName,
		string MethodName, string MethodToken, string[] ParameterNames);
}

/// <summary>Whether the optional collector ran, plus the availability evidence recorded when it did not.</summary>
internal sealed record DotNetMetadataQualificationResult(bool Performed, bool Available, bool Attached, int DomainCount,
	string? Reason = null);
