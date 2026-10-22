using Hasseware.AspNetCore.Components.Bpmn.Editing;

namespace Hasseware.AspNetCore.Components.Bpmn;

public sealed partial class BpmnToolbar
{
    private readonly string _colorsId = $"bpmn-colors-{Guid.NewGuid():N}";

    private bool CanEditColors => CanColor(BpmnColorChannel.Border);

    private string EditorText(string key, params object[] arguments) => EditorL.EditorText(key, arguments);
}
