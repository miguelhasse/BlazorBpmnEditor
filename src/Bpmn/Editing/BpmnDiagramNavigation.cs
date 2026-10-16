using Hasseware.AspNetCore.Components.Bpmn.Model;

namespace Hasseware.AspNetCore.Components.Bpmn.Editing;

/// <summary>
/// One diagram a document declares, named for a picker.
/// </summary>
/// <param name="DiagramId">The <c>BPMNDiagram</c> id.</param>
/// <param name="Label">What to call it: its own name, the name of what it draws, or its id.</param>
/// <param name="PlaneElementId">The element its plane draws — a Process, Collaboration or Sub-Process.</param>
/// <param name="IsRoot">
/// Whether this plane draws something the document declares at the top level, as opposed to the
/// inside of a collapsed Sub-Process.
/// </param>
public sealed record BpmnDiagramTarget(string DiagramId, string Label, string PlaneElementId, bool IsRoot);

/// <summary>
/// Why a drill-down is not on offer.
/// </summary>
/// <remarks>
/// A reason rather than a bare false. The exit criterion for this milestone is that drill-down is
/// "disabled with a reason when no local diagram exists", and the three reasons are genuinely
/// different to a user: this element never drills, this one would but names nothing, and this one
/// names something the file does not contain.
/// </remarks>
public enum BpmnDrillRefusal
{
    /// <summary>It can be drilled into.</summary>
    None,

    /// <summary>Not a Call Activity or a collapsed Sub-Process.</summary>
    NotDrillable,

    /// <summary>A Call Activity that names no called element.</summary>
    NothingCalled,

    /// <summary>What it names has no diagram in this document.</summary>
    NoLocalDiagram,
}

/// <summary>The outcome of asking to drill into an element.</summary>
/// <param name="DiagramId">The diagram to go to, when there is one.</param>
/// <param name="Refusal">Why not, when there is not.</param>
/// <param name="Reason">That refusal in words, for a disabled control's tooltip.</param>
public sealed record BpmnDrillTarget(string? DiagramId, BpmnDrillRefusal Refusal, string? Reason)
{
    /// <summary>Whether the drill-down can happen.</summary>
    public bool IsAvailable => DiagramId is not null;
}

/// <summary>
/// Which diagrams a document has, and which of them an element leads to.
/// </summary>
/// <remarks>
/// <para>
/// A BPMN file may draw more than one thing. Section 10.1 lets a <c>BPMNPlane</c> name any Process,
/// Collaboration or collapsed Sub-Process, and a Call Activity's content is a different Process
/// entirely — possibly one this file draws, possibly one it only names. Navigation is therefore a
/// question about the document rather than about the canvas, which is why it is answered here and
/// not on the paper.
/// </para>
/// <para>
/// Nothing here changes anything. Switching planes is a view operation: the semantic model, the
/// edit history and any unsaved work belong to the document, not to the plane being looked at, so
/// moving between them can neither lose nor commit anything.
/// </para>
/// </remarks>
public static class BpmnDiagramNavigation
{
    /// <summary>
    /// Every diagram in the document, in declaration order.
    /// </summary>
    /// <param name="document">The document to list.</param>
    /// <returns>One entry per <c>BPMNDiagram</c>, labelled for display.</returns>
    public static IReadOnlyList<BpmnDiagramTarget> Diagrams(BpmnDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return
        [
            .. document.Diagrams.Select(diagram =>
            {
                var planeElement = diagram.Plane.BpmnElement ?? string.Empty;
                var drawn = planeElement.Length == 0 ? null : document.Find(planeElement);

                return new BpmnDiagramTarget(
                    diagram.Id,
                    LabelFor(diagram, drawn, planeElement),
                    planeElement,
                    // A plane over a Sub-Process is the inside of something the user drills into;
                    // everything else is a diagram in its own right.
                    drawn is not BpmnFlowNode);
            }),
        ];
    }

