using Hasseware.AspNetCore.Components.Bpmn.Editing;

using Microsoft.AspNetCore.Components;

namespace Hasseware.AspNetCore.Components.Bpmn;

/// <summary>
/// The logic of the diagram picker and drill-down; a UI kit supplies the markup.
/// </summary>
/// <remarks>
/// <para>
/// Two ways to move, because they are different intentions: the picker goes to a diagram the file
/// declares at the top level; the open button follows a Call Activity or a collapsed Sub-Process
/// into its content, which is the route that builds a breadcrumb.
/// </para>
/// <para>
/// Available in Readonly. Switching planes is looking, not editing.
/// </para>
/// </remarks>
public abstract class BpmnDiagramNavigatorBase : BpmnEditorComponentBase
{
    /// <inheritdoc />
    protected override bool ShouldRenderOnStateChange(BpmnEditorState previous, BpmnEditorState current) =>
        previous.Diagrams != current.Diagrams || previous.Selection != current.Selection
            || previous.ActiveDiagramId != current.ActiveDiagramId
            || !ReferenceEquals(previous.Document, current.Document)
            || !ReferenceEquals(previous.DrawnDocument, current.DrawnDocument);

    /// <summary>The group's accessible name; a localized default when not set.</summary>
    [Parameter]
    public string? AriaLabel { get; set; }

    /// <summary>The accessible name in force.</summary>
    protected string EffectiveAriaLabel => string.IsNullOrWhiteSpace(AriaLabel) ? Text("DiagramNavigator.AriaLabel") : AriaLabel;

    /// <summary>The diagrams to choose from.</summary>
    protected IReadOnlyList<BpmnDiagramTarget> Diagrams => Session.Diagrams;

    /// <summary>The diagram now shown.</summary>
    protected string? ActiveDiagramId => State.ActiveDiagramId;

    /// <summary>Where the selected element leads, or why it leads nowhere; none for no single selection.</summary>
    protected BpmnDrillTarget? Drill => State.Selection.Length == 1 ? Session.DrillTargetFor(State.Selection[0]) : null;

    /// <summary>Whether the selected element can be opened.</summary>
    protected bool CanDrill => Drill?.IsAvailable == true;

    /// <summary>The localized label of a diagram in the picker.</summary>
    /// <param name="target">The diagram.</param>
    /// <returns>Its label, marked when it is nested.</returns>
    protected string LabelOf(BpmnDiagramTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.IsRoot ? target.Label : $"{target.Label} {Text("DiagramNavigator.Nested")}";
    }

    /// <summary>The tooltip of the open button: where it leads, or why it does not.</summary>
    protected string OpenTitle => CanDrill
        ? Text("DiagramNavigator.OpenTarget", Drill?.DiagramId ?? string.Empty)
        : Drill is { } drill ? RefusalText(drill) : Text("DiagramNavigator.NothingToOpen");

    private string RefusalText(BpmnDrillTarget drill)
    {
        var called = State.DrawnDocument?.FindFlowNode(State.Selection[0])?.CalledElementRef ?? string.Empty;
        return drill.Refusal switch
        {
            BpmnDrillRefusal.NotDrillable => BuiltInReason(drill.Reason, "Only a Call Activity or a collapsed Sub-Process opens a diagram.", "DiagramNavigator.NotDrillable"),
            BpmnDrillRefusal.NothingCalled => BuiltInReason(drill.Reason, "This Call Activity does not say what it calls.", "DiagramNavigator.NothingCalled"),
            BpmnDrillRefusal.NoLocalDiagram when drill.Reason == $"'{called}' is called from here but is not drawn in this file." =>
                Text("DiagramNavigator.NoCalledDiagram", called),
            BpmnDrillRefusal.NoLocalDiagram => BuiltInReason(drill.Reason, "This Sub-Process has no diagram of its own; its content is drawn in place.", "DiagramNavigator.NoSubProcessDiagram"),
            _ => drill.Reason ?? Text("DiagramNavigator.NothingToOpen"),
        };
    }

    private string BuiltInReason(string? reason, string invariant, string key) =>
        reason is null || reason == invariant ? Text(key) : reason;

    /// <summary>Goes to a diagram the file declares.</summary>
    /// <param name="diagramId">The diagram the user picked.</param>
    /// <returns>A task that completes once the diagram is shown.</returns>
    protected Task SelectAsync(string? diagramId) => string.IsNullOrEmpty(diagramId)
        ? Task.CompletedTask
        : Session.GoToDiagramAsync(diagramId);

    /// <summary>Opens the diagram behind the selected element.</summary>
    /// <returns>A task that completes once the diagram is shown.</returns>
    protected async Task DrillAsync()
    {
        if (State.Selection.Length == 1) await Session.DrillIntoAsync(State.Selection[0]).ConfigureAwait(true);
    }
}
