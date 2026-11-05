namespace Hasseware.AspNetCore.Components.Bpmn.Configuration;

/// <summary>Defaults that belong to Core for good: the canvas-intrinsic, accessibility-critical parts.</summary>
internal static class BpmnCoreDefaults
{
    internal static BpmnEditorComponentOptions CreateComponents() =>
        new BpmnEditorComponentOptions()
            .UseDefault<BpmnLabelEditor>(BpmnEditorPart.LabelEditor)
            .UseDefault<BpmnAccessibleTree>(BpmnEditorPart.AccessibleTree);
}
