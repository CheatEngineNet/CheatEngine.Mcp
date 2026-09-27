using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.RegularExpressions;

using CheatEngine.Mcp.Prompts;
using CheatEngine.Mcp.Resources;
using CheatEngine.Mcp.Resources.Docs;
using CheatEngine.Mcp.Resources.Knowledge;
using CheatEngine.Mcp.Tests.Support;

using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace CheatEngine.Mcp.Tests.Resources;

/// <summary>The embedded knowledge documents and workflow bodies, and how every host serves them.</summary>
public sealed partial class KnowledgeResourceTests
{
	private const int DocumentBudgetBytes = 16 * 1024;
	private const int TotalBudgetBytes = 200 * 1024;

	/// <summary>Reference files that stay in the skill only: local notes and the legacy files P6 removes.</summary>
	private static readonly string[] NeverEmbedded =
	[
		"local-cheat-engine", "local-cheat-engine.example", "address-list-and-speedhack", "lua-execution",
		"scanning-and-debugging", "tool-catalog"
	];

	private static string ReferencesFolder => Path.Combine(RepositoryPaths.Root, "skills", "cheatengine-mcp",
		"references");

	internal static CheatEngineMcpPrimitiveOptions KnowledgeManifest
	{
		get;
	} = CheatEngineMcpComposition.CreateManifest(CheatEngineMcpMode.Catalog,
		static builder => builder.AddResources().AddPrompts());

	[Fact]
	public void Knowledge_EmbeddedDocuments_EqualTheExplicitListAndEachFileExists()
	{
		Assert.Equal(18, CheatEngineKnowledge.DocumentSlugs.Count);
		Assert.Equal(CheatEngineKnowledge.DocumentSlugs.Order(StringComparer.Ordinal),
			CheatEngineKnowledge.EmbeddedDocuments.Order(StringComparer.Ordinal));
		Assert.All(CheatEngineKnowledge.DocumentSlugs,
			static slug => Assert.True(File.Exists(Path.Combine(ReferencesFolder, slug + ".md")), slug));
	}

	[Fact]
	public void Knowledge_EmbeddedWorkflows_EqualTheWorkflowFiles()
	{
		string[] files = Directory.EnumerateFiles(Path.Combine(ReferencesFolder, "workflows"), "*.md")
			.Select(static path => Path.GetFileNameWithoutExtension(path))
			.Where(static name => !name.StartsWith("local-", StringComparison.Ordinal))
			.Order(StringComparer.Ordinal).ToArray();

		Assert.Equal(22, files.Length);
		Assert.Equal(files, CheatEngineKnowledge.WorkflowNames);
	}

	[Fact]
	public void Knowledge_LocalAndLegacyFiles_AreNeverEmbedded()
	{
		string[] embedded = typeof(CheatEngineKnowledge).Assembly.GetManifestResourceNames()
			.Concat(typeof(CheatEngineWorkflowPrompts).Assembly.GetManifestResourceNames()).ToArray();

		Assert.All(embedded, static name => Assert.StartsWith("CheatEngine.Mcp.Knowledge/", name,
			StringComparison.Ordinal));
		Assert.All(NeverEmbedded, name => Assert.DoesNotContain(embedded,
			resource => resource.EndsWith("/" + name + ".md", StringComparison.Ordinal)));
	}

