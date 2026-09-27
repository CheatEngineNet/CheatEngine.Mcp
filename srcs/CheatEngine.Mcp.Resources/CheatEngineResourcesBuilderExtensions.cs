namespace CheatEngine.Mcp.Resources;

/// <summary>The Resources project's single composition entry point.</summary>
public static class CheatEngineResourcesBuilderExtensions
{
	extension(ICheatEngineMcpBuilder builder)
	{
		/// <summary>Declares every Cheat Engine resource container. None is declared yet.</summary>
		/// <returns>The same builder.</returns>
		public ICheatEngineMcpBuilder AddResources()
		{
			ArgumentNullException.ThrowIfNull(builder);
			return builder;
		}
	}
}
