using System.ComponentModel;
using System.Reflection;

using CheatEngine.Client;
using CheatEngine.Client.Lua;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Tools;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tests;

public sealed class LuaToolRuntimeTests
{
	[Fact]
	public void BuildSource_HostileTextAndNilArguments_EncodesDataAndPreservesPositions()
	{
		const string hostile = "quote\" slash\\ newline\nUnicode: \u2603 \U0001F98A";

		string source = LuaToolRuntime.BuildSource("return a", [hostile, null, "tail"]);

		Assert.StartsWith("local a = { n = 3, [1] = \"", source);
		Assert.Contains("[2] = nil", source, StringComparison.Ordinal);
		Assert.Contains("[3] = \"tail\"", source, StringComparison.Ordinal);
		Assert.Contains("\\034", source, StringComparison.Ordinal);
		Assert.Contains("\\092", source, StringComparison.Ordinal);
		Assert.Contains("\\010", source, StringComparison.Ordinal);
		Assert.DoesNotContain(hostile, source, StringComparison.Ordinal);
		Assert.DoesNotContain("\u2603", source, StringComparison.Ordinal);
		Assert.EndsWith(" };\nreturn a", source, StringComparison.Ordinal);
	}

	[Fact]
	public void BuildSource_UnsignedPointerAndNestedValues_UsesLuaLiteralForms()
	{
		string source = LuaToolRuntime.BuildSource("return a", [ulong.MaxValue, new object?[] { true, 7L, null }]);

		Assert.Contains("[1] = 0xFFFFFFFFFFFFFFFF", source, StringComparison.Ordinal);
		Assert.Contains("[2] = {true,7,nil,}", source, StringComparison.Ordinal);
	}

	[Fact]
	public void BuildSource_UnsupportedOrOversizedValue_ThrowsBeforeLuaDispatch()
	{
		Assert.Throws<ArgumentException>(() => LuaToolRuntime.BuildSource("return a", [double.PositiveInfinity]));
		Assert.Throws<ArgumentException>(() => LuaToolRuntime.BuildSource("return a", [new object()]));
		Assert.Throws<ArgumentException>(() => LuaToolRuntime.BuildSource("return a", [new string('x', (LuaToolRuntime.MaximumStringBytes / 4) + 1)]));
	}

	[Fact]
	public void Invoke_ClientReturnsValue_DispatchesTypedOperationWithCopiedSource()
	{
		LuaToolRuntime.LuaToolOperation? captured = null;
		ILuaClient lua = ClientTestDouble.Create<ILuaClient>((method, arguments) =>
		{
			Assert.Equal(nameof(ILuaClient.Execute), method.Name);
			captured = Assert.IsAssignableFrom<LuaToolRuntime.LuaToolOperation>(arguments![0]);
			return new Dictionary<string, object?> { ["value"] = 42L };
		});
		ICheatEngineClient client = ClientTestDouble.Client((nameof(ICheatEngineClient.Lua), lua));

		object result = LuaToolRuntime.Invoke(client, "test_operation", "return { value = a[1] }", 42L);

		ToolResultAssert.IsSuccess(result);
		Dictionary<string, object?> payload = Assert.IsAssignableFrom<Dictionary<string, object?>>(ToolResultAssert.GetProperty<object>(result, "result"));
		Assert.Equal(42L, payload["value"]);
		Assert.NotNull(captured);
		Assert.Equal("test_operation", captured.Operation);
		Assert.Contains("[1] = 42", captured.Source, StringComparison.Ordinal);
		Assert.EndsWith("return { value = a[1] }", captured.Source, StringComparison.Ordinal);
	}

	[Fact]
	public void Invoke_ClientFailure_ReturnsClassifiedFailure()
	{
		CheatEngineFailure failure = new(CheatEngineFailureKind.LuaError, "Lua.Execute", "Lua rejected the chunk", hostEffect: CheatEngineHostEffect.Started);
		ILuaClient lua = ClientTestDouble.Create<ILuaClient>((_, _) => throw failure.ToException());
		ICheatEngineClient client = ClientTestDouble.Client((nameof(ICheatEngineClient.Lua), lua));

		object result = LuaToolRuntime.Invoke(client, "failing_operation", "return a", 1);

		ToolResultAssert.IsFailure(result, "Lua rejected the chunk");
		ToolResultAssert.HasPropertyValue(result, "kind", CheatEngineFailureKind.LuaError.ToString());
		ToolResultAssert.HasPropertyValue(result, "operation", "Lua.Execute");
		ToolResultAssert.HasPropertyValue(result, "hostEffect", CheatEngineHostEffect.Started.ToString());
	}

	[Fact]
	public void Call_InvalidFunctionName_ReturnsFailureWithoutLuaDispatch()
	{
		int calls = 0;
		ILuaClient lua = ClientTestDouble.Create<ILuaClient>((_, _) =>
		{
			calls++;
			return null;
		});
		ICheatEngineClient client = ClientTestDouble.Client((nameof(ICheatEngineClient.Lua), lua));

		object result = LuaToolRuntime.Call(client, "getStructure(); os.execute('x')");

		ToolResultAssert.IsFailure(result, "Invalid Lua function name.");
		Assert.Equal(0, calls);
	}

	[Fact]
	public void LuaFallbackTools_PublicContracts_HaveUniqueNamesAndDescriptions()
	{
		MethodInfo[] methods = typeof(LuaToolRuntime).Assembly
			.GetTypes()
			.Where(type => type.Namespace == typeof(LuaToolRuntime).Namespace && type.Name.StartsWith("Lua", StringComparison.Ordinal) &&
				type.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
			.SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
			.Where(method => method.GetCustomAttribute<McpServerToolAttribute>() is not null)
			.ToArray();

		Assert.NotEmpty(methods);
		string?[] names = methods.Select(method => method.GetCustomAttribute<McpServerToolAttribute>()!.Name).ToArray();
		Assert.False(names.Any(string.IsNullOrWhiteSpace), "Lua fallback tool names must be present.");
		Assert.True(names.Length == names.Select(name => name!).Distinct(StringComparer.Ordinal).Count(), "Lua fallback tool names must be unique.");
		foreach (MethodInfo method in methods)
		{
			System.ComponentModel.DescriptionAttribute? methodDescription = method.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>();
			if (methodDescription is null)
			{
				throw new Xunit.Sdk.XunitException($"{method.DeclaringType!.Name}.{method.Name} needs a tool description.");
			}
			Assert.False(string.IsNullOrWhiteSpace(methodDescription.Description), $"{method.DeclaringType!.Name}.{method.Name} needs a non-empty tool description.");
			foreach (ParameterInfo parameter in method.GetParameters())
			{
				System.ComponentModel.DescriptionAttribute? description = parameter.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>();
				if (description is null)
				{
					throw new Xunit.Sdk.XunitException($"{method.DeclaringType!.Name}.{method.Name} parameter {parameter.Name} needs a description.");
				}
				Assert.False(string.IsNullOrWhiteSpace(description.Description), $"{method.DeclaringType!.Name}.{method.Name} parameter {parameter.Name} needs a non-empty description.");
			}
		}
	}
}
