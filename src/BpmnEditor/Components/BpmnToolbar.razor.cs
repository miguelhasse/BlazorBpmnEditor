namespace Hasseware.AspNetCore.Components.Bpmn;

public sealed partial class BpmnToolbar
{
    private string EditorText(string key, params object[] arguments) => EditorL.EditorText(key, arguments);
}
