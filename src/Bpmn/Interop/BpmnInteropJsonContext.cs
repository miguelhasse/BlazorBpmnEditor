using System.Text.Json.Serialization;
using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Execution;

namespace Hasseware.AspNetCore.Components.Bpmn.Interop;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(EditorConfig))]
[JsonSerializable(typeof(ViewportCommand))]
[JsonSerializable(typeof(BpmnExecutionState))]
[JsonSerializable(typeof(CanvasInitResult))]
[JsonSerializable(typeof(CanvasInitialized))]
[JsonSerializable(typeof(ConfigApplied))]
[JsonSerializable(typeof(ConfigRejected))]
[JsonSerializable(typeof(DocumentLoaded))]
[JsonSerializable(typeof(ViewportChanged))]
[JsonSerializable(typeof(ShortcutInvoked))]
[JsonSerializable(typeof(SelectionChanged))]
[JsonSerializable(typeof(SelectionAnchorChanged))]
[JsonSerializable(typeof(ContextMenuRequested))]
[JsonSerializable(typeof(LabelEditRequested))]
[JsonSerializable(typeof(FileDropped))]
[JsonSerializable(typeof(EditRequest))]
[JsonSerializable(typeof(EditRejected))]
[JsonSerializable(typeof(DropTarget))]
[JsonSerializable(typeof(Anchor))]
[JsonSerializable(typeof(CanvasDocumentPatch))]
internal sealed partial class BpmnInteropJsonContext : JsonSerializerContext;
