namespace Hasseware.FluentUI.AspNetCore.Components.Bpmn;

public sealed partial class FluentBpmnAgentConfirmation
{
    private string EditorText(string key, params object[] arguments) => arguments.Length == 0 ? EditorL[key] : EditorL[key, arguments];
}