	[Fact]
	public void Docs_EachSlug_IsOneLocalMarkdownResourceTitledLikeItsHeading()
	{
		McpPrimitiveCatalog catalog = McpPrimitiveCatalog.Create(KnowledgeManifest);

		Assert.Equal(CheatEngineKnowledge.DocumentSlugs.Select(McpResourceUris.Doc).Order(StringComparer.Ordinal),
			catalog.Resources.Select(static resource => resource.Uri).Order(StringComparer.Ordinal));
		foreach (string slug in CheatEngineKnowledge.DocumentSlugs)
		{
			Resource resource = Assert.Single(catalog.Resources,
				candidate => candidate.Uri == McpResourceUris.Doc(slug));
			string markdown = CheatEngineKnowledge.ReadDocument(slug);
			Assert.Equal(Heading(markdown), resource.Title);
			Assert.Equal("doc_" + slug.Replace('-', '_'), resource.Name);
			Assert.Equal(McpResourceUris.MarkdownMimeType, resource.MimeType);
			Assert.False(string.IsNullOrWhiteSpace(resource.Description));
			Assert.Equal(Encoding.UTF8.GetByteCount(markdown), resource.Size);
		}

		Assert.All(catalog.LocalResources, static entry => Assert.Equal(McpPrimitiveRouting.Local, entry.Routing));
		Assert.Empty(catalog.InstanceResources);
		ResourceTemplate template = Assert.Single(catalog.ResourceTemplates);
		Assert.Equal("cheatengine://docs/workflows/{workflow}", template.UriTemplate);
		Assert.Equal(McpResourceUris.MarkdownMimeType, template.MimeType);
	}

	/// <summary>
	///     The budgets apply to the knowledge files as written (UTF-8, LF line endings); serving them rewrites each
	///     relative link to its longer <c>cheatengine://docs/…</c> URI, which the test reports but does not bound.
	/// </summary>
	[Fact]
	public void Docs_Sizes_StayWithinTheBudgets()
	{
		int total = 0;
		int served = 0;
		List<string> sizes = [];
		foreach (string slug in CheatEngineKnowledge.DocumentSlugs)
		{
			int size = Encoding.UTF8.GetByteCount(Embedded(McpKnowledgeText.DocumentsLogicalPrefix + slug + ".md"));
			Assert.True(size <= DocumentBudgetBytes, $"{slug} is {size} bytes; the budget is {DocumentBudgetBytes}.");
			total += size;
			served += Encoding.UTF8.GetByteCount(CheatEngineKnowledge.ReadDocument(slug));
			sizes.Add($"{slug}={size}");
		}

		Assert.True(total <= TotalBudgetBytes, $"The documents total {total} bytes; the budget is {TotalBudgetBytes}.");
		TestContext.Current.TestOutputHelper?.WriteLine(
			$"documents={total} served={served} {string.Join(' ', sizes)}");
	}

	[Fact]
	public void Docs_RelativeLinks_RewriteToExistingDocsUris()
	{
		HashSet<string> known = new(CheatEngineKnowledge.DocumentSlugs.Select(McpResourceUris.Doc)
			.Concat(CheatEngineKnowledge.WorkflowNames.Select(McpResourceUris.Workflow)), StringComparer.Ordinal);
		IEnumerable<(string Name, string Markdown)> served = CheatEngineKnowledge.DocumentSlugs
			.Select(static slug => (slug, CheatEngineKnowledge.ReadDocument(slug)))
			.Concat(CheatEngineKnowledge.WorkflowNames.Select(static workflow =>
				(workflow, CheatEngineKnowledge.TryReadWorkflow(workflow, out string? markdown) ? markdown : "")));
		int links = 0;
		foreach ((string name, string markdown) in served)
		{
			foreach (Match link in MarkdownLink().Matches(markdown))
			{
				string target = link.Groups["target"].Value;
				if (target.StartsWith("http://", StringComparison.Ordinal) ||
					target.StartsWith("https://", StringComparison.Ordinal))
				{
					continue;
				}

				links++;
				Assert.True(known.Contains(target.Split('#')[0]),
					$"{name} links to '{target}', which is not a served document or workflow.");
			}
		}

		Assert.True(links > 100, $"Only {links} knowledge links were checked.");
	}

