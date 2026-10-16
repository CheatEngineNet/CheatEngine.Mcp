using System.Reflection;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.DotNet;
using CheatEngine.Mcp.Tools.Mono;

namespace CheatEngine.Mcp.Tests.Tools;

/// <summary>Positive managed-adapter checks for the .NET and Mono collector surfaces.</summary>
public sealed class ManagedAdapterQualificationTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public void DotNetDiscoveryAdapters_MapStatusDomainsAndModulesWithRequestedPage()
	{
		StateTestHarness harness = new();
		harness.Answer<DotNetStatus>(static _ => new DotNetStatus(true, true, 2));
		harness.Answer<DotNetDomainPage>(static _ => new DotNetDomainPage([new DotNetDomain("7", "game")], 2, 1));
		harness.Answer<DotNetModulePage>(static _ => new DotNetModulePage("7",
			[new DotNetModule("11", "Game.dll")], 1));
		DotNetTools tools = DotNet(harness);

		DotNetStatus status = tools.GetStatus(Token);
		DotNetDomainPage domains = tools.ListDomains(0, 1, Token);
		DotNetModulePage modules = tools.ListModules(" 7 ", 0, 1, Token);

		Assert.Equal((true, true, 2), (status.Available, status.Attached, status.DomainCount));
		Assert.Equal(("7", "game", 2, 1), (domains.Domains[0].Handle, domains.Domains[0].Name, domains.Total,
			domains.NextOffset));
		Assert.Equal(("7", "11", "Game.dll", 1), (modules.DomainHandle, modules.Modules[0].Handle,
			modules.Modules[0].Name, modules.Total));
		AssertOperations(harness,
			(CheatEngineToolNames.DotNetGetStatus, null),
			(CheatEngineToolNames.DotNetListDomains, "[1] = 0, [2] = 1"),
			(CheatEngineToolNames.DotNetListModules, "[1] = \"7\", [2] = 0, [3] = 1"));
	}

	[Fact]
	public void DotNetTypeAdapters_MapPagesDetailsAndMethodsWithFilters()
	{
		StateTestHarness harness = new();
		harness.Answer<DotNetTypePage>(static _ => new DotNetTypePage("11",
			[new DotNetType("33554433", "Game.Player", 1, "33554432")], 3, 2));
		harness.Answer<DotNetTypeDetails>(static _ => new DotNetTypeDetails("11", "33554433", "Game.Player",
			"System.Object", 18, null, null, null, null, [new DotNetField("health", 16, FieldType: "System.Int32",
				ElementType: 8)]));
		harness.Answer<DotNetMethodPage>(static _ => new DotNetMethodPage("11", "33554433",
			[new DotNetMethod("100663297", "Damage", NativeCode: "7FF01000")], 1));
		DotNetTools tools = DotNet(harness);

		DotNetTypePage types = tools.ListTypes(" 11 ", 1, 1, "Player", Token);
		DotNetTypeDetails details = tools.GetType(" 11 ", " 33554433 ", 1, Token);
		DotNetMethodPage methods = tools.ListMethods(" 11 ", " 33554433 ", 0, 1, "Damage", Token);

		Assert.Equal(("11", "33554433", "Game.Player", 3, 2), (types.ModuleHandle, types.Types[0].Token,
			types.Types[0].Name, types.Total, types.NextOffset));
		Assert.Equal(("Game.Player", "System.Object", "health", 16L, 8L), (details.Name, details.BaseType,
			details.Fields[0].Name, details.Fields[0].Offset, details.Fields[0].ElementType));
		Assert.Equal(("100663297", "Damage", "7FF01000", 1), (methods.Methods[0].Token, methods.Methods[0].Name,
			methods.Methods[0].NativeCode, methods.Total));
		AssertOperations(harness,
			(CheatEngineToolNames.DotNetListTypes, "[1] = \"11\", [2] = 1, [3] = 1, [4] = \"Player\""),
			(CheatEngineToolNames.DotNetGetType, "[1] = \"11\", [2] = \"33554433\", [3] = 1"),
			(CheatEngineToolNames.DotNetListMethods,
				"[1] = \"11\", [2] = \"33554433\", [3] = 0, [4] = 1, [5] = \"Damage\""));
	}

	[Fact]
	public void DotNetParameterAndObjectAdapters_MapKnownFieldsAndNormalizedInputs()
	{
		StateTestHarness harness = new();
		harness.Answer<DotNetMethodParameters>(static _ => new DotNetMethodParameters("11", "100663297",
			[new DotNetParameter(0, "amount", 0x08)], "System.Void (System.Int32)"));
		harness.Answer<DotNetObject>(static _ => new DotNetObject("7FF02000", "Game.Player",
			[new DotNetObjectField("health", "100", "System.Int32", 16, 8)]));
		DotNetTools tools = DotNet(harness);

		DotNetMethodParameters parameters = tools.GetMethodParameters(" 11 ", " 100663297 ", Token);
		DotNetObject value = tools.GetObject(" [player]+10 ", 1, Token);

		Assert.Equal(("11", "100663297", "amount", "Int32", "System.Void (System.Int32)"),
			(parameters.ModuleHandle, parameters.MethodToken, parameters.Parameters[0].Name,
				parameters.Parameters[0].ElementTypeName, parameters.Signature));
		Assert.Equal(("7FF02000", "Game.Player", "health", "100", 16L), (value.Address, value.TypeName,
			value.Fields[0].Name, value.Fields[0].Value, value.Fields[0].Offset));
		AssertOperations(harness,
			(CheatEngineToolNames.DotNetGetMethodParameters, "[1] = \"11\", [2] = \"100663297\""),
			(CheatEngineToolNames.DotNetGetObject, "[1] = \"[player]+10\", [2] = 1"));
	}

	[Fact]
	public void MonoDiscoveryAdapters_MapStatusAndPagedCollectorResults()
	{
		StateTestHarness harness = new();
		harness.Answer<MonoStatus>(static _ => new MonoStatus(true, false, 7, ["3"]));
		harness.Answer<MonoAssemblyPage>(static _ => new MonoAssemblyPage([new MonoAssembly("5", "6", "Game")], 2, 1));
		harness.Answer<MonoClassPage>(static _ => new MonoClassPage("6", [new MonoClass("9", "Player", "Game")], 1));
		harness.Answer<MonoFieldList>(static _ => new MonoFieldList("9",
			[new MonoField("10", "health", "System.Int32", 16, false)]));
		harness.Answer<MonoMethodPage>(static _ => new MonoMethodPage("9",
			[new MonoMethod("11", "Damage", "System.Int32", "System.Void", ["amount"], 0x10, true)], 1));
		MonoTools tools = Mono(harness);

		MonoStatus status = tools.GetStatus(Token);
		MonoAssemblyPage assemblies = tools.ListAssemblies(0, 1, Token);
		MonoClassPage classes = tools.ListClasses(" 6 ", 0, 1, "Player", Token);
		MonoFieldList fields = tools.ListFields(" 9 ", false, 1, "health", Token);
		MonoMethodPage methods = tools.ListMethods(" 9 ", 0, 1, "Damage", Token);

		Assert.Equal((true, 7L, "3"), (status.Attached, status.CollectorVersion, Assert.Single(status.Domains!)));
		Assert.Equal(("5", "6", "Game", 2, 1), (assemblies.Assemblies[0].AssemblyHandle,
			assemblies.Assemblies[0].ImageHandle, assemblies.Assemblies[0].Name, assemblies.Total, assemblies.NextOffset));
		Assert.Equal(("6", "9", "Player", "Game"), (classes.ImageHandle, classes.Classes[0].Handle,
			classes.Classes[0].Name, classes.Classes[0].Namespace));
		Assert.Equal(("9", "10", "health", 16L), (fields.ClassHandle, fields.Fields[0].Handle,
			fields.Fields[0].Name, fields.Fields[0].Offset));
		Assert.Equal(("9", "11", "Damage", true), (methods.ClassHandle, methods.Methods[0].Handle,
			methods.Methods[0].Name, methods.Methods[0].IsStatic));
		AssertOperations(harness,
			(CheatEngineToolNames.MonoGetStatus, null),
			(CheatEngineToolNames.MonoListAssemblies, "[1] = 0, [2] = 1"),
			(CheatEngineToolNames.MonoListClasses, "[1] = \"6\", [2] = 0, [3] = 1, [4] = \"Player\""),
			(CheatEngineToolNames.MonoListFields, "[1] = \"9\", [2] = false, [3] = 1, [4] = \"health\""),
			(CheatEngineToolNames.MonoListMethods, "[1] = \"9\", [2] = 0, [3] = 1, [4] = \"Damage\""));
	}

	[Fact]
	public void MonoLookupAdapters_MapClassMethodAndStaticAddressWithExactHandles()
	{
		StateTestHarness harness = new();
		harness.Answer<MonoClass>(static _ => new MonoClass("9", "Player", "Game"));
		harness.Answer<MonoMethod>(static _ => new MonoMethod("11", "Damage", "System.Int32", "System.Void"));
		harness.Answer<MonoStaticFieldAddress>(static _ => new MonoStaticFieldAddress("9", "3", "7FF03000"));
		MonoTools tools = Mono(harness);

		MonoClass @class = tools.FindClass(" Game ", " Player ", Token);
		MonoMethod method = tools.FindMethod(" 9 ", " Damage ", Token);
		MonoStaticFieldAddress address = tools.GetStaticFieldAddress(" 9 ", " 3 ", Token);

		Assert.Equal(("9", "Player", "Game"), (@class.Handle, @class.Name, @class.Namespace));
		Assert.Equal(("11", "Damage", "System.Int32", "System.Void"), (method.Handle, method.Name, method.Signature,
			method.ReturnType));
		Assert.Equal(("9", "3", "7FF03000"), (address.ClassHandle, address.DomainHandle, address.Address));
		AssertOperations(harness,
			(CheatEngineToolNames.MonoFindClass, "[1] = \"Game\", [2] = \"Player\""),
			(CheatEngineToolNames.MonoFindMethod, "[1] = \"9\", [2] = \"Damage\""),
			(CheatEngineToolNames.MonoGetStaticFieldAddress, "[1] = \"9\", [2] = \"3\""));
	}

	[Fact]
	public void MonoGatedMutationAdapters_DeclareTheGateMapResultsAndReleaseAttachment()
	{
		StateTestHarness harness = new();
		harness.Answer<MonoAttachResult>(static _ => new MonoAttachResult(true, false, 7, ["collector_injected"]));
		harness.Answer<MonoCompiledMethod>(static _ => new MonoCompiledMethod("11", "7FF04000"));
		harness.Answer<MonoMethodInvocation>(static _ => new MonoMethodInvocation(true, "17"));
		harness.Answer<MonoDetachResult>(static _ => new MonoDetachResult(true, []));
		MonoTools tools = Mono(harness);

		AssertTargetCodeExecutionGate(nameof(MonoTools.Attach));
		AssertTargetCodeExecutionGate(nameof(MonoTools.CompileMethod));
		AssertTargetCodeExecutionGate(nameof(MonoTools.InvokeMethod));
		MonoAttachResult attached = tools.Attach(Token);
		MonoCompiledMethod compiled = tools.CompileMethod(" 11 ", Token);
		MonoMethodInvocation invoked = tools.InvokeMethod(" 11 ", " 401000 ", [new MonoInvokeArgument(2, "7")], Token);
		MonoDetachResult detached = tools.Detach(Token);

		Assert.Equal((true, 7L, "collector_injected"), (attached.Attached, attached.CollectorVersion,
			Assert.Single(attached.HostEffects)));
		Assert.Equal(("11", "7FF04000"), (compiled.MethodHandle, compiled.NativeAddress));
		Assert.Equal((true, "17", null), (invoked.Invoked, invoked.ReturnValue, invoked.Exception));
		Assert.True(detached.Detached);
		Assert.Empty(harness.Resources.List());
		AssertOperations(harness,
			(CheatEngineToolNames.MonoAttach, "[1] = \"mono-"),
			(CheatEngineToolNames.MonoCompileMethod, "[1] = \"11\""),
			(CheatEngineToolNames.MonoInvokeMethod, "[1] = \"11\", [2] = \"401000\", [3] = {{2,7,},}"),
			(CheatEngineToolNames.MonoDetach, "[1] = \"mono-"));
		Assert.Equal(4, harness.Dispatches);
	}

	private static DotNetTools DotNet(StateTestHarness harness) => new(harness.Dispatch, harness.Jobs);

	private static MonoTools Mono(StateTestHarness harness) => new(harness.Dispatch, harness.Jobs, harness.Resources);

	private static void AssertTargetCodeExecutionGate(string methodName)
	{
		MethodInfo method = typeof(MonoTools).GetMethod(methodName) ?? throw new InvalidOperationException(methodName);
		RequiresFeatureAttribute gate = Assert.Single(method.GetCustomAttributes<RequiresFeatureAttribute>());
		Assert.Equal(McpFeature.TargetCodeExecution, gate.Feature);
	}

	private static void AssertOperations(StateTestHarness harness, params (string Name, string? Arguments)[] expected)
	{
		(string Operation, string Source)[] actual = [.. harness.LuaCalls];
		Assert.Equal(expected.Select(static item => item.Name), actual.Select(static item => item.Operation));
		for (int index = 0; index < expected.Length; index++)
		{
			if (expected[index].Arguments is { } arguments)
			{
				Assert.Contains(arguments, actual[index].Source, StringComparison.Ordinal);
			}
		}
	}
}
