namespace CheatEngine.Mcp.Prompts;

/// <summary>The Prompts project's single composition entry point.</summary>
public static class CheatEnginePromptsBuilderExtensions
{
	extension(ICheatEngineMcpBuilder builder)
	{
		/// <summary>
		///     Declares the server instructions (<see cref="McpServerInstructions" />) and the guided workflow prompts
		///     (<see cref="CheatEngineWorkflowPrompts" />, one per workflow), which are Local: every backend and the
		///     gateway serve them.
		/// </summary>
		/// <returns>The same builder.</returns>
		public ICheatEngineMcpBuilder AddPrompts()
		{
			ArgumentNullException.ThrowIfNull(builder);
			return builder.SetServerInstructions(McpServerInstructions.Gateway, McpServerInstructions.Backend)
				.AddPromptType<CheatEngineWorkflowPrompts>();
		}
	}
}
