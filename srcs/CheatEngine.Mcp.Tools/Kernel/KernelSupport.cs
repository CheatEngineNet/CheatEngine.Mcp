using System.Globalization;

using CheatEngine.Client;
using CheatEngine.Client.Inspection;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Mcp.Tools.Kernel;

/// <summary>Bounded validation and typed Client address resolution for the kernel tools.</summary>
internal static class KernelSupport
{
	internal const int MaximumPhysicalBytes = 4096;

	// 0xFF with a two-character whitespace separator is the longest normal representation of every byte.
	internal const int MaximumPhysicalByteTextCharacters = MaximumPhysicalBytes * 6;
	internal const int MaximumWatchEntries = 4096;
	internal const int MaximumExpressionLength = 1024;

	// DBVM watches one 4 KiB physical page and silently shortens a range that crosses into the next one.
	internal const int PhysicalPageBytes = 0x1000;

	internal static string Expression(string? value, string parameter)
	{
		string text = value?.Trim() ?? string.Empty;
		if (text.Length is 0 or > MaximumExpressionLength || text.Any(char.IsControl))
		{
			throw CheatEngineToolException.InvalidArgument(parameter,
				$"must be an address expression of 1 to {MaximumExpressionLength} characters without control characters.");
		}

		return text;
	}

	internal static ulong PhysicalAddress(string? value, string parameter)
	{
		return HexParse.TryAddress(value, out ulong address)
			? address
			: throw CheatEngineToolException.InvalidArgument(parameter,
				"must be a physical hexadecimal address with at most 16 digits.");
	}

	internal static void PhysicalRange(ulong address, int size, string addressParameter)
	{
		if ((ulong) (size - 1) > ulong.MaxValue - address)
		{
			throw CheatEngineToolException.InvalidArgument(addressParameter,
				"plus the requested size must stay within the 64-bit physical-address range.");
		}
	}

	internal static void WithinPhysicalPage(ulong address, int size, string sizeParameter)
	{
		int available = PhysicalPageBytes - (int) (address & (PhysicalPageBytes - 1));
		if (size > available)
		{
			string message = string.Create(CultureInfo.InvariantCulture,
				$"must stay inside one 4 KiB physical page; at most {available} bytes fit after physicalAddress.");
			throw CheatEngineToolException.InvalidArgument(sizeParameter, message);
		}
	}

	internal static byte[] PhysicalBytes(string? value, string parameter)
	{
		if (value is { Length: > MaximumPhysicalByteTextCharacters })
		{
			throw CheatEngineToolException.LimitExceeded(parameter,
				$"accepts at most {MaximumPhysicalByteTextCharacters} characters for {MaximumPhysicalBytes} bytes.");
		}

		return HexParse.Bytes(value, parameter, MaximumPhysicalBytes);
	}

	internal static Address Resolve(ICheatEngineClient client, string expression, string parameter,
		CancellationToken cancellationToken)
	{
		if (client.Inspection.TryResolveAddress(new SymbolExpression(expression), AddressResolutionMode.Default,
				out Address address, out CheatEngineFailure failure, cancellationToken))
		{
			return address;
		}

		if (failure.Kind is CheatEngineFailureKind.NotFound)
		{
			throw CheatEngineToolException.NotFound($"{parameter} does not resolve to an address.",
				"Check the expression with symbol_resolve.");
		}

		throw CheatEngineToolException.FromFailure(failure, client.Stopping.IsCancellationRequested);
	}

	internal static void Range(int value, int minimum, int maximum, string parameter)
	{
		if (value < minimum)
		{
			throw CheatEngineToolException.InvalidArgument(parameter, $"must be at least {minimum}.");
		}

		if (value > maximum)
		{
			throw CheatEngineToolException.LimitExceeded(parameter, $"accepts at most {maximum}.");
		}
	}
}
