using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CheatEngine.Mcp.Tools.Memory;

/// <summary>The composition entry point of the <c>memory</c> tool domain (19 tools in the v2 catalog).</summary>
public static class MemoryToolsBuilderExtensions
{
	extension(ICheatEngineMcpBuilder builder)
	{
		/// <summary>
		///     Declares the <c>memory_*</c> tool containers and their JSON metadata, and, for a backend, the activation's
		///     named allocations and memory snapshots.
		/// </summary>
		/// <returns>The same builder.</returns>
		public ICheatEngineMcpBuilder AddMemoryTools()
		{
			ArgumentNullException.ThrowIfNull(builder);
			if (builder.Mode is CheatEngineMcpMode.Backend)
			{
				builder.Services.TryAddScoped<AllocationRegistry>();
				builder.Services.TryAddScoped<MemorySnapshotStore>();
			}

			return builder
				.AddJsonTypeInfoResolver(MemoryJsonContext.Default)
				.AddToolType<MemoryReadTools>()
				.AddToolType<MemoryWriteTools>()
				.AddToolType<MemoryInfoTools>()
				.AddToolType<MemoryAllocationTools>()
				.AddToolType<MemoryFileTools>()
				.AddToolType<MemorySnapshotTools>()
				.AddToolType<MemorySampleTools>();
		}
	}
}
