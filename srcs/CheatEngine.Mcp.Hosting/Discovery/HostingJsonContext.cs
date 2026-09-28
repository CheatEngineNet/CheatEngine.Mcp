using System.Text.Json;
using System.Text.Json.Serialization;

using CheatEngine.Mcp.Hosting.Gateway;

namespace CheatEngine.Mcp.Hosting.Discovery;

/// <summary>Reflection-free JSON for the registry record, the backend identity and the gateway's instance list.</summary>
/// <remarks>
///     The Web defaults keep the registry file, the <c>/instance</c> body and the <c>instance_list</c> result
///     byte-identical to the reflection serializer with <see cref="JsonSerializerDefaults.Web" />.
/// </remarks>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(InstanceDescriptor))]
[JsonSerializable(typeof(InstanceIdentity))]
[JsonSerializable(typeof(InstanceListResult))]
internal sealed partial class HostingJsonContext : JsonSerializerContext;
