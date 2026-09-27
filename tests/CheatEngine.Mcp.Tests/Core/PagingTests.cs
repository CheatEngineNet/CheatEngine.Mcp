using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Values;

namespace CheatEngine.Mcp.Tests.Core;

public sealed class PagingTests
{
	private static readonly int[] Items = [1, 2, 3, 4, 5];

	[Fact]
	public void Slice_FirstPage_ReportsTheNextOffset()
	{
		PageSlice<int> page = Paging.Slice(Items, 0, 2, 10);

		Assert.Equal([1, 2], page.Items);
		Assert.Equal(5, page.Total);
		Assert.Equal(2, page.NextOffset);
		Assert.False(page.Truncated);
	}

	[Fact]
	public void Slice_LastPage_OmitsTheNextOffset()
	{
		PageSlice<int> page = Paging.Slice(Items, 4, 2, 10, true);

		Assert.Equal([5], page.Items);
		Assert.Null(page.NextOffset);
		Assert.True(page.Truncated);
	}

	[Fact]
	public void Slice_OffsetPastTheEnd_IsAnEmptyCompletePage()
	{
		PageSlice<int> page = Paging.Slice(Items, 9, 2, 10);

		Assert.Empty(page.Items);
		Assert.Equal(5, page.Total);
		Assert.Null(page.NextOffset);
	}

	[Theory]
	[InlineData(-1, 1, "offset", ToolErrorKind.InvalidArgument)]
	[InlineData(0, 0, "limit", ToolErrorKind.InvalidArgument)]
	[InlineData(0, 11, "limit", ToolErrorKind.LimitExceeded)]
	public void Slice_InvalidArguments_NameTheParameter(int offset, int limit, string parameter, ToolErrorKind kind)
	{
		ToolError error = Assert.Throws<CheatEngineToolException>(() => Paging.Slice(Items, offset, limit, 10)).Error;

		Assert.Equal(kind, error.Kind);
		Assert.Equal($$"""{"parameter":"{{parameter}}"}""", error.Details!.Value.GetRawText());
	}
}
