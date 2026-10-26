using Hasseware.AspNetCore.Components.Bpmn.Editing;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.FluentUI.AspNetCore.Components;

namespace Hasseware.FluentUI.AspNetCore.Components.Bpmn;

public sealed partial class FluentBpmnToolbar
{
    private readonly string _colorsButtonId = $"bpmn-colors-{Guid.NewGuid():N}";
    private FluentButton? _colorsButton;
    private bool _colorsOpened;
    private bool _restoreColorsFocus;
    private FluentButton? _messages;

    private bool CanEditColors => CanColor(BpmnColorChannel.Border);

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        if (!ShowColorPicker || !CanEditColors)
        {
            _colorsOpened = false;
            _restoreColorsFocus = false;
        }
    }

    private void OnColorsKeyDown(KeyboardEventArgs args)
    {
        if (args.Key != "Escape")
            return;

        _colorsOpened = false;
        _restoreColorsFocus = true;
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_messages is not null)
            MessagesButton = _messages.Element;

        await base.OnAfterRenderAsync(firstRender).ConfigureAwait(true);

        if (_restoreColorsFocus && ShowColorPicker && _colorsButton is not null && CanEditColors)
        {
            _restoreColorsFocus = false;
            await _colorsButton.Element.FocusAsync().ConfigureAwait(true);
        }
    }


    private string EditorText(string key, params object[] arguments) => EditorL.EditorText(key, arguments);
}
