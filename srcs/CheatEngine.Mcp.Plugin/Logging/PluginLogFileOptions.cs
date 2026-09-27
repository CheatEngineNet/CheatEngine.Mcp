namespace CheatEngine.Mcp.Plugin.Logging;

/// <summary>The bounds of the plugin log file and of its background queue.</summary>
/// <param name="MaxFileBytes">The size at which the current file rolls to the first archive.</param>
/// <param name="MaxArchives">The number of archives kept, <c>CheatEngine.Mcp.&lt;pid&gt;.1.log</c> being the newest.</param>
/// <param name="QueueCapacity">The number of entries that may wait for the writer before new ones are dropped.</param>
/// <param name="MaxEntryChars">The length at which one entry, exception included, is truncated.</param>
internal sealed record PluginLogFileOptions(long MaxFileBytes, int MaxArchives, int QueueCapacity, int MaxEntryChars)
{
	/// <summary>10 MiB per file, five archives, 8,192 queued entries and 32 KiB characters per entry.</summary>
	internal static PluginLogFileOptions Default
	{
		get;
	} = new(10 * 1024 * 1024, 5, 8192, 32 * 1024);

	/// <summary>Rejects bounds that could not hold one entry or one archive.</summary>
	internal void Validate()
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(MaxFileBytes, 1024L);
		ArgumentOutOfRangeException.ThrowIfLessThan(MaxArchives, 1);
		ArgumentOutOfRangeException.ThrowIfLessThan(QueueCapacity, 1);
		ArgumentOutOfRangeException.ThrowIfLessThan(MaxEntryChars, 256);
	}
}
