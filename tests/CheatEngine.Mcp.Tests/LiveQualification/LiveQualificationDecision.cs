namespace CheatEngine.Mcp.Tests.LiveQualification;

internal sealed record LiveQualificationDecision(LiveQualificationInputs? Inputs, string? Refusal)
{
	internal bool IsAuthorized => Inputs is not null;
}
