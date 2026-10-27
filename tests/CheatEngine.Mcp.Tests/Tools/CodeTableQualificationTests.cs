using System.Reflection;

using CheatEngine.Client;
using CheatEngine.Client.Extensions.DependencyInjection;
using CheatEngine.Client.Tables;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Execution;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Core.Files;
using CheatEngine.Mcp.Tests.Core;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Code;
using CheatEngine.Mcp.Tools.Table;

using Microsoft.Extensions.Options;

namespace CheatEngine.Mcp.Tests.Tools;

/// <summary>Managed dispatch qualification for the table-save and code-view adapters.</summary>
public sealed class CodeTableQualificationTests : IDisposable
{
	private readonly McpFileScratch _scratch = new();

	public void Dispose() => _scratch.Dispose();

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void TableLoad_InspectedDataTable_ForwardsPinnedTrustedFileAndMerge(bool merge)
	{
		string root = _scratch.CreateFolder("tables");
		string path = Path.Combine(root, "plain.ct");
		const string content = "<?xml version=\"1.0\"?><CheatTable><CheatEntries><CheatEntry><ID>1</ID><Description>Health</Description><VariableType>4 Bytes</VariableType><Address>401000</Address></CheatEntry></CheatEntries></CheatTable>";
		File.WriteAllText(path, content);
		StateTestHarness lua = new();
		lua.Answer(static _ => new MonoAutoAttachState(false, true, true, true));
		int loads = 0;
		ITableClient tables = ClientTestDouble.Create<ITableClient>((method, arguments) =>
		{
			Assert.Equal(nameof(ITableClient.LoadTrustedTable), method.Name);
			TableLoadRequest request = Assert.IsType<TableLoadRequest>(arguments![0]);
			Assert.Equal((path, merge), (request.File.FullPath, request.Merge));
			Assert.Equal(content, File.ReadAllText(request.File.FullPath));
			loads++;
			return null;
		});
		IOptions<McpExecutionOptions> execution = Options.Create(new McpExecutionOptions());
		ToolDispatch dispatch = new(ClientTestDouble.Client(new RecordingDispatcher().Dispatcher, CancellationToken.None,
			(nameof(ICheatEngineClient.Tables), tables)), new McpFeatureGate(Options.Create(new McpFeatureOptions())),
			execution, new DispatchStatistics(execution), TimeProvider.System, new RecordingLogger<ToolDispatch>(), lua.FixedLua);
		CheatEngineClientOptions clientOptions = new();
		clientOptions.AllowedTableRoots.Add(root);
		TableTools tools = new(dispatch,
			new McpFilePaths(new McpFileOptions(), _scratch.CreateFolder("registry"), _scratch.CreateFolder("data")),
			Options.Create(clientOptions));

		TableLoadResult result = tools.Load(path, merge, TestContext.Current.CancellationToken);

		Assert.Equal((path, merge, false), (result.Path, result.Merge, result.MonoAutoAttach));
		Assert.Equal(("xml", false, false, false, 1, 0), (result.Inspection.Format, result.Inspection.ContainsLua,
			result.Inspection.ContainsForms, result.Inspection.UsesMono, result.Inspection.RecordCount,
			result.Inspection.AssemblerScriptCount));
		Assert.Empty(result.Inspection.Requires);
		Assert.Equal(1, loads);
		Assert.Equal(CheatEngineToolNames.TableLoad, Assert.Single(lua.LuaCalls).Operation);
		Assert.Equal(content, File.ReadAllText(path));
	}

