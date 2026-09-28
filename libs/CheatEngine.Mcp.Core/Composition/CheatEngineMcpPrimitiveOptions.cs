using System.Text.Json.Serialization.Metadata;

namespace CheatEngine.Mcp.Core.Composition;

/// <summary>The ordered primitive manifest shared by the live backend and the schema-only catalog.</summary>
public sealed class CheatEngineMcpPrimitiveOptions
{
	private readonly List<IJsonTypeInfoResolver> _jsonResolvers = [];
	private readonly List<CheatEngineMcpPrimitive> _primitives = [];

	/// <summary>The declared primitive types in declaration order, each at most once.</summary>
	public IReadOnlyList<CheatEngineMcpPrimitive> Primitives => _primitives;

	/// <summary>
	///     The source-generated JSON metadata the primitive projects registered, in declaration order and each instance at
	///     most once. <see cref="CheatEngineMcpJson.CreateOptions" /> places them ahead of the SDK's resolvers.
	/// </summary>
	public IReadOnlyList<IJsonTypeInfoResolver> JsonResolvers => _jsonResolvers;

	/// <summary>
	///     The <c>initialize</c> instructions the stdio gateway advertises, or <see langword="null" /> when the composition
	///     declares none.
	/// </summary>
	public string? GatewayInstructions
	{
		get;
		private set;
	}

	/// <summary>
	///     The <c>initialize</c> instructions each instance backend advertises, or <see langword="null" /> when the
	///     composition declares none.
	/// </summary>
	public string? BackendInstructions
	{
		get;
		private set;
	}

	/// <summary>How many times the composition declared server instructions; validation accepts at most one.</summary>
	internal int InstructionDeclarations
	{
		get;
		private set;
	}

	/// <summary>
	///     Declares the server instructions of both hosts. Only one declaration is allowed: a repeated one is recorded and
	///     fails validation when the manifest is resolved, instead of silently replacing the first.
	/// </summary>
	/// <param name="gateway">The gateway's instructions, which route through <c>instance_list</c>.</param>
	/// <param name="backend">The instructions of a backend serving one instance.</param>
	public void SetServerInstructions(string gateway, string backend)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(gateway);
		ArgumentException.ThrowIfNullOrWhiteSpace(backend);
		InstructionDeclarations++;
		if (InstructionDeclarations == 1)
		{
			GatewayInstructions = gateway;
			BackendInstructions = backend;
		}
	}

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

	/// <summary>Registers a JSON metadata resolver; registering the same instance again is ignored.</summary>
	/// <param name="resolver">The resolver, usually a source-generated <c>JsonSerializerContext</c>.</param>
	public void AddJsonResolver(IJsonTypeInfoResolver resolver)
	{
		ArgumentNullException.ThrowIfNull(resolver);
		foreach (IJsonTypeInfoResolver registered in _jsonResolvers)
		{
			if (ReferenceEquals(registered, resolver))
			{
				return;
			}
		}

		_jsonResolvers.Add(resolver);
	}
}
