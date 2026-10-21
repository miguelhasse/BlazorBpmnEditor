using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Interop;
using Hasseware.AspNetCore.Components.Bpmn.Model;

namespace Hasseware.AspNetCore.Components.Bpmn;

/// <summary>
/// The logic of the quick-action strip beside a selected element; a UI kit supplies the markup.
/// </summary>
/// <remarks>
/// Which buttons appear comes from the element's own <see cref="BpmnElementSnapshot"/>, never from
/// the selection alone: an End Event has nothing that can follow it, and a Readonly snapshot
/// permits none of them. Both facts are already in the capability flags.
/// </remarks>
public abstract class BpmnContextualActionsBase : BpmnCanvasWidgetBase<BpmnContextualActionsState>
{
    /// <inheritdoc />
    protected override bool ShouldRenderOnStateChange(BpmnEditorState previous, BpmnEditorState current) =>
        !ReferenceEquals(previous.Overlays.ContextualActions, current.Overlays.ContextualActions);

    /// <summary>
    /// The gap between the element and the strip, in client pixels.
    /// </summary>
    /// <remarks>
    /// Wide enough to clear the connector-initiation handle, which JointJS draws 12px past the
    /// right edge with a 7px radius. The strip is an HTML element over the SVG, so any overlap
    /// would swallow the pointer-down that starts a connector drag.
    /// </remarks>
    protected const double Gap = 26d;

    /// <inheritdoc />
    protected override BpmnContextualActionsState? WidgetState => State.Overlays.ContextualActions;

    /// <inheritdoc />
    protected override bool IsOpen => WidgetState?.IsOpen == true;

    /// <inheritdoc />
    protected override Anchor? Anchor => WidgetState?.Anchor;

    /// <inheritdoc />
    protected override bool FocusOnOpen => false;

    /// <inheritdoc />
    protected override string DefaultAriaLabel => Text("ContextualActions.AriaLabel");

    /// <summary>The selected element's properties, which decide what is offered.</summary>
    protected BpmnElementSnapshot? Selected => WidgetState?.Snapshot;

    /// <summary>The buttons to show, in order.</summary>
    protected IReadOnlyList<BpmnContextualAction> Offered => WidgetState?.Actions ?? [];

    /// <summary>
    /// Placed immediately right of the element, top-aligned with it.
    /// </summary>
    /// <remarks>
    /// The right is where the appended element will go, so the affordance points at its own
    /// result. Top-aligned because the strip is taller than an Event.
    /// </remarks>
    protected string PositionStyle => AnchorStyle(client => client.X + client.Width + Gap, client => client.Y);

    /// <summary>The localized label of a built-in action; already localized and host labels are kept.</summary>
    /// <param name="action">The action.</param>
    /// <returns>The accessible name and tooltip.</returns>
    protected string LabelFor(BpmnContextualAction action) => Display.ContextualAction(action);

    /// <summary>Runs an action against the selected element.</summary>
    /// <param name="action">The action.</param>
    /// <returns>A task that completes once the action has run.</returns>
    protected Task ChooseAsync(BpmnContextualAction action) => Session.InvokeContextualActionAsync(action);

    /// <summary>
    /// The default set for an element, given what its snapshot says may be done to it.
    /// </summary>
    /// <remarks>
    /// Static so a host can take this list, filter it and pass its own. Labels are read from the
    /// built-in resources for the current UI culture; the neutral resources are the English defaults.
    /// </remarks>
    /// <param name="snapshot">The selected element's snapshot.</param>
    /// <returns>The actions to offer, which is empty when none apply.</returns>
    public static IReadOnlyList<BpmnContextualAction> DefaultsFor(BpmnElementSnapshot? snapshot) =>
        DefaultsFor(snapshot, null);

    /// <summary>The default set for an element, with labels localized by <paramref name="display"/>.</summary>
    /// <param name="snapshot">The selected element's snapshot.</param>
    /// <param name="display">The caption localizer, or <see langword="null"/> for the current UI culture's resources.</param>
    /// <returns>The actions to offer, which is empty when none apply.</returns>
    public static IReadOnlyList<BpmnContextualAction> DefaultsFor(BpmnElementSnapshot? snapshot, BpmnDisplayLocalizer? display)
    {
        display ??= BpmnDisplayLocalizer.CurrentCulture;
        if (snapshot is null || snapshot.ElementType == "group") return [];

        var actions = new List<BpmnContextualAction>();

        // Nothing follows an End Event, so offering to append after one would offer a Sequence Flow
        // the connection matrix refuses (section 5.1).
        var appendable = snapshot.Capabilities.CanConnect
            && snapshot.Category is BpmnElementCategory.Event
                or BpmnElementCategory.Activity
                or BpmnElementCategory.Gateway
            && snapshot.ElementType != "endEvent";

        if (appendable)
        {
            actions.Add(Action(display, BpmnActionKind.Append, "▭", BpmnFlowNodeType.Task));
            actions.Add(Action(display, BpmnActionKind.Append, "◇", BpmnFlowNodeType.ExclusiveGateway));
            actions.Add(Action(display, BpmnActionKind.Append, "○", BpmnFlowNodeType.EndEvent));
        }

        if ((snapshot.Capabilities.CanConnect || snapshot.ElementType == "lane" && snapshot.Capabilities.CanEditLabel)
            && snapshot.Bounds is not null)
        {
            actions.Add(Action(display, BpmnActionKind.AddTextAnnotation, "["));
        }

        if (snapshot.Capabilities.CanEditLabel)
        {
            actions.Add(Action(display, BpmnActionKind.EditLabel, "✎"));
        }

        if (snapshot.Capabilities.CanDelete)
        {
            actions.Add(Action(display, BpmnActionKind.Delete, "✕"));
        }

        if (actions.Count > 0)
        {
            actions.Add(Action(display, BpmnActionKind.OpenMenu, "⋯"));
        }

        return actions;
    }

    private static BpmnContextualAction Action(
        BpmnDisplayLocalizer display,
        BpmnActionKind kind,
        string glyph,
        BpmnFlowNodeType type = BpmnFlowNodeType.Task) =>
        new(kind, display.ContextualAction(kind, type), glyph, type);
}
