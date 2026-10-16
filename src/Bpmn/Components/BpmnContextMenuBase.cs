using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Interop;

namespace Hasseware.AspNetCore.Components.Bpmn;

/// <summary>
/// The logic of the Blazor-owned context menu (specification section 7); a UI kit supplies the
/// markup.
/// </summary>
/// <remarks>
/// Everything it exposes comes from the <see cref="BpmnElementSnapshot"/> in the editor's
/// <see cref="BpmnContextMenuState"/> slice. It never sees the document, so it cannot show a value
/// that disagrees with the model. In Readonly the snapshot arrives with every capability false, so
/// the same markup renders an inspector.
/// </remarks>
public abstract class BpmnContextMenuBase : BpmnCanvasWidgetBase<BpmnContextMenuState>
{
    /// <inheritdoc />
    protected override bool ShouldRenderOnStateChange(BpmnEditorState previous, BpmnEditorState current) =>
        !ReferenceEquals(previous.Overlays.ContextMenu, current.Overlays.ContextMenu);

    /// <summary>
    /// How many property edits this menu has submitted.
    /// </summary>
    /// <remarks>
    /// Part of each control's key, so every control is rebuilt from the snapshot once an edit has
    /// been through the document: a refusal leaves the rendered value unchanged, and without a new
    /// key the control the user clicked would keep showing a state the document refused.
    /// </remarks>
    protected int Revision { get; private set; }

    /// <inheritdoc />
    protected override BpmnContextMenuState? WidgetState => State.Overlays.ContextMenu;

    /// <inheritdoc />
    protected override bool IsOpen => WidgetState?.IsOpen == true;

    /// <inheritdoc />
    protected override Anchor? Anchor => WidgetState?.Anchor;

    /// <summary>What the request resolved to: <c>element</c>, <c>link</c> or <c>canvas</c>.</summary>
    protected string Target => WidgetState?.Target ?? "canvas";

    /// <summary>The target's properties, or <see langword="null"/> for a canvas-level menu.</summary>
    protected BpmnElementSnapshot? Selected => WidgetState?.Snapshot;

    /// <summary>The commands to offer, in display order.</summary>
    protected IReadOnlyList<BpmnMenuEntry> Entries => WidgetState?.Commands ?? [];

    /// <summary>The heading, which is also the menu's accessible name.</summary>
    protected string Title => Selected is { } snapshot
        ? snapshot.Label is { Length: > 0 } label ? label : Display.ElementType(snapshot.ElementType)
        : Text("ContextMenu.CanvasTitle");

    /// <inheritdoc />
    protected override string DefaultAriaLabel => Title;

    /// <summary>The localized name of the group listing validation findings.</summary>
    protected string FindingsLabel => Text("ContextMenu.Findings");

    /// <summary>The localized name of the command group.</summary>
    protected string CommandsLabel => Text("ContextMenu.Commands");

    /// <summary>The localized heading of the type-switch group.</summary>
    protected string SwitchTypeLabel => Text("ContextMenu.SwitchType");

    /// <summary>The localized heading of the property group.</summary>
    protected string PropertiesLabel => Text("ContextMenu.Properties");

    /// <summary>The localized empty-state text.</summary>
    protected string EmptyMessage => Text("ContextMenu.Empty");

    /// <summary>
    /// Absolute placement: the bottom-left of the anchor, which for a zero-size anchor is the
    /// point itself.
    /// </summary>
    /// <remarks>
    /// The anchor is already relative to the canvas host's top-left corner, so nothing here knows
    /// about the paper transform, the scroll position or the page.
    /// </remarks>
    protected string PositionStyle => AnchorStyle(client => client.X, client => client.Y + client.Height);

    /// <summary>The localized label of a built-in menu entry; already localized and host labels are kept.</summary>
    /// <param name="entry">The entry.</param>
    /// <returns>The text to show.</returns>
    protected string LabelFor(BpmnMenuEntry entry) => Display.MenuCommand(entry);

    /// <summary>Runs a menu command; the editor closes the menu.</summary>
    /// <param name="command">The command.</param>
    /// <returns>A task that completes once the command has run.</returns>
    protected Task InvokeAsync(BpmnMenuCommand command) => Session.InvokeMenuCommandAsync(command);

    /// <summary>Switches the target's type, keeping the menu open.</summary>
    /// <param name="option">The type chosen.</param>
    /// <returns>A task that completes once the switch is applied or refused.</returns>
    protected Task SwitchTypeAsync(BpmnTypeOption option) => Selected is null
        ? Task.CompletedTask
        : Session.SwitchElementTypeAsync(
            new SwitchElementTypeCommand(Selected.ElementId, option.Type, option.EventDefinition));

    /// <summary>
    /// Submits one property.
    /// </summary>
    /// <remarks>
    /// One property per command rather than a whole form on a Save button: a refusal that names
    /// the property it is about is only possible if that property is what was sent.
    /// </remarks>
    /// <param name="edit">The edit.</param>
    /// <returns>A task that completes once the edit is applied or refused.</returns>
    protected Task SetAsync(BpmnPropertyEdit edit)
    {
        if (Selected is null) return Task.CompletedTask;

        Revision++;

        return Session.ApplyMenuPropertyAsync(new UpdatePropertiesCommand(Selected.ElementId, edit.Name, edit.Value));
    }

    /// <summary>Escape closes it, the same "never mind" as clearing a selection.</summary>
    /// <returns>A completed task.</returns>
    protected override Task DismissAsync()
    {
        Session.CloseContextMenu();
        return Task.CompletedTask;
    }
}
