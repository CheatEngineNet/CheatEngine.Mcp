using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools;

/// <summary>Provides deterministic managed string conversions that do not require Cheat Engine state.</summary>
[McpServerToolType]
#pragma warning disable CA1822 // MCP tool types are registered as DI-managed instances.
public sealed class ConversionTool
{
	[McpServerTool(Name = "convert_string")]
	[Description("Convert text with a managed MD5 digest or explicit UTF-8 and ANSI byte round trips.")]
	public object ConvertString(
		[Description("Input text.")] string input,
		[Description("md5, ansitoutf8, or utf8toansi.")]
		string conversionType)
	{
		try
		{
			ArgumentNullException.ThrowIfNull(input);
			ArgumentException.ThrowIfNullOrWhiteSpace(conversionType);
			string result = conversionType.ToLowerInvariant() switch
			{
				"md5" => ComputeMd5(input),
				"ansitoutf8" => Encoding.UTF8.GetString(Encoding.Default.GetBytes(input)),
				"utf8toansi" => Encoding.Default.GetString(Encoding.UTF8.GetBytes(input)),
				_ => throw new ArgumentException("Conversion type must be md5, ansitoutf8, or utf8toansi.",
					nameof(conversionType))
			};
			return new { success = true, result };
		}
		catch (Exception exception)
		{
			return ToolExecution.Error(exception.Message);
		}
	}

#pragma warning disable CA5351 // The MCP contract deliberately exposes legacy MD5 text conversion, not a security primitive.
	private static string ComputeMd5(string input)
	{
		return Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
	}
#pragma warning restore CA5351
}
#pragma warning restore CA1822
