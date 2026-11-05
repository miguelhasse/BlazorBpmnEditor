using System.Text.Json.Serialization;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions;

namespace Hasseware.AspNetCore.Components.Bpmn.Serialization;

[JsonSerializable(typeof(BpmnExtensionElement))]
internal sealed partial class BpmnExtensionJsonContext : JsonSerializerContext;
