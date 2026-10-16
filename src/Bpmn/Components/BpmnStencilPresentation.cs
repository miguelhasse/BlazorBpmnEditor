using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Icons;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions.Templates;

namespace Hasseware.AspNetCore.Components.Bpmn;

/// <summary>An entry's shape: the border that says what it is, and any second stroke it needs.</summary>
/// <param name="Outline">The main border, as SVG path data in a 24-unit box.</param>
/// <param name="Detail">A second stroke — an Event's inner ring, a Pool's header band.</param>
public readonly record struct BpmnStencilShape(string Outline, string? Detail = null)
{
    /// <summary>A dashed outline for an Event Sub-Process, or a solid outline when absent.</summary>
    public string? OutlineDashArray { get; init; }
}

/// <summary>
/// The headless presentation rules of the element palette: which outline, glyph, key and tooltip a
/// stencil entry gets.
/// </summary>
/// <remarks>
/// Kept apart from any component so a second UI kit renders the palette from markup alone and
/// draws exactly the shapes the canvas notation calls for.
/// </remarks>
public static class BpmnStencilPresentation
{
    private const string Activity =
        "M 4 6 h 16 a 2 2 0 0 1 2 2 v 8 a 2 2 0 0 1 -2 2 h -16 a 2 2 0 0 1 -2 -2 v -8 a 2 2 0 0 1 2 -2 Z";

    private const string CollapsedMarker = "M 10 12 h 4 v 4 h -4 Z M 12 13 v 2 M 11 14 h 2";

    private static BpmnIconRegistry Icons => BpmnIconRegistry.Default;

    /// <summary>
    /// A stable key, so re-rendering a recomputed stencil does not rebuild every tool.
    /// </summary>
    /// <remarks>
    /// The type alone is not unique: a Start Event appears several times with different triggers,
    /// and the disabled data and artifact entries all borrow <c>Task</c> until they have commands
    /// of their own.
    /// </remarks>
    /// <param name="group">The group the entry is in.</param>
    /// <param name="entry">The entry.</param>
    /// <returns>The key.</returns>
    public static string KeyOf(BpmnStencilGroup group, BpmnStencilEntry entry)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(entry);