	[Fact]
	public void TableSave_AllowedDestination_WritesAnExtensionPreservingTemporaryFileAndPublishesItAtomically()
	{
		string root = _scratch.CreateFolder("tables");
		string destination = Path.Combine(root, "saved.ct");
		RecordingDispatcher dispatcher = new();
		ITableClient tables = ClientTestDouble.Create<ITableClient>((method, arguments) =>
		{
			Assert.Equal(nameof(ITableClient.SaveTable), method.Name);
			PropertyInfo fileProperty = arguments![0]!.GetType().GetProperties()
				.Single(static property => property.PropertyType == typeof(TrustedTableFile));
			TrustedTableFile file = Assert.IsType<TrustedTableFile>(fileProperty.GetValue(arguments[0]));
			Assert.NotEqual(destination, file.FullPath);
			Assert.Equal(Path.GetDirectoryName(destination), Path.GetDirectoryName(file.FullPath));
			Assert.Equal(".ct", Path.GetExtension(file.FullPath));
			Assert.False(File.Exists(destination));
			File.WriteAllText(file.FullPath, "<CheatTable />");
			return null;
		});
		IOptions<McpExecutionOptions> execution = Options.Create(new McpExecutionOptions());
		ToolDispatch dispatch = new(ClientTestDouble.Client(dispatcher.Dispatcher, CancellationToken.None,
			(nameof(ICheatEngineClient.Tables), tables)), new McpFeatureGate(Options.Create(new McpFeatureOptions())),
			execution, new DispatchStatistics(execution), TimeProvider.System, new RecordingLogger<ToolDispatch>());
		CheatEngineClientOptions clientOptions = new();
		clientOptions.AllowedTableRoots.Add(root);
		TableTools tools = new(dispatch,
			new McpFilePaths(new McpFileOptions(), _scratch.CreateFolder("registry"), _scratch.CreateFolder("data")),
			Options.Create(clientOptions));

		TableSaveResult result = tools.Save(destination, cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(new TableSaveResult(destination, false), result);
		Assert.Equal("<CheatTable />", File.ReadAllText(destination));
		Assert.Equal(1, dispatcher.Calls);
	}

	[Fact]
	public void TableSave_ExistingDestinationWithoutOverwrite_RefusesBeforeTheClientCanReplaceIt()
	{
		string root = _scratch.CreateFolder("tables");
		string destination = Path.Combine(root, "saved.ct");
		File.WriteAllText(destination, "old");
		RecordingDispatcher dispatcher = new();
		ITableClient tables = ClientTestDouble.Create<ITableClient>((method, _) =>
			throw new Xunit.Sdk.XunitException($"Unexpected table call {method.Name}."));
		TableTools tools = CreateTableTools(dispatcher, tables, root);

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			tools.Save(destination, cancellationToken: TestContext.Current.CancellationToken));

		Assert.Equal((ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Contains("already exists", exception.Error.Message, StringComparison.Ordinal);
		Assert.Equal("old", File.ReadAllText(destination));
		Assert.Equal(0, dispatcher.Calls);
	}

	[Fact]
	public void TableSave_ConcurrentDestinationCreatorWithoutOverwrite_PreservesTheCompetingBytes()
	{
		string root = _scratch.CreateFolder("tables");
		string destination = Path.Combine(root, "saved.ct");
		RecordingDispatcher dispatcher = new();
		ITableClient tables = ClientTestDouble.Create<ITableClient>((method, arguments) =>
		{
			Assert.Equal(nameof(ITableClient.SaveTable), method.Name);
			TrustedTableFile file = Assert.IsType<TableSaveRequest>(arguments![0]).File;
			File.WriteAllText(destination, "concurrent");
			File.WriteAllText(file.FullPath, "candidate");
			return null;
		});
		TableTools tools = CreateTableTools(dispatcher, tables, root);

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() => tools.Save(destination,
			cancellationToken: TestContext.Current.CancellationToken));

		Assert.Equal((ToolErrorKind.PartialEffect, ToolHostEffect.Started),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal("concurrent", File.ReadAllText(destination));
		Assert.Empty(Directory.GetFiles(root, ".*.partial*"));
		Assert.Equal(1, dispatcher.Calls);
	}

	[Fact]
	public void TableSave_ExistingDestinationWithOverwrite_ReplacesOnlyAfterTheTemporarySaveSucceeds()
	{
		string root = _scratch.CreateFolder("tables");
		string destination = Path.Combine(root, "saved.ct");
		File.WriteAllText(destination, "old");
		RecordingDispatcher dispatcher = new();
		ITableClient tables = ClientTestDouble.Create<ITableClient>((method, arguments) =>
		{
			Assert.Equal(nameof(ITableClient.SaveTable), method.Name);
			TrustedTableFile file = Assert.IsType<TableSaveRequest>(arguments![0]).File;
			Assert.NotEqual(destination, file.FullPath);
			Assert.Equal("old", File.ReadAllText(destination));
			File.WriteAllText(file.FullPath, "new");
			return null;
		});
		TableTools tools = CreateTableTools(dispatcher, tables, root);

		TableSaveResult result = tools.Save(destination, overwrite: true,
			cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(new TableSaveResult(destination, true), result);
		Assert.Equal("new", File.ReadAllText(destination));
		Assert.Equal(1, dispatcher.Calls);
	}

	[Fact]
	public void TableSave_ClientFailure_RemovesTheProtectedTemporaryFileAndPreservesTheDestination()
	{
		string root = _scratch.CreateFolder("tables");
		string destination = Path.Combine(root, "saved.ct");
		File.WriteAllText(destination, "old");
		RecordingDispatcher dispatcher = new();
		CheatEngineToolException expected = CheatEngineToolException.InvalidState("native table save failed",
			effect: ToolHostEffect.Started);
		ITableClient tables = ClientTestDouble.Create<ITableClient>((method, arguments) =>
		{
			Assert.Equal(nameof(ITableClient.SaveTable), method.Name);
			TrustedTableFile file = Assert.IsType<TableSaveRequest>(arguments![0]).File;
			File.WriteAllText(file.FullPath, "incomplete");
			throw expected;
		});
		TableTools tools = CreateTableTools(dispatcher, tables, root);

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() => tools.Save(destination,
			overwrite: true, cancellationToken: TestContext.Current.CancellationToken));

		Assert.Same(expected, exception);
		Assert.Equal("old", File.ReadAllText(destination));
		Assert.Empty(Directory.GetFiles(root, ".*.partial*"));
		Assert.Equal(1, dispatcher.Calls);
	}

	[Fact]
	public void TableSave_ClientFailureWithLockedTemporary_ReportsUnconfirmedCleanupAndPreservesTheDestination()
	{
		string root = _scratch.CreateFolder("tables");
		string destination = Path.Combine(root, "saved.ct");
		File.WriteAllText(destination, "old");
		FileStream? temporaryHandle = null;
		RecordingDispatcher dispatcher = new();
		ITableClient tables = ClientTestDouble.Create<ITableClient>((method, arguments) =>
		{
			Assert.Equal(nameof(ITableClient.SaveTable), method.Name);
			TrustedTableFile file = Assert.IsType<TableSaveRequest>(arguments![0]).File;
			File.WriteAllText(file.FullPath, "incomplete");
			temporaryHandle = File.Open(file.FullPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
			throw new IOException("save failed while retaining the temporary table");
		});
		TableTools tools = CreateTableTools(dispatcher, tables, root);

		try
		{
			CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() => tools.Save(destination,
				overwrite: true, cancellationToken: TestContext.Current.CancellationToken));

			Assert.Equal((ToolErrorKind.PartialEffect, ToolHostEffect.CleanupUnconfirmed),
				(exception.Error.Kind, exception.Error.HostEffect));
			string hint = Assert.IsType<string>(exception.Error.Hint);
			Assert.Contains("protected temporary table", hint, StringComparison.Ordinal);
			Assert.Equal("old", File.ReadAllText(destination));
			Assert.Single(Directory.GetFiles(root, ".*.partial*"));
			Assert.Equal(1, dispatcher.Calls);
		}
		finally
		{
			temporaryHandle?.Dispose();
		}
	}

	private TableTools CreateTableTools(RecordingDispatcher dispatcher, ITableClient tables, string root)
	{
		IOptions<McpExecutionOptions> execution = Options.Create(new McpExecutionOptions());
		ToolDispatch dispatch = new(ClientTestDouble.Client(dispatcher.Dispatcher, CancellationToken.None,
			(nameof(ICheatEngineClient.Tables), tables)), new McpFeatureGate(Options.Create(new McpFeatureOptions())),
			execution, new DispatchStatistics(execution), TimeProvider.System, new RecordingLogger<ToolDispatch>());
		CheatEngineClientOptions options = new();
		options.AllowedTableRoots.Add(root);
		return new TableTools(dispatch,
			new McpFilePaths(new McpFileOptions(), _scratch.CreateFolder("registry"), _scratch.CreateFolder("data")),
			Options.Create(options));
	}

	[Fact]
	public void CodeComments_ForwardOrderedExpressionsAndReturnTheFixedLuaValues()
	{
		StateTestHarness harness = new();
		harness.Answer<CodeLuaComments>(source =>
		{
			Assert.Contains("[1] = {\"401000\",\"402000\",}", source, StringComparison.Ordinal);
			return new CodeLuaComments([new CodeLuaComment("401000", "entry"), new CodeLuaComment("402000", null)]);
		});
		CodeTools tools = new(harness.Dispatch, harness.Jobs);

		CodeComments result = tools.GetComments(["401000", "402000"], TestContext.Current.CancellationToken);

		Assert.Equal([new CodeComment("401000", "entry"), new CodeComment("402000", null)], result.Comments);
		AssertFixedCall(harness, CheatEngineToolNames.CodeGetComments, CodeScripts.GetComments);
		Assert.Equal(1, harness.Dispatches);
	}

	[Fact]
	public void CodeSetComment_ForwardsExactAddressAndBlankCommentToTheFixedBody()
	{
		StateTestHarness harness = new();
		harness.Answer<CodeLuaComments>(source =>
		{
			Assert.Contains("[1] = \"401000\", [2] = \"\"", source, StringComparison.Ordinal);
			return new CodeLuaComments([new CodeLuaComment("401000", string.Empty)]);
		});
		CodeTools tools = new(harness.Dispatch, harness.Jobs);

		CodeComment result = tools.SetComment("401000", string.Empty, TestContext.Current.CancellationToken);

		Assert.Equal(new CodeComment("401000", string.Empty), result);
		AssertFixedCall(harness, CheatEngineToolNames.CodeSetComment, CodeScripts.SetComment);
	}

	[Fact]
	public void CodeGetFunction_MapsKnownBoundariesAndJumpDestination()
	{
		StateTestHarness harness = new();
		harness.Answer<CodeLuaFunction>(source =>
		{
			Assert.Contains("[1] = \"401020\", [2] = 512", source, StringComparison.Ordinal);
			return new CodeLuaFunction(true, "401020", "401000", "401080", 128, true);
		});
		CodeTools tools = new(harness.Dispatch, harness.Jobs);

		CodeFunction result = tools.GetFunction("401020", 512, TestContext.Current.CancellationToken);

		Assert.Equal(new CodeFunction("401020", true, "401000", "401080", 128, true), result);
		AssertFixedCall(harness, CheatEngineToolNames.CodeGetFunction, CodeScripts.GetFunction);
	}

	private static void AssertFixedCall(StateTestHarness harness, string expectedOperation, string body)
	{
		(string operation, string source) = Assert.Single(harness.LuaCalls);
		Assert.Equal(expectedOperation, operation);
		Assert.Contains(body, source, StringComparison.Ordinal);
	}
}
