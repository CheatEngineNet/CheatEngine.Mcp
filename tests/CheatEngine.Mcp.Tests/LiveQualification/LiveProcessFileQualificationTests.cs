using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace CheatEngine.Mcp.Tests.LiveQualification;

[SupportedOSPlatform("windows")]
public sealed class LiveProcessFileQualificationTests
{
	[Fact]
	public void CreateInputBytes_IsDeterministicAndPageSized()
	{
		byte[] first = LiveProcessFileQualification.CreateInputBytes();
		byte[] second = LiveProcessFileQualification.CreateInputBytes();

		Assert.Equal(LiveProcessFileQualification.InputLength, first.Length);
		Assert.Equal(first, second);
		Assert.NotSame(first, second);
	}

	[Fact]
	public async Task DescribeFileAsync_ReportsFullPathExactLengthAndSha256()
	{
		string directory = Path.Combine(Path.GetTempPath(), "CheatEngine.Mcp.LiveProcessFileQualification",
			Guid.NewGuid().ToString("N"));
		string path = Path.Combine(directory, "owned.bin");
		byte[] bytes = LiveProcessFileQualification.CreateInputBytes();
		Directory.CreateDirectory(directory);
		try
		{
			await File.WriteAllBytesAsync(path, bytes, TestContext.Current.CancellationToken);

			LiveProcessFileQualification.FileContent actual =
				await LiveProcessFileQualification.DescribeFileAsync(path);

			Assert.Equal(bytes, actual.Bytes);
			Assert.Equal(Path.GetFullPath(path), actual.Fact.Path);
			Assert.Equal(bytes.LongLength, actual.Fact.Length);
			Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), actual.Fact.Sha256);
		}
		finally
		{
			Directory.Delete(directory, true);
		}
	}

	[Fact]
	public async Task DescribeFileAsync_OverInputLength_RefusesBeforeReading()
	{
		string directory = Path.Combine(Path.GetTempPath(), "CheatEngine.Mcp.LiveProcessFileQualification",
			Guid.NewGuid().ToString("N"));
		string path = Path.Combine(directory, "oversized.bin");
		Directory.CreateDirectory(directory);
		try
		{
			await File.WriteAllBytesAsync(path, new byte[LiveProcessFileQualification.InputLength + 1],
				TestContext.Current.CancellationToken);

			InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
				LiveProcessFileQualification.DescribeFileAsync(path));

			Assert.Contains("exceeds", exception.Message, StringComparison.Ordinal);
		}
		finally
		{
			Directory.Delete(directory, true);
		}
	}
}
