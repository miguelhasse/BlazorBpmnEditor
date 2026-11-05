using System.Text.Json.Serialization;

namespace Hasseware.AspNetCore.Components.Bpmn.WebMcp;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(IReadOnlyList<BpmnWebMcpToolDescriptor>))]
[JsonSerializable(typeof(BpmnWebMcp.BpmnWebMcpRegistration))]
internal sealed partial class BpmnWebMcpJsonContext : JsonSerializerContext;
