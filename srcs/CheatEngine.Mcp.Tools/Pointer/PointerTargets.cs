using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Values;

namespace CheatEngine.Mcp.Tools.Pointer;

/// <summary>Resolves the target address of an offline search: a literal needs no Cheat Engine call.</summary>
internal static class PointerTargets
{
	/// <summary>
	///     Resolves a target: hexadecimal text directly, anything else (a symbol or an expression) in one dispatch
	///     against the selected process.
	/// </summary>
	/// <param name="dispatch">The activation's dispatch facade.</param>
	/// <param name="operation">The tool name.</param>
	/// <param name="expression">The checked expression.</param>
	/// <param name="width">The pointer width the address must fit.</param>
	/// <param name="cancellationToken">The MCP request's token.</param>
	/// <returns>The address.</returns>
	/// <exception cref="CheatEngineToolException"><c>invalid_argument</c> when it does not fit the width.</exception>
	internal static ulong Resolve(ToolDispatch dispatch, string operation, string expression, int width,
		CancellationToken cancellationToken)
	{
		ulong address = HexParse.TryAddress(expression, out ulong literal)
			? literal
			: dispatch.Run(operation, token => PointerSupport.Resolve(dispatch.Client, expression, token),
				cancellationToken);
		return Check(address, width, "target");
	}

	/// <summary>Checks that an address fits a pointer width.</summary>
	/// <param name="address">The address.</param>
	/// <param name="width">4 or 8.</param>
	/// <param name="parameter">The parameter name.</param>
	/// <returns>The address.</returns>
	/// <exception cref="CheatEngineToolException"><c>invalid_argument</c>.</exception>
	internal static ulong Check(ulong address, int width, string parameter)
	{
		return width == 8 || address <= uint.MaxValue
			? address
			: throw CheatEngineToolException.InvalidArgument(parameter,
				$"{HexFormat.Address(address)} does not fit a 32-bit target's pointer.");
	}
}
