using CheatEngine.Mcp.Tests.Support;

namespace CheatEngine.Mcp.Tests.Core;

public sealed class TestDirectoryTests
{
	[Fact]
	public async Task DeleteStrictAsync_HeldFile_RethrowsPersistentCleanupFailure()
	{
		string root = Path.Combine(Path.GetTempPath(), $"CheatEngine.Mcp.Tests-{Guid.NewGuid():N}");
		Directory.CreateDirectory(root);
		string path = Path.Combine(root, "held.bin");
		try
		{
			using (FileStream held = new(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
			{
				Exception? exception = await Record.ExceptionAsync(() => TestDirectory.DeleteStrictAsync(root));

				Assert.NotNull(exception);
				Assert.True(exception is IOException or UnauthorizedAccessException);
				Assert.True(Directory.Exists(root));
			}
		}
		finally
		{
			if (Directory.Exists(root))
			{
				await TestDirectory.DeleteStrictAsync(root);
			}
		}
	}
}
