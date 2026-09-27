namespace CheatEngine.Mcp.Core.Composition;

/// <summary>The ordered primitive manifest shared by the live backend and the schema-only catalog.</summary>
public sealed class CheatEngineMcpPrimitiveOptions
{
	private readonly List<CheatEngineMcpPrimitive> _primitives = [];

	/// <summary>The declared primitive types in declaration order, each at most once.</summary>
	public IReadOnlyList<CheatEngineMcpPrimitive> Primitives => _primitives;

	/// <summary>Declares a primitive type; a repeated declaration is ignored.</summary>
	/// <param name="primitive">The primitive type to declare.</param>
	public void Add(CheatEngineMcpPrimitive primitive)
	{
		ArgumentNullException.ThrowIfNull(primitive);
		if (!_primitives.Contains(primitive))
		{
			_primitives.Add(primitive);
		}
	}
}
