using Microsoft.AspNetCore.Components;
using Microsoft.FluentUI.AspNetCore.Components;

namespace Hasseware.FluentUI.AspNetCore.Components.Bpmn;

/// <summary>The Fluent UI rendering of the dialog component.</summary>
public partial class FluentBpmnDialog
{
    private FluentDialog? _dialog;
    private bool _shown;

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
        // Inline v5 dialogs preserve the editor scope; service-backed dialogs would reparent content.
        if (firstRender && _dialog is not null)
        {
            _shown = true;
            await _dialog.ShowAsync();
        }
    }

    private Task StateChangedAsync(DialogEventArgs args) =>
        _shown && args.State == DialogState.Closed ? OnDismiss.InvokeAsync() : Task.CompletedTask;
}
