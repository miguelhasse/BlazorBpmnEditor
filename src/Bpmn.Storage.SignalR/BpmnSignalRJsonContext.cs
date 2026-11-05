using System.Text.Json.Serialization;

namespace Hasseware.AspNetCore.Components.Bpmn.Storage.SignalR;

[JsonSerializable(typeof(string))]
internal sealed partial class BpmnSignalRJsonContext : JsonSerializerContext;