    /// <summary>
    /// The diagram an element leads to, or why it leads nowhere.
    /// </summary>
    /// <remarks>
    /// Two elements lead somewhere: a Call Activity, whose content is the Process it names
    /// (section 6's <c>calledElementRef</c>), and a collapsed Sub-Process, whose content this file
    /// may draw on a plane of its own. Everything else is a leaf as far as navigation goes.
    /// </remarks>
    /// <param name="document">The document being navigated.</param>
    /// <param name="elementId">The element the user asked to open.</param>
    /// <returns>The target, or a refusal with the reason to show on the disabled control.</returns>
    public static BpmnDrillTarget DrillTargetFor(BpmnDocument document, string elementId)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.FindFlowNode(elementId) is not { } node)
        {
            return new BpmnDrillTarget(null, BpmnDrillRefusal.NotDrillable, "Only a Call Activity or a collapsed Sub-Process opens a diagram.");
        }

        if (node.Type == BpmnFlowNodeType.CallActivity)
        {
            if (node.CalledElementRef is not { Length: > 0 } called)
            {
                return new BpmnDrillTarget(
                    null,
                    BpmnDrillRefusal.NothingCalled,
                    "This Call Activity does not say what it calls.");
            }

            return DiagramDrawing(document, called)
                ?? new BpmnDrillTarget(
                    null,
                    BpmnDrillRefusal.NoLocalDiagram,
                    $"'{called}' is called from here but is not drawn in this file.");
        }

        if (IsSubProcess(node.Type))
        {
            // A collapsed Sub-Process keeps its content on a plane of its own; an expanded one has
            // its content on this plane already, and there is nowhere to go.
            return DiagramDrawing(document, elementId)
                ?? new BpmnDrillTarget(
                    null,
                    BpmnDrillRefusal.NoLocalDiagram,
                    "This Sub-Process has no diagram of its own; its content is drawn in place.");
        }

        return new BpmnDrillTarget(null, BpmnDrillRefusal.NotDrillable, "Only a Call Activity or a collapsed Sub-Process opens a diagram.");
    }

    /// <summary>The diagram whose plane draws a given element, if the document has one.</summary>
    /// <param name="document">The document to search.</param>
    /// <param name="elementId">The element the plane should be drawing.</param>
    /// <returns>The diagram id, or <see langword="null"/>.</returns>
    public static string? DiagramDrawingElement(BpmnDocument document, string elementId)
    {
        ArgumentNullException.ThrowIfNull(document);

        return document.Diagrams
            .FirstOrDefault(diagram => string.Equals(diagram.Plane.BpmnElement, elementId, StringComparison.Ordinal))
            ?.Id;
    }

    /// <summary>
    /// The diagram that draws the element a usage or a selection names.
    /// </summary>
    /// <remarks>
    /// Not the same question as <see cref="DiagramDrawingElement"/>: this one asks which plane the
    /// element appears <em>on</em>, which is what a reference browser needs to send the user to the
    /// right plane before selecting anything.
    /// </remarks>
    /// <param name="document">The document to search.</param>
    /// <param name="elementId">The element to find.</param>
    /// <returns>The diagram id, or <see langword="null"/> when nothing draws it.</returns>
    public static string? DiagramShowing(BpmnDocument document, string elementId)
    {
        ArgumentNullException.ThrowIfNull(document);

        return document.Diagrams
            .FirstOrDefault(diagram => diagram.Plane.Shapes.Any(shape => shape.BpmnElement == elementId)
                || diagram.Plane.Edges.Any(edge => edge.BpmnElement == elementId))
            ?.Id;
    }

    private static BpmnDrillTarget? DiagramDrawing(BpmnDocument document, string elementId) =>
        DiagramDrawingElement(document, elementId) is { } diagramId
            ? new BpmnDrillTarget(diagramId, BpmnDrillRefusal.None, null)
            : null;

    private static bool IsSubProcess(BpmnFlowNodeType type) => type
        is BpmnFlowNodeType.SubProcess
        or BpmnFlowNodeType.EventSubProcess
        or BpmnFlowNodeType.Transaction
        or BpmnFlowNodeType.AdHocSubProcess;

    /// <summary>
    /// What to call a diagram.
    /// </summary>
    /// <remarks>
    /// Its own name if it has one, because that is what the author chose to call it. Otherwise the
    /// name of whatever it draws, which is nearly always more useful than either id — a picker
    /// listing four generated ids tells the user nothing about which plane is which.
    /// </remarks>
    private static string LabelFor(Model.Di.BpmnDiagram diagram, object? drawn, string planeElement)
    {
        if (diagram.Name is { Length: > 0 } named) return named;

        var drawnName = drawn switch
        {
            BpmnProcess process => process.Name,
            BpmnCollaboration collaboration => collaboration.Name,
            BpmnFlowNode node => node.Name,
            _ => null,
        };

        if (drawnName is { Length: > 0 } label) return label;

        return planeElement.Length > 0 ? planeElement : diagram.Id;
    }
}
