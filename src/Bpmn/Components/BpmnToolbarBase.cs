using System.Globalization;

using Hasseware.AspNetCore.Components.Bpmn.Interop;

using Microsoft.AspNetCore.Components;

namespace Hasseware.AspNetCore.Components.Bpmn;

/// <summary>
/// The logic of the zoom, history and help controls; a UI kit supplies the buttons.
/// </summary>
/// <remarks>
/// Commands go to the <see cref="BpmnEditorSession"/> and the readouts come from
/// <see cref="BpmnEditorState"/>, so the toolbar needs no host wiring and tracks wheel and drag
/// zoom as well as button presses.
/// </remarks>
public abstract class BpmnToolbarBase : BpmnEditorComponentBase
{
    /// <inheritdoc />
    protected override bool ShouldRenderOnStateChange(BpmnEditorState previous, BpmnEditorState current) =>
        ChildContent is not null || previous.Zoom != current.Zoom || previous.IsReadOnly != current.IsReadOnly
            || previous.CanUndo != current.CanUndo || previous.CanRedo != current.CanRedo;

    /// <summary>Accessible name of the toolbar; a localized default when not set.</summary>
    [Parameter]
    public string? AriaLabel { get; set; }

    /// <summary>Extra controls appended to the toolbar.</summary>
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    /// <summary>The accessible name in force.</summary>
    protected string EffectiveAriaLabel => string.IsNullOrWhiteSpace(AriaLabel) ? Text("Toolbar.AriaLabel") : AriaLabel;

    /// <summary>The zoom readout, such as <c>100%</c>.</summary>
    protected string ZoomLabel => string.Create(CultureInfo.InvariantCulture, $"{Math.Round(State.Zoom * 100d):0}%");

    /// <summary>Whether there is an edit to undo.</summary>
    protected bool CanUndo => !State.IsReadOnly && State.CanUndo;

    /// <summary>Whether there is an edit to redo.</summary>
    protected bool CanRedo => !State.IsReadOnly && State.CanRedo;

    /// <summary>Requests a viewport change.</summary>
    /// <param name="command">The change.</param>
    /// <returns>A task that completes once the canvas has acted.</returns>
    protected Task ExecuteAsync(ViewportCommand command) => Session.SetViewportAsync(command).AsTask();

    /// <summary>Undoes the last edit.</summary>
    /// <returns>A task that completes once the edit is undone.</returns>
    protected Task UndoAsync() => Session.UndoAsync();

    /// <summary>Redoes the last undone edit.</summary>
    /// <returns>A task that completes once the edit is redone.</returns>
    protected Task RedoAsync() => Session.RedoAsync();

    /// <summary>Opens the keyboard shortcut help.</summary>
    protected void ShowShortcuts() => Session.ShowShortcutHelp();
}
