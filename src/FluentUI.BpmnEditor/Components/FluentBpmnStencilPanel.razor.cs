namespace Hasseware.FluentUI.AspNetCore.Components.Bpmn;

public sealed partial class FluentBpmnStencilPanel
{
    private string EditorText(string key, params object[] arguments) => EditorL.EditorText(key, arguments);
}
