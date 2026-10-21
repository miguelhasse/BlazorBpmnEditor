namespace Hasseware.AspNetCore.Components.Bpmn;

public sealed partial class BpmnPropertyPanel
{
    private string EditorText(string key, params object[] arguments) => EditorL.EditorText(key, arguments);
}
