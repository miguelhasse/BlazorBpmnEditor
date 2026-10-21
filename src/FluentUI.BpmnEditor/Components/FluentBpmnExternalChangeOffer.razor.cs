namespace Hasseware.FluentUI.AspNetCore.Components.Bpmn;

public sealed partial class FluentBpmnExternalChangeOffer
{
    private string Message => EditorText(MessageKey, DocumentName);

    private string EditorText(string key, params object[] arguments) => EditorL.EditorText(key, arguments);
}
