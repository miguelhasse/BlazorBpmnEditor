using Hasseware.AspNetCore.Components.Bpmn.Resources;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Microsoft.FluentUI.AspNetCore.Components;

namespace Hasseware.FluentUI.AspNetCore.Components.Bpmn;

/// <summary>The Fluent UI rendering of the dialog component.</summary>
public partial class FluentBpmnDialog
{
    private FluentDialog? _dialog;
    private ElementReference _body;
    private bool _shown;
    private bool _transitioning;

    [Inject] private IStringLocalizer<BpmnStrings> Localizer { get; set; } = default!;

    /// <summary>Whether the owning editor currently needs this dialog.</summary>
    [Parameter] public bool Visible { get; set; } = true;

    /// <summary>Dialog content, kept within the owning editor's cascades.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>Heading content within the dialog header.</summary>
    [Parameter] public RenderFragment? TitleTemplate { get; set; }

    /// <summary>Footer actions, excluding the header Close button.</summary>
    [Parameter] public RenderFragment? ActionTemplate { get; set; }

    /// <summary>Localized label for the header Close button.</summary>
    [Parameter] public string? CloseLabel { get; set; }

    /// <summary>Additional CSS classes.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Raised when Fluent dismisses the dialog.</summary>
    [Parameter] public EventCallback OnDismiss { get; set; }

    /// <summary>Raised after the native modal releases its focus trap.</summary>
    [Parameter] public EventCallback OnClosed { get; set; }

    /// <summary>HTML and accessibility attributes on the dialog.</summary>
    [Parameter(CaptureUnmatchedValues = true)] public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    private string CloseText => CloseLabel ?? Localizer["ShortcutHelp.Close"];

    /// <summary>Restores focus inside a modal after its focused body content is replaced.</summary>
    public ValueTask FocusAsync() => _shown && _dialog is not null
        ? _body.FocusAsync(preventScroll: true) : ValueTask.CompletedTask;

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_transitioning || _dialog is null || Visible == _shown) return;

        _transitioning = true;
        _shown = Visible;
        try
        {
            if (_shown)
            {
                await _dialog.ShowAsync();
            }
            else
            {
                // Close the native modal before removing its host so focus and top-layer state are released.
                await _dialog.HideAsync();
                await OnClosed.InvokeAsync();
            }
        }
        finally
        {
            _transitioning = false;
        }

        StateHasChanged();
    }

    private async Task StateChangedAsync(DialogToggleEventArgs args)
    {
        // Fluent UI 5's OnStateChange filters out inline dialogs because they have no service instance.
        if (!_shown || args.Id != _dialog?.Id || args.NewState != "closed") return;

        _shown = false;
        await OnDismiss.InvokeAsync();
        await OnClosed.InvokeAsync();
    }
}
