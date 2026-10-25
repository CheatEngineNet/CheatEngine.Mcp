using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Files;
using CheatEngine.Mcp.Tests.Tools.Memory;
using CheatEngine.Mcp.Tests.Tools.Modules;
using CheatEngine.Mcp.Tools.Investigation;

namespace CheatEngine.Mcp.Tests.Tools.Investigation;

public sealed class InvestigationToolsTests : IDisposable
{
	private readonly string _root = Path.Combine(Path.GetTempPath(), $"ce-mcp-notes-{Guid.NewGuid():N}");
	public InvestigationToolsTests()
	{
		Directory.CreateDirectory(_root);
	}
	public void Dispose() => Directory.Delete(_root, true);
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public void CreateThenRestartSearch_RecoversTheImmutableRevision()
	{
		InvestigationTools first = Tools();
		InvestigationNote created = first.CreateNote(_root, "Player_State", "Player state", "Observed health.", ["player"], "high", ["HUD matched memory"], cancellationToken: Token);
		InvestigationNote found = Assert.Single(Tools().SearchNotes(_root, tag: "PLAYER", cancellationToken: Token).Notes);
		Assert.Equal("player_state", created.Name);
		Assert.Equal(1, created.Revision);
		Assert.Equal("Player state", created.Title);
		Assert.Equal("Observed health.", created.Body);
		Assert.Equal(["player"], created.Tags);
		Assert.Equal("high", created.Confidence);
		Assert.Equal(["HUD matched memory"], created.Observations);
		Assert.Null(created.Binding);
		Assert.Equal(JsonSerializer.Serialize(created, InvestigationJsonContext.Default.InvestigationNote),
			JsonSerializer.Serialize(found, InvestigationJsonContext.Default.InvestigationNote));
		Assert.True(File.Exists(Path.Combine(_root, "cemcp-note-player_state.r0000000001.json")));
	}

	[Fact]
	public void Create_DefaultOptionalValues_RoundTripsAfterReconstruction()
	{
		Tools().CreateNote(_root, "plain", "title", "body", cancellationToken: Token);

		InvestigationNote found = Assert.Single(Tools().SearchNotes(_root, cancellationToken: Token).Notes);

		Assert.Equal("plain", found.Name);
		Assert.Null(found.Confidence);
		Assert.Null(found.Binding);
		Assert.Empty(found.Tags);
		Assert.Empty(found.Observations);
	}

	[Fact]
	public void Update_CompetingExpectedRevision_AllowsExactlyOneImmutableSuccessor()
	{
		InvestigationTools tools = Tools();
		tools.CreateNote(_root, "state", "one", "body", cancellationToken: Token);
		InvestigationNote updated = tools.UpdateNote(_root, "STATE", 1, "two", "body", cancellationToken: Token);
		CheatEngineToolException conflict = Assert.Throws<CheatEngineToolException>(() => tools.UpdateNote(_root, "state", 1, "three", "body", cancellationToken: Token));
		Assert.Equal(2, updated.Revision);
		Assert.Equal(ToolErrorKind.InvalidState, conflict.Error.Kind);
		Assert.Equal(2, Directory.GetFiles(_root, "cemcp-note-*.json").Length);
	}

	[Fact]
	public async Task Update_ConcurrentExpectedRevision_AllowsOneWinnerAndRereadsLatest()
	{
		Tools().CreateNote(_root, "race", "one", "body", cancellationToken: Token);
		Task<object> first = Task.Run(() => UpdateRace("first"));
		Task<object> second = Task.Run(() => UpdateRace("second"));
		object[] results = await Task.WhenAll(first, second);

		Assert.Single(results.OfType<InvestigationNote>());
		CheatEngineToolException failure = Assert.Single(results.OfType<CheatEngineToolException>());
		Assert.Equal(ToolErrorKind.InvalidState, failure.Error.Kind);
		InvestigationNote latest = Assert.Single(Tools().SearchNotes(_root, name: "race", cancellationToken: Token).Notes);
		Assert.Equal(2, latest.Revision);
		Assert.Equal(2, Directory.GetFiles(_root, "cemcp-note-race.r*.json").Length);
	}

