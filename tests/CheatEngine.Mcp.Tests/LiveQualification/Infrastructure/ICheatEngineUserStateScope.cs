namespace CheatEngine.Mcp.Tests.LiveQualification.Infrastructure;

/// <summary>A backed-up user state; disposing it restores and verifies the state if <see cref="Restore" /> did not.</summary>
internal interface ICheatEngineUserStateScope : IDisposable
{
	/// <summary>Whether the state was restored and verified equal to the backup.</summary>
	public bool Restored
	{
		get;
	}

	/// <summary>Restores the backup and verifies it; throws when the restored state differs.</summary>
	public void Restore();
}