	[Fact]
	public async Task Docs_ListAndRead_ServeMarkdownWithACacheHint()
	{
		await using TestMcpPipeline pipeline =
			await TestMcpPipeline.StartAsync(KnowledgeManifest, McpPrimitiveBinding.Catalog);

		IList<McpClientResource> resources =
			await pipeline.Client.ListResourcesAsync(cancellationToken: TestContext.Current.CancellationToken);
		ReadResourceResult safety = await pipeline.Client.ReadResourceAsync(McpResourceUris.Doc("safety"),
			cancellationToken: TestContext.Current.CancellationToken);
		ReadResourceResult workflow = await pipeline.Client.ReadResourceAsync(
			McpResourceUris.Workflow("find-writer"), cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(18, resources.Count);
		TextResourceContents text = Assert.IsType<TextResourceContents>(Assert.Single(safety.Contents));
		Assert.Equal(McpResourceUris.Doc("safety"), text.Uri);
		Assert.Equal(McpResourceUris.MarkdownMimeType, text.MimeType);
		Assert.Equal(CheatEngineKnowledge.ReadDocument("safety"), text.Text);
		Assert.Equal(TimeSpan.FromHours(1), safety.TimeToLive);
		Assert.Equal(CacheScope.Public, safety.CacheScope);
		TextResourceContents body = Assert.IsType<TextResourceContents>(Assert.Single(workflow.Contents));
		Assert.Equal(McpResourceUris.Workflow("find-writer"), body.Uri);
		Assert.StartsWith("# Find what writes or accesses an address", body.Text, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("2025-06-18", McpErrorCode.ResourceNotFound)]
	[InlineData("2026-07-28", McpErrorCode.InvalidParams)]
	public async Task Docs_UnknownWorkflow_IsAJsonRpcNotFoundWithTheContractData(string version, McpErrorCode code)
	{
		await using TestMcpPipeline pipeline =
			await TestMcpPipeline.StartAsync(KnowledgeManifest, McpPrimitiveBinding.Catalog, version);

		McpProtocolException exception = await Assert.ThrowsAsync<McpProtocolException>(async () =>
			await pipeline.Client.ReadResourceAsync(McpResourceUris.Workflow("find-nothing"),
				cancellationToken: TestContext.Current.CancellationToken));

		Assert.Equal(code, exception.ErrorCode);
		Assert.Equal("not_found", exception.Data["kind"]);
		Assert.Equal("not_started", exception.Data["hostEffect"]);
		Assert.Contains("find-writer", Assert.IsType<string>(exception.Data["hint"]), StringComparison.Ordinal);
	}

	[Fact]
	public async Task WorkflowTemplate_AllowedValues_CompleteWithoutAHandler()
	{
		await using TestMcpPipeline pipeline =
			await TestMcpPipeline.StartAsync(KnowledgeManifest, McpPrimitiveBinding.Catalog);

		CompleteResult result = await pipeline.Client.CompleteAsync(
			new ResourceTemplateReference { Uri = "cheatengine://docs/workflows/{workflow}" }, "workflow", "find",
			cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(["find-float-value", "find-known-value", "find-unknown-value", "find-writer"],
			result.Completion.Values);
	}

	[Fact]
	public void WorkflowTemplate_AllowedValues_AreTheEmbeddedWorkflows()
	{
		AllowedValuesAttribute allowed = typeof(CheatEngineDocResources)
			.GetMethod(nameof(CheatEngineDocResources.Workflow))!.GetParameters()[0]
			.GetCustomAttributes(typeof(AllowedValuesAttribute), false).Cast<AllowedValuesAttribute>().Single();

		Assert.Equal(CheatEngineKnowledge.WorkflowNames, allowed.Values.Cast<string>());
	}

	private static string Embedded(string logicalName)
	{
		using Stream stream = typeof(CheatEngineKnowledge).Assembly.GetManifestResourceStream(logicalName)!;
		using StreamReader reader = new(stream, Encoding.UTF8, true);
		return reader.ReadToEnd().ReplaceLineEndings("\n");
	}

	internal static string Heading(string markdown)
	{
		string first = markdown.Split('\n')[0].TrimEnd('\r');
		Assert.StartsWith("# ", first, StringComparison.Ordinal);
		return first[2..];
	}

	[GeneratedRegex(@"\]\((?<target>[^)\s]+)\)", RegexOptions.CultureInvariant)]
	private static partial Regex MarkdownLink();
}
