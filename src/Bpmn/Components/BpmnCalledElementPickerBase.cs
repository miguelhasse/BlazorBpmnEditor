using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Hasseware.AspNetCore.Components.Bpmn;

/// <summary>
/// The logic of the dialog that asks which reusable definition a Call Activity invokes; a UI kit
/// supplies the markup.
/// </summary>
/// <remarks>
/// Section 9.1 requires a Call Activity to name an existing global Process or Global Task, and
/// forbids inventing a hidden embedded graph when it does not. So the drop cannot create anything
/// on its own: it opens this, and the creation happens when the question is answered.
/// </remarks>
public abstract class BpmnCalledElementPickerBase : BpmnEditorComponentBase
{
    /// <inheritdoc />
    protected override bool ShouldRenderOnStateChange(BpmnEditorState previous, BpmnEditorState current) =>
        previous.IsPickingCalledElement != current.IsPickingCalledElement
            || previous.CallableDefinitions != current.CallableDefinitions;

    /// <summary>A stable id for the heading's labelled-by relationship.</summary>
    protected string TitleId { get; } = $"bpmn-picker-{Guid.NewGuid():N}";

    /// <summary>The dialog heading and the list's accessible name; a localized default when not set.</summary>
    [Parameter]
    public string? Title { get; set; }

    /// <summary>Shown when the document declares nothing callable; a localized default when not set.</summary>
    [Parameter]
    public string? EmptyMessage { get; set; }

    /// <summary>The heading in force.</summary>
    protected string EffectiveTitle => string.IsNullOrWhiteSpace(Title) ? Text("CalledElementPicker.Title") : Title;

    /// <summary>The empty-state text in force.</summary>
    protected string EffectiveEmptyMessage => string.IsNullOrWhiteSpace(EmptyMessage)
        ? Text("CalledElementPicker.Empty")
        : EmptyMessage;

    /// <summary>Whether the picker is open.</summary>
    protected bool IsVisible => State.IsPickingCalledElement;

    /// <summary>The definitions that may be called, as id and display name.</summary>
    protected IReadOnlyList<(string Id, string? Name)> Definitions => State.CallableDefinitions;

    /// <summary>Creates the Call Activity naming the chosen definition.</summary>
    /// <param name="id">The definition's id.</param>
    /// <returns>A task that completes once the activity is created.</returns>
    protected Task ChooseAsync(string id) => Session.ChooseCalledElementAsync(id);

    /// <summary>Backs out without choosing.</summary>
    /// <returns>A completed task.</returns>
    protected Task CancelAsync()
    {
        Session.CancelCalledElement();
        return Task.CompletedTask;
    }

    /// <summary>Escape closes it, the same "never mind" as clearing a selection.</summary>
    /// <param name="args">The key event.</param>
    /// <returns>A completed task.</returns>
    protected Task HandleKeyDownAsync(KeyboardEventArgs args) =>
        args.Key == "Escape" ? CancelAsync() : Task.CompletedTask;
}