	[Fact]
	public void Search_MalformedOrFutureVersion_IsRejected()
	{
		File.WriteAllText(Path.Combine(_root, "cemcp-note-bad.r0000000001.json"), "{\"schema\":\"cemcp.investigation-note\",\"version\":2}");
		Assert.Equal(ToolErrorKind.InvalidState, Assert.Throws<CheatEngineToolException>(() => Tools().SearchNotes(_root, cancellationToken: Token)).Error.Kind);
	}

	[Theory]
	[InlineData("{\"schema\":\"cemcp.investigation-note\",\"version\":1,\"name\":\"bad\",\"revision\":1,\"title\":\"title\",\"body\":\"body\",\"tags\":[],\"observations\":[],\"unknown\":true}")]
	[InlineData("{\"schema\":\"cemcp.investigation-note\",\"version\":1,\"name\":\"other\",\"revision\":1,\"title\":\"title\",\"body\":\"body\",\"tags\":[],\"observations\":[]}")]
	public void Search_StrictOrCanonicalMismatch_IsRejected(string content)
	{
		File.WriteAllText(Path.Combine(_root, "cemcp-note-bad.r0000000001.json"), content);

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			Tools().SearchNotes(_root, cancellationToken: Token));

		Assert.Equal(ToolErrorKind.InvalidState, exception.Error.Kind);
	}

	[Fact]
	public void Search_GappedRevisionsAndOversizedFile_AreRejected()
	{
		InvestigationTools tools = Tools();
		tools.CreateNote(_root, "gap", "title", "body", cancellationToken: Token);
		JsonObject revision = JsonNode.Parse(File.ReadAllText(Path.Combine(_root, "cemcp-note-gap.r0000000001.json")))!.AsObject();
		revision["revision"] = 3;
		File.WriteAllText(Path.Combine(_root, "cemcp-note-gap.r0000000003.json"), revision.ToJsonString());

		CheatEngineToolException gap = Assert.Throws<CheatEngineToolException>(() => tools.SearchNotes(_root, cancellationToken: Token));
		Assert.Equal(ToolErrorKind.InvalidState, gap.Error.Kind);
		File.Delete(Path.Combine(_root, "cemcp-note-gap.r0000000003.json"));
		File.WriteAllBytes(Path.Combine(_root, "cemcp-note-large.r0000000001.json"), new byte[InvestigationTools.MaximumFileBytes + 1]);

		CheatEngineToolException large = Assert.Throws<CheatEngineToolException>(() => tools.SearchNotes(_root, cancellationToken: Token));
		Assert.Equal(ToolErrorKind.LimitExceeded, large.Error.Kind);
	}

	[Theory]
	[InlineData("tags", "null")]
	[InlineData("observations", "null")]
	[InlineData("binding", "{\"identity\":null,\"rva\":\"1\"}")]
	[InlineData("binding", "{\"identity\":{},\"rva\":\"1\",\"rva\":\"2\"}")]
	[InlineData("binding", "{\"identity\":{\"name\":\"a.dll\",\"pointerBytes\":8,\"fingerprint\":\"BAD\",\"imageSize\":100,\"extra\":true},\"rva\":\"1\"}")]
	public void Search_NullCollectionsAndMalformedNestedBinding_AreRejected(string property, string json)
	{
		Tools().CreateNote(_root, "strict", "title", "body", cancellationToken: Token);
		string path = Path.Combine(_root, "cemcp-note-strict.r0000000001.json");
		JsonObject root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
		root.Remove(property);
		string prefix = root.ToJsonString()[..^1];
		File.WriteAllText(path, prefix + ",\"" + property + "\":" + json + "}");

		CheatEngineToolException error = Assert.Throws<CheatEngineToolException>(() => Tools().SearchNotes(_root, cancellationToken: Token));

		Assert.Equal(ToolErrorKind.InvalidState, error.Error.Kind);
	}

	[Fact]
	public void Create_DirectoryByteBudgetRefusesNewRevisionWithoutPublishing()
	{
		for (int index = 0; index < 64; index++)
		{
			string name = $"seed{index}";
			byte[] encoded = JsonSerializer.SerializeToUtf8Bytes(new InvestigationNoteFile("cemcp.investigation-note", 1,
				name, 1, "title", "body", [], null, [], null), InvestigationFileJsonContext.Default.InvestigationNoteFile);
			byte[] padded = new byte[InvestigationTools.MaximumFileBytes];
			padded.AsSpan().Fill((byte) ' ');
			encoded.CopyTo(padded, 0);
			File.WriteAllBytes(Path.Combine(_root, $"cemcp-note-{name}.r0000000001.json"), padded);
		}

		CheatEngineToolException error = Assert.Throws<CheatEngineToolException>(() =>
			Tools().CreateNote(_root, "overflow", "title", "body", cancellationToken: Token));

		Assert.Equal(ToolErrorKind.LimitExceeded, error.Error.Kind);
		Assert.False(File.Exists(Path.Combine(_root, "cemcp-note-overflow.r0000000001.json")));
		Assert.Equal(64, Tools().SearchNotes(_root, cancellationToken: Token).Total);
	}

	[Fact]
	public async Task Create_ConcurrentNamesAtRevisionLimit_OnlyOneCanPublish()
	{
		for (int index = 0; index < InvestigationTools.MaximumFiles - 1; index++)
		{
			string name = $"seed{index}";
			File.WriteAllBytes(Path.Combine(_root, $"cemcp-note-{name}.r0000000001.json"),
				JsonSerializer.SerializeToUtf8Bytes(new InvestigationNoteFile("cemcp.investigation-note", 1,
					name, 1, "title", "body", [], null, [], null), InvestigationFileJsonContext.Default.InvestigationNoteFile));
		}

		Task<object> first = Task.Run(() => TryCreate("first"), Token);
		Task<object> second = Task.Run(() => TryCreate("second"), Token);
		object[] results = await Task.WhenAll(first, second);

		InvestigationNote winner = Assert.Single(results.OfType<InvestigationNote>());
		CheatEngineToolException failure = Assert.Single(results.OfType<CheatEngineToolException>());
		if (failure.Error.Kind == ToolErrorKind.InvalidState)
		{
			// Loading 4095 revisions can outlast the bounded directory lease wait on a busy runner.
			Assert.Equal("The note directory is busy; retry after the current operation finishes.", failure.Error.Message);
			string losingName = winner.Name == "first" ? "second" : "first";
			failure = Assert.IsType<CheatEngineToolException>(TryCreate(losingName));
		}
		Assert.Equal(ToolErrorKind.LimitExceeded, failure.Error.Kind);
		Assert.Equal(InvestigationTools.MaximumFiles, Directory.GetFiles(_root, "cemcp-note-*.json").Length);
	}

	[Fact]
	public void CancelledCreate_DoesNotPublishRevision()
	{
		using CancellationTokenSource cancelled = new();
		cancelled.Cancel();

		Assert.Throws<OperationCanceledException>(() => Tools().CreateNote(_root, "cancelled", "title", "body", cancellationToken: cancelled.Token));

		Assert.Empty(Directory.GetFiles(_root, "cemcp-note-*.json"));
	}

	[Fact]
	public void Search_PaginatesOrdinalLatestAndExportUsesEnvelopeWithoutHostPath()
	{
		InvestigationTools tools = Tools();
		tools.CreateNote(_root, "zeta", "z", "body", cancellationToken: Token);
		tools.CreateNote(_root, "alpha", "a", "body", cancellationToken: Token);
		tools.UpdateNote(_root, "zeta", 1, "new", "body", cancellationToken: Token);

		InvestigationNotePage page = tools.SearchNotes(_root, offset: 0, limit: 1, cancellationToken: Token);
		InvestigationExportResult export = tools.ExportNotes(_root, Path.Combine(_root, "notes-export.json"), cancellationToken: Token);

		Assert.Equal("alpha", Assert.Single(page.Notes).Name);
		Assert.Equal(2, page.Total);
		Assert.Equal(1, page.NextOffset);
		Assert.Equal("notes-export.json", export.FileName);
		Assert.DoesNotContain(_root, export.FileName, StringComparison.OrdinalIgnoreCase);
		string json = File.ReadAllText(Path.Combine(_root, export.FileName));
		using JsonDocument document = JsonDocument.Parse(json);
		Assert.Equal("cemcp.investigation-export", document.RootElement.GetProperty("schema").GetString());
		Assert.Equal(1, document.RootElement.GetProperty("version").GetInt32());
		JsonElement notes = document.RootElement.GetProperty("notes");
		Assert.Equal(2, notes.GetArrayLength());
		Assert.Equal("alpha", notes[0].GetProperty("name").GetString());
		Assert.Equal("zeta", notes[1].GetProperty("name").GetString());
		Assert.Equal(2, notes[1].GetProperty("revision").GetInt32());
		Assert.Equal("new", notes[1].GetProperty("title").GetString());
	}

	[Fact]
	public void Export_CanonicalNameInAnotherNotebook_IsRefusedEvenWithOverwrite()
	{
		string other = Directory.CreateDirectory(Path.Combine(_root, "other")).FullName;
		Tools().CreateNote(other, "keep", "original", "body", cancellationToken: Token);
		string path = Path.Combine(other, "cemcp-note-keep.r0000000001.json");
		byte[] before = File.ReadAllBytes(path);

		Assert.Equal(ToolErrorKind.InvalidArgument, Assert.Throws<CheatEngineToolException>(() =>
			Tools().ExportNotes(_root, path, overwrite: true, cancellationToken: Token)).Error.Kind);

		Assert.Equal(before, File.ReadAllBytes(path));
	}

	[Fact]
	public void Export_DeniedWriteRoot_RemainsPolicyError()
	{
		TargetDouble target = new();
		InvestigationTools denied = new(new McpFilePaths(new McpFileOptions(), Path.Combine(_root, "registry"),
			Path.Combine(_root, "data")), target.Dispatch);

		CheatEngineToolException error = Assert.Throws<CheatEngineToolException>(() =>
			denied.ExportNotes(_root, Path.Combine(_root, "export.json"), cancellationToken: Token));

		Assert.Equal(ToolErrorKind.InvalidArgument, error.Error.Kind);
		Assert.False(File.Exists(Path.Combine(_root, "export.json")));
	}

	[Fact]
	public void Export_CanonicalDestinationAndWritePolicy_AreRejected()
	{
		InvestigationTools tools = Tools();
		tools.CreateNote(_root, "safe", "title", "body", cancellationToken: Token);

		CheatEngineToolException canonical = Assert.Throws<CheatEngineToolException>(() => tools.ExportNotes(_root,
			Path.Combine(_root, "cemcp-note-safe.r0000000001.json"), cancellationToken: Token));

		Assert.Equal(ToolErrorKind.InvalidArgument, canonical.Error.Kind);
		Assert.Equal("title", Tools().SearchNotes(_root, cancellationToken: Token).Notes[0].Title);
	}

	[Fact]
	public void Search_BindingValidationOnlyChecksTheRequestedPage()
	{
		ModuleSymbolTarget target = ModuleToolTests.LoadedSample();
		InvestigationTools tools = Tools(target);
		tools.CreateNote(_root, "zeta", "bound", "body", module: "sample.dll", rva: "4010", cancellationToken: Token);
		tools.CreateNote(_root, "alpha", "unbound", "body", cancellationToken: Token);
		int timestampOffset = TestPe.NtOffset + 8;
		BinaryPrimitives.WriteUInt32LittleEndian(target.Memory.AsSpan(timestampOffset), 123);

		InvestigationNotePage page = tools.SearchNotes(_root, limit: 1, validateBinding: true, cancellationToken: Token);

		Assert.Equal("alpha", Assert.Single(page.Notes).Name);
		Assert.Equal(2, page.Total);
		Assert.Equal(ToolErrorKind.InvalidState, Assert.Throws<CheatEngineToolException>(() =>
			tools.SearchNotes(_root, offset: 1, limit: 1, validateBinding: true, cancellationToken: Token)).Error.Kind);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void ValidateBindings_TargetChangesBetweenIndividuallyStableReads_RefusesMixedPageOrExport(bool export)
	{
		ModuleSymbolTarget target = ModuleToolTests.LoadedSample();
		InvestigationTools tools = Tools(target);
		tools.CreateNote(_root, "alpha", "first", "body", module: "sample.dll", rva: "4010", cancellationToken: Token);
		tools.CreateNote(_root, "beta", "second", "body", module: "sample.dll", rva: "4020", cancellationToken: Token);
		int reads = 0;
		bool switched = false;
		target.BeforeRead = _ => reads++;
		target.AfterCurrentProcess = () =>
		{
			if (reads == 1 && !switched)
			{
				switched = true;
				target.SelectionEpoch++;
			}
		};
		string output = Path.Combine(_root, "mixed.json");

		CheatEngineToolException error = Assert.Throws<CheatEngineToolException>(() =>
		{
			if (export)
			{
				tools.ExportNotes(_root, output, validateBinding: true, cancellationToken: Token);
			}
			else
			{
				tools.SearchNotes(_root, validateBinding: true, cancellationToken: Token);
			}
		});

		Assert.True(switched);
		Assert.Equal(2, reads);
		Assert.Equal(ToolErrorKind.TargetChanged, error.Error.Kind);
		Assert.False(File.Exists(output));
	}

	private object TryCreate(string name)
	{
		try
		{
			return Tools().CreateNote(_root, name, "title", "body", cancellationToken: Token);
		}
		catch (CheatEngineToolException exception) { return exception; }
	}

	[Fact]
	public void Update_RetainedStaleBinding_IsRejectedBeforePublishingSuccessor()
	{
		ModuleSymbolTarget target = ModuleToolTests.LoadedSample();
		InvestigationTools tools = Tools(target);
		tools.CreateNote(_root, "bound", "title", "body", module: "sample.dll", rva: "4010", cancellationToken: Token);
		int timestampOffset = TestPe.NtOffset + 8;
		BinaryPrimitives.WriteUInt32LittleEndian(target.Memory.AsSpan(timestampOffset),
			BinaryPrimitives.ReadUInt32LittleEndian(target.Memory.AsSpan(timestampOffset)) + 1);

		CheatEngineToolException stale = Assert.Throws<CheatEngineToolException>(() =>
			tools.UpdateNote(_root, "bound", 1, "next", "body", cancellationToken: Token));

		Assert.Equal(ToolErrorKind.InvalidState, stale.Error.Kind);
		Assert.Single(Directory.GetFiles(_root, "cemcp-note-bound.r*.json"));
	}

	private object UpdateRace(string title)
	{
		try
		{
			return Tools().UpdateNote(_root, "race", 1, title, "body", cancellationToken: CancellationToken.None);
		}
		catch (CheatEngineToolException exception)
		{
			return exception;
		}
	}

	private InvestigationTools Tools()
	{
		TargetDouble target = new();
		McpFilePaths files = new(new McpFileOptions { AllowedRoots = [_root] }, Path.Combine(_root, "registry"), Path.Combine(_root, "data"));
		return new InvestigationTools(files, target.Dispatch);
	}

	private InvestigationTools Tools(ModuleSymbolTarget target)
	{
		McpFilePaths files = new(new McpFileOptions { AllowedRoots = [_root] }, Path.Combine(_root, "registry"),
			Path.Combine(_root, "data"));
		return new InvestigationTools(files, target.Dispatch);
	}
}
