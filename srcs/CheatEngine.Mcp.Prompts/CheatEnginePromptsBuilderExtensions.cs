namespace CheatEngine.Mcp.Prompts;

/// <summary>The Prompts project's single composition entry point.</summary>
public static class CheatEnginePromptsBuilderExtensions
{
	extension(ICheatEngineMcpBuilder builder)
	{
		/// <summary>Declares every Cheat Engine prompt container. None is declared yet.</summary>
		/// <returns>The same builder.</returns>
		public ICheatEngineMcpBuilder AddPrompts()
		{
			ArgumentNullException.ThrowIfNull(builder);
			return builder;
		}
	}
}
