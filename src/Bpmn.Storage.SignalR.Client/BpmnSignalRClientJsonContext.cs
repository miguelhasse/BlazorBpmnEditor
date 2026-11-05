using System.Text.Json.Serialization;

namespace Hasseware.AspNetCore.Components.Bpmn.Storage.SignalR.Client;

[JsonSerializable(typeof(string))]
internal sealed partial class BpmnSignalRClientJsonContext : JsonSerializerContext;