        return $"{group.Title}/{entry.Template?.Id}/{entry.Template?.Version}/{entry.Type}/{entry.EventDefinition}/{entry.Label}";
    }

    /// <summary>The safe icon URL a template names, or <see langword="null"/>.</summary>
    /// <param name="entry">The entry.</param>
    /// <returns>The trimmed URL when it is safe to render.</returns>
    public static string? TemplateIcon(BpmnStencilEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return BpmnTemplateIcons.IsSafe(entry.Template?.Icon) ? entry.Template!.Icon!.Trim() : null;
    }

    /// <summary>
    /// The tooltip for a tool.
    /// </summary>
    /// <remarks>
    /// A host's description when there is one, because a host that bothered to write one knows its
    /// own users better than this label does. The reason a tool is unavailable always wins: that is
    /// the more urgent thing to say, and section 9.3 requires it to be said at all.
    /// </remarks>
    /// <param name="entry">The entry.</param>
    /// <param name="unavailable">Formats the unavailable text from the entry's name and reason.</param>
    /// <returns>The tooltip.</returns>
    public static string TitleOf(BpmnStencilEntry entry, Func<string, string?, string> unavailable)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(unavailable);

        var name = entry.Description ?? entry.Label;
        return entry.IsEnabled ? name : unavailable(name, entry.DisabledReason);
    }

    /// <summary>
    /// The manifest glyph for an entry, when one applies.
    /// </summary>
    /// <remarks>
    /// A host's chosen glyph first. It is looked up in the same manifest the canvas draws from, so
    /// an override names a glyph that exists rather than supplying artwork of its own, and one that
    /// does not exist falls back to the entry's own rather than drawing nothing.
    /// </remarks>
    /// <param name="entry">The entry.</param>
    /// <returns>The glyph, or <see langword="null"/>.</returns>
    public static BpmnIcon? IconFor(BpmnStencilEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.IconId is { Length: > 0 } chosen && Icons.Find(chosen) is { } overridden)
        {
            return overridden;
        }

        if (entry.EventDefinition != BpmnEventDefinitionType.None)
        {
            return Icons.ForEventTrigger(EventContextOf(entry.Type), entry.EventDefinition);
        }

        return Icons.ForGateway(entry.Type) ?? Icons.ForTaskType(entry.Type);
    }

    /// <summary>The context name the icon manifest keys event triggers by.</summary>
    /// <param name="type">The event's type.</param>
    /// <returns>The context name.</returns>
    public static string EventContextOf(BpmnFlowNodeType type) => type switch
    {
        BpmnFlowNodeType.StartEvent => "start",
        BpmnFlowNodeType.IntermediateCatchEvent => "intermediateCatch",
        BpmnFlowNodeType.IntermediateThrowEvent => "intermediateThrow",
        BpmnFlowNodeType.BoundaryEvent => "boundary",
        BpmnFlowNodeType.EndEvent => "end",
        _ => "start",
    };

    /// <summary>
    /// The outline for an entry, in the icon's 24-unit box.
    /// </summary>
    /// <remarks>
    /// Kind before type, because a Data Object and a Text Annotation are entries whose
    /// <c>Type</c> is a placeholder the command never reads: switching on it alone drew them as
    /// Tasks.
    /// </remarks>
    /// <param name="entry">The entry.</param>
    /// <returns>The shape.</returns>
    public static BpmnStencilShape Silhouette(BpmnStencilEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return entry.Kind switch
        {
            BpmnStencilKind.Participant or BpmnStencilKind.Lane or BpmnStencilKind.PoolOrLane => new(
                "M 2 5 h 20 v 14 h -20 Z",
                // The header band, which is what distinguishes a Pool from any other rectangle.
                "M 7 5 v 14"),

            BpmnStencilKind.Data => entry.Data == BpmnDataKind.DataStore
                // A cylinder: an ellipse on top of an open-sided body.
                ? new("M 4 7 v 10 c 0 1.7 3.6 3 8 3 s 8 -1.3 8 -3 v -10 Z", "M 4 7 c 0 1.7 3.6 3 8 3 s 8 -1.3 8 -3 s -3.6 -3 -8 -3 s -8 1.3 -8 3 Z")
                // A page with a folded corner.
                : new("M 7 3 h 7 l 4 4 v 14 h -11 Z", "M 14 3 v 4 h 4"),

            BpmnStencilKind.Artifact => entry.Artifact == BpmnArtifactKind.Group
                ? new("M 3 5 h 18 v 14 h -18 Z")
                // A Text Annotation is its bracket and nothing else.
                : new("M 10 4 h -4 v 16 h 4"),

            BpmnStencilKind.BoundaryEvent => new(
                "M 12 2 A 10 10 0 1 0 12 22 A 10 10 0 1 0 12 2 Z",
                // The second ring: a Boundary Event is always a double circle.
                "M 12 4.5 A 7.5 7.5 0 1 0 12 19.5 A 7.5 7.5 0 1 0 12 4.5 Z"),

            _ => FlowNodeSilhouette(entry.Type),
        };
    }

    /// <summary>
    /// Where the manifest glyph goes inside the silhouette.
    /// </summary>
    /// <remarks>
    /// Glyphs are drawn in their own 24-unit box, so each family places one where the canvas does:
    /// centred in an Event or a Gateway, and in the top-left corner of an Activity. Scaled larger
    /// than the canvas proportion, deliberately: a glyph's ink fills only part of its box, so at the
    /// canvas ratio a Gateway's symbol was true to the notation and indistinguishable from an empty
    /// diamond.
    /// </remarks>
    /// <param name="entry">The entry.</param>
    /// <returns>An SVG transform.</returns>
    public static string GlyphTransform(BpmnStencilEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.Kind is BpmnStencilKind.Data or BpmnStencilKind.Artifact
            or BpmnStencilKind.Participant or BpmnStencilKind.Lane)
        {
            return "translate(7.2 7.2) scale(0.4)";
        }

        return BpmnElementInspector.CategoryOf(entry.Type) switch
        {
            BpmnElementCategory.Event => "translate(3.6 3.6) scale(0.7)",
            BpmnElementCategory.Gateway => "translate(3.6 3.6) scale(0.7)",
            _ => "translate(2.6 6.6) scale(0.45)",
        };
    }

    private static BpmnStencilShape FlowNodeSilhouette(BpmnFlowNodeType type) => type switch
    {
        BpmnFlowNodeType.StartEvent => new("M 12 2 A 10 10 0 1 0 12 22 A 10 10 0 1 0 12 2 Z"),

        // A catching or throwing Intermediate Event is drawn as two rings; an End Event as one
        // thick ring, which at this size reads as the same circle the canvas draws.
        BpmnFlowNodeType.IntermediateCatchEvent or BpmnFlowNodeType.IntermediateThrowEvent => new(
            "M 12 2 A 10 10 0 1 0 12 22 A 10 10 0 1 0 12 2 Z",
            "M 12 4.5 A 7.5 7.5 0 1 0 12 19.5 A 7.5 7.5 0 1 0 12 4.5 Z"),

        BpmnFlowNodeType.EndEvent => new("M 12 2.5 A 9.5 9.5 0 1 0 12 21.5 A 9.5 9.5 0 1 0 12 2.5 Z"),

        BpmnFlowNodeType.ExclusiveGateway or BpmnFlowNodeType.InclusiveGateway
            or BpmnFlowNodeType.ParallelGateway or BpmnFlowNodeType.ComplexGateway
            or BpmnFlowNodeType.EventBasedGateway =>
            new("M 12 2 L 22 12 L 12 22 L 2 12 Z"),

        BpmnFlowNodeType.SubProcess => new(Activity, CollapsedMarker),

        BpmnFlowNodeType.EventSubProcess => new(Activity, CollapsedMarker) { OutlineDashArray = "2 1" },

        BpmnFlowNodeType.Transaction => new(Activity,
            "M 5 7.5 h 14 a 1.5 1.5 0 0 1 1.5 1.5 v 6 a 1.5 1.5 0 0 1 -1.5 1.5 h -14 a 1.5 1.5 0 0 1 -1.5 -1.5 v -6 a 1.5 1.5 0 0 1 1.5 -1.5 Z "
            + CollapsedMarker),

        BpmnFlowNodeType.AdHocSubProcess => new(Activity,
            "M 7 12 h 4 v 4 h -4 Z M 9 13 v 2 M 8 14 h 2 "
            + "M 13 14 c 1 -2 2 2 3 0 s 2 -2 3 0"),

        // A Call Activity is distinguished by its thick border rather than a glyph.
        BpmnFlowNodeType.CallActivity => new(
            Activity,
            "M 5.5 7.5 h 13 a 1 1 0 0 1 1 1 v 7 a 1 1 0 0 1 -1 1 h -13 a 1 1 0 0 1 -1 -1 v -7 a 1 1 0 0 1 1 -1 Z"),

        _ => new(Activity),
    };
}
