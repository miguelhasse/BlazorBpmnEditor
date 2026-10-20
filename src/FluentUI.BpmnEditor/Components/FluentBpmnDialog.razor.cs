using Microsoft.AspNetCore.Components;
using Microsoft.FluentUI.AspNetCore.Components;

namespace Hasseware.FluentUI.AspNetCore.Components.Bpmn;

/// <summary>The Fluent UI rendering of the dialog component.</summary>
public partial class FluentBpmnDialog
{
    private FluentDialog? _dialog;
    private bool _shown;
    private bool _transitioning;

    /// <summary>Whether the owning editor currently needs this dialog.</summary>
    [Parameter] public bool Visible { get; set; } = true;

    /// <summary>Dialog content, kept within the owning editor's cascades.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>Additional CSS classes.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Raised when Fluent dismisses the dialog.</summary>
    [Parameter] public EventCallback OnDismiss { get; set; }

    /// <summary>HTML and accessibility attributes on the dialog.</summary>
    [Parameter(CaptureUnmatchedValues = true)] public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

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
    }
}
