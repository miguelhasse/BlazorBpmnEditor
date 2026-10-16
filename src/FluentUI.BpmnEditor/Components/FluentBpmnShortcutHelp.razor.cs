using Microsoft.FluentUI.AspNetCore.Components;

namespace Hasseware.FluentUI.AspNetCore.Components.Bpmn;

public sealed partial class FluentBpmnShortcutHelp
{
    private FluentButton? _close;

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_close is not null) CloseButton = _close.Element;
        await base.OnAfterRenderAsync(firstRender);
    }
}
